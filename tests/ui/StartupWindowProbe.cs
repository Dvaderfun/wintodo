// Used only by run-startup-placement-gate.ps1 in the disposable test guest.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

public sealed class StartupWindowSample
{
    public double Ms { get; set; }
    public long Hwnd { get; set; }
    public bool Visible { get; set; }
    public bool Maximized { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool SameRect(StartupWindowSample other)
    {
        return other != null && X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;
    }
}

public sealed class StartupWindowProbe
{
    struct Rect { public int Left, Top, Right, Bottom; }
    delegate bool EnumProc(IntPtr hwnd, IntPtr parameter);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr parameter);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder name, int capacity);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

    public static StartupWindowSample Read(IntPtr hwnd)
    {
        var previous = SetThreadDpiAwarenessContext(new IntPtr(-4)); // Physical pixels.
        try
        {
            var visible = IsWindowVisible(hwnd); var zoomed = IsZoomed(hwnd); Rect r;
            if (!GetWindowRect(hwnd, out r) || visible != IsWindowVisible(hwnd) || zoomed != IsZoomed(hwnd)) return null;
            return new StartupWindowSample { Hwnd = hwnd.ToInt64(), Visible = visible, Maximized = zoomed,
                X = r.Left, Y = r.Top, Width = r.Right - r.Left, Height = r.Bottom - r.Top };
        }
        finally { SetThreadDpiAwarenessContext(previous); }
    }

    public static void SetNormalBounds(IntPtr hwnd)
    {
        var previous = SetThreadDpiAwarenessContext(new IntPtr(-4));
        try
        {
            if (!SetWindowPos(hwnd, IntPtr.Zero, 120, 96, 1432, 912, 0x14)) throw new Exception("SetWindowPos failed.");
        }
        finally { SetThreadDpiAwarenessContext(previous); }
    }

    public static void RequestMaximized(IntPtr hwnd, bool maximized)
    {
        // Same system commands as Maximize and Restore Down in the title bar.
        if (!PostMessage(hwnd, 0x112, new IntPtr(maximized ? 0xf030 : 0xf120), IntPtr.Zero))
            throw new Exception("Window system command failed.");
    }

    readonly string exe;
    readonly Thread thread;
    readonly Stopwatch watch = new Stopwatch();
    readonly ManualResetEventSlim ready = new ManualResetEventSlim(), start = new ManualResetEventSlim();
    readonly List<StartupWindowSample> changes = new List<StartupWindowSample>();
    volatile bool stop;
    volatile int pid;
    long visibleHwnd;
    public int ProcessId { get { return pid; } }
    public long Hwnd { get { return Interlocked.Read(ref visibleHwnd); } }
    public double Ms { get { return watch.Elapsed.TotalMilliseconds; } }
    public double ProcessSeenMs { get; private set; }
    public double MaxWindowPollGapMs { get; private set; }
    public int Polls { get; private set; }
    public string Error { get; private set; }
    public StartupWindowSample[] Changes { get { return changes.ToArray(); } } // Read only after End().
    public StartupWindowProbe(string exe)
    {
        this.exe = exe;
        thread = new Thread(Observe) { IsBackground = true };
        thread.Start(); ready.Wait();
    }
    public void Begin() { watch.Start(); start.Set(); }
    public void End() { stop = true; start.Set(); if (!thread.Join(5000)) throw new TimeoutException("Window probe did not stop."); }

    void Observe()
    {
        Process app = null;
        IntPtr hwnd = IntPtr.Zero;
        ready.Set(); start.Wait();
        try
        {
            double last = 0;
            while (!stop)
            {
                double now = Ms;
                if (Polls++ > 0) MaxWindowPollGapMs = Math.Max(MaxWindowPollGapMs, now - last);
                last = now;
                if (app == null)
                {
                    foreach (var candidate in Process.GetProcessesByName("Wintodo"))
                    {
                        try
                        {
                            if (app == null && string.Equals(exe, candidate.MainModule.FileName, StringComparison.OrdinalIgnoreCase))
                            { app = candidate; pid = app.Id; ProcessSeenMs = Ms; }
                        }
                        catch (System.ComponentModel.Win32Exception) { }
                        catch (InvalidOperationException) { }
                        finally { if (candidate != app) candidate.Dispose(); }
                    }
                }
                if (app != null)
                {
                    if (app.HasExited) throw new InvalidOperationException("Installed app exited during launch.");
                    if (hwnd == IntPtr.Zero)
                        EnumWindows(delegate(IntPtr h, IntPtr unused) {
                            int id; GetWindowThreadProcessId(h, out id); if (id != pid) return true;
                            var name = new StringBuilder(128); GetClassName(h, name, name.Capacity);
                            if (name.ToString() != "WinUIDesktopWin32WindowClass") return true;
                            hwnd = h; return false;
                        }, IntPtr.Zero);
                    var sample = hwnd == IntPtr.Zero ? null : Read(hwnd);
                    if (sample != null)
                    {
                        sample.Ms = Ms;
                        var prior = changes.Count == 0 ? null : changes[changes.Count - 1];
                        if (!sample.SameRect(prior) || prior.Visible != sample.Visible || prior.Maximized != sample.Maximized)
                            changes.Add(sample);
                        if (sample.Visible) Interlocked.Exchange(ref visibleHwnd, sample.Hwnd);
                    }
                }
                Thread.Sleep(5);
            }
        }
        catch (Exception e) { Error = e.Message; }
        finally { if (app != null) app.Dispose(); }
    }
}
