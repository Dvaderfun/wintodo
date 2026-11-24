// Used only by run-startup-placement-gate.ps1 in the disposable test guest.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

public sealed class StartupWindowSample
{
    public double Ms { get; set; }
    public long Hwnd { get; set; }
    public bool Visible { get; set; }
    // Win32 WS_VISIBLE and DWM visibility are independent. Null if not requested/available.
    public uint? DwmCloakFlags { get; set; }
    public int? DwmCloakQueryHResult { get; set; }
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
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    static extern bool GetClientAnimationSetting(uint action, uint parameter, out int value, uint flags);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    static extern bool SetClientAnimationSetting(uint action, uint parameter, IntPtr value, uint flags);

    public static bool ClientAnimationsEnabled()
    {
        int value;
        if (!GetClientAnimationSetting(0x1042 /* SPI_GETCLIENTAREAANIMATION */, 0, out value, 0))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return value != 0;
    }

    public static void SetClientAnimations(bool enabled)
    {
        if (ClientAnimationsEnabled() == enabled) return;
        if (!SetClientAnimationSetting(0x1043 /* SPI_SETCLIENTAREAANIMATION */, 0, new IntPtr(enabled ? 1 : 0), 2 /* SPIF_SENDCHANGE */))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        if (ClientAnimationsEnabled() != enabled) throw new InvalidOperationException("Guest animation setting did not change.");
    }
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out uint value, int size);

    public static StartupWindowSample Read(IntPtr hwnd) { return Read(hwnd, false); }
    static StartupWindowSample Read(IntPtr hwnd, bool inspectCloaking)
    {
        var previous = SetThreadDpiAwarenessContext(new IntPtr(-4)); // Physical pixels.
        try
        {
            var visible = IsWindowVisible(hwnd); var zoomed = IsZoomed(hwnd); Rect r;
            int? result = null; uint? flags = null;
            if (inspectCloaking) {
                uint value;
                result = DwmGetWindowAttribute(hwnd, 14 /* DWMWA_CLOAKED */, out value, sizeof(uint));
                if (result >= 0) flags = value;
            }
            if (!GetWindowRect(hwnd, out r) || visible != IsWindowVisible(hwnd) || zoomed != IsZoomed(hwnd)) return null;
            return new StartupWindowSample { Hwnd = hwnd.ToInt64(), Visible = visible, Maximized = zoomed,
                DwmCloakFlags = flags, DwmCloakQueryHResult = result,
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
    readonly bool inspectCloaking;
    readonly string recoveryStaging;
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
    public double? LastStagingPresentMs { get; private set; }
    public double? FirstStagingRemovedMs { get; private set; }
    public int Polls { get; private set; }
    public string Error { get; private set; }
    public StartupWindowSample[] Changes { get { return changes.ToArray(); } } // Read only after End().
    public StartupWindowProbe(string exe, bool inspectCloaking, string recoveryStaging)
    {
        this.exe = exe;
        this.inspectCloaking = inspectCloaking;
        this.recoveryStaging = recoveryStaging;
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
                if (!String.IsNullOrEmpty(recoveryStaging) && !FirstStagingRemovedMs.HasValue)
                {
                    if (Directory.Exists(recoveryStaging)) LastStagingPresentMs = Ms;
                    else FirstStagingRemovedMs = Ms;
                }
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
                    var sample = hwnd == IntPtr.Zero ? null : Read(hwnd, inspectCloaking);
                    if (sample != null)
                    {
                        sample.Ms = Ms;
                        var prior = changes.Count == 0 ? null : changes[changes.Count - 1];
                        if (!sample.SameRect(prior) || prior.Visible != sample.Visible || prior.Maximized != sample.Maximized ||
                            prior.DwmCloakFlags != sample.DwmCloakFlags || prior.DwmCloakQueryHResult != sample.DwmCloakQueryHResult)
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
