// Diagnostic only: instantiated by the VM-only signed placement gate when -CaptureFrames is requested.
// Reads the composited guest desktop, including frames drawn before WinUI's first Rendering event.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

public sealed class StartupScreenCapture : IDisposable
{
    sealed class Frame { public byte[] Pixels; public double First, Last; public int Count; }
    readonly List<Frame> frames = new List<Frame>();
    readonly ManualResetEventSlim ready = new ManualResetEventSlim(), start = new ManualResetEventSlim();
    readonly Stopwatch clock = new Stopwatch();
    readonly Thread thread;
    volatile bool stop;
    Exception error;
    int w, h;
    public int Samples { get; private set; }
    public double MaxGapMs { get; private set; }
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("gdi32.dll")] static extern int SetStretchBltMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")] static extern bool StretchBlt(IntPtr target, int x, int y, int w, int h,
        IntPtr source, int sx, int sy, int sw, int sh, int op);
    public StartupScreenCapture()
    {
        thread = new Thread(Capture) { IsBackground = true };
        thread.Start();
        if (!ready.Wait(10000)) throw new TimeoutException("Guest screen capture did not initialize.");
        if (error != null) throw new IOException("Guest screen capture failed.", error);
    }
    public void Begin() { clock.Start(); start.Set(); }
    void Capture()
    {
        IntPtr prev = SetThreadDpiAwarenessContext(new IntPtr(-4)), dc = IntPtr.Zero;
        try {
            int sw = GetSystemMetrics(0), sh = GetSystemMetrics(1);
            if (sw < 1000 || sh < 700) throw new InvalidOperationException("Unexpected test display.");
            w = sw / 4; h = sh / 4;
            dc = GetDC(IntPtr.Zero);
            if (dc == IntPtr.Zero) throw new IOException("Guest desktop DC unavailable.");
            using (var bmp = new Bitmap(w,h,PixelFormat.Format32bppRgb))
            using (var g = Graphics.FromImage(bmp)) {
                double prior = 0;
                var bytes = new byte[w*h*4];
                do {
                    IntPtr dst = g.GetHdc();
                    try {
                        SetStretchBltMode(dst, 3); // COLORONCOLOR; sample native desktop pixels at 1:4 scale.
                        if (!StretchBlt(dst,0,0,w,h,dc,0,0,sw,sh,0x40CC0020)) throw new IOException("Guest screen copy failed.");
                    } finally { g.ReleaseHdc(dst); }
                    var data = bmp.LockBits(new Rectangle(0,0,w,h),ImageLockMode.ReadOnly,PixelFormat.Format32bppRgb);
                    try { Marshal.Copy(data.Scan0,bytes,0,bytes.Length); } finally { bmp.UnlockBits(data); }
                    double t = clock.Elapsed.TotalMilliseconds;
                    if (Samples > 0) MaxGapMs = Math.Max(MaxGapMs,t-prior);
                    Samples++; prior = t;
                    Frame f = frames.Count == 0 ? null : frames[frames.Count-1];
                    if (f == null || !Equal(f.Pixels,bytes)) { f = new Frame { Pixels=(byte[])bytes.Clone(), First=t }; frames.Add(f); }
                    f.Last=t; f.Count++;
                    if (!ready.IsSet) { ready.Set(); start.Wait(); }
                    if (t >= 30000) break;
                    Thread.Sleep(t < 5000 ? 10 : 125);
                } while (!stop);
            }
        } catch (Exception e) { error = e; }
        finally { if (dc != IntPtr.Zero) ReleaseDC(IntPtr.Zero,dc); SetThreadDpiAwarenessContext(prev); ready.Set(); }
    }
    static bool Equal(byte[] a, byte[] b) { for (int i=0;i<a.Length;i++) if (a[i]!=b[i]) return false; return true; }
    public void Save(string dir)
    {
        stop=true; start.Set();
        if (!thread.Join(5000)) throw new TimeoutException("Guest capture did not stop.");
        if (error != null) throw new IOException("Guest capture failed.",error);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir,"sampling.json"),
            "{" + String.Format(CultureInfo.InvariantCulture,"\"width\":{0},\"height\":{1},\"samples\":{2},\"maxSampleGapMs\":{3:F3}",
                w,h,Samples,MaxGapMs) + "}");
        using (var csv = new StreamWriter(Path.Combine(dir,"frames.csv"))) {
            csv.WriteLine("image,firstMs,lastMs,samples");
            for (int i=0;i<frames.Count;i++) {
                Frame f=frames[i];
                string name=String.Format(CultureInfo.InvariantCulture,"{0:D4}.png",i);
                using (var b = new Bitmap(w,h,PixelFormat.Format32bppRgb)) {
                    var data=b.LockBits(new Rectangle(0,0,w,h),ImageLockMode.WriteOnly,PixelFormat.Format32bppRgb);
                    try { Marshal.Copy(f.Pixels,0,data.Scan0,f.Pixels.Length); } finally { b.UnlockBits(data); }
                    b.Save(Path.Combine(dir,name),ImageFormat.Png);
                }
                csv.WriteLine(String.Format(CultureInfo.InvariantCulture,"{0},{1:F3},{2:F3},{3}",name,f.First,f.Last,f.Count));
            }
        }
        frames.Clear();
    }
    public void Dispose() { stop=true; start.Set(); if (thread.Join(5000)) { ready.Dispose(); start.Dispose(); } }
}
