using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

// Keep pulse count, direction and cadence fixed; vary wheel displacement per pulse.
// UIA supplies physical screen coordinates inside the task list.
public static class RapidScrollWheel
{
    [StructLayout(LayoutKind.Sequential)] struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct MouseInput
    {
        public int X, Y, Data;
        public uint Flags, Time;
        public UIntPtr Extra;
    }
    [StructLayout(LayoutKind.Sequential)] struct Input { public uint Type; public MouseInput Mouse; }

    [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint count, Input[] input, int size);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    static extern bool ReadSystemParameter(uint action, uint unused, out uint value, uint flags);

    public static uint GetSystemWheelScrollLines()
    {
        const uint SpiGetWheelScrollLines = 0x0068;
        if (!ReadSystemParameter(SpiGetWheelScrollLines, 0, out uint lines, 0))
            throw new InvalidOperationException("Cannot read the Windows wheel setting: " + Marshal.GetLastWin32Error());
        return lines;
    }

    static void CheckTarget(uint expectedPid, Point point)
    {
        uint foregroundPid, hoverPid;
        GetWindowThreadProcessId(GetForegroundWindow(), out foregroundPid);
        GetWindowThreadProcessId(WindowFromPoint(point), out hoverPid);
        if (foregroundPid != expectedPid || hoverPid != expectedPid)
            throw new InvalidOperationException("The foreground window and wheel target must both belong to Wintodo.");
    }

    public static int Run(int expectedPid, int screenX, int screenY, int notchesPerPulse)
        => Send(expectedPid, screenX, screenY, notchesPerPulse, new[] { -48, 28, -64 });

    // Positive wheel deltas scroll up; a distance check sends at most four packets per leg.
    public static int SendBurst(int expectedPid, int screenX, int screenY, int signedNotchesPerPacket, int packets)
    {
        if (signedNotchesPerPacket is not (-8 or -1 or 1 or 8) || packets < 1 || packets > 4)
            throw new ArgumentOutOfRangeException(nameof(packets), "Expected 1–4 packets of ±1 or ±8 notches.");
        return Send(expectedPid, screenX, screenY, Math.Abs(signedNotchesPerPacket),
            new[] { Math.Sign(signedNotchesPerPacket) * packets });
    }

    static int Send(int expectedPid, int screenX, int screenY, int notchesPerPulse, int[] bursts)
    {
        if (notchesPerPulse != 1 && notchesPerPulse != 8)
            throw new ArgumentOutOfRangeException("notchesPerPulse", "Expected one or eight notches per pulse.");
        if (Environment.GetEnvironmentVariable("WINTODO_ALLOW_LOCAL_UI") != "1" ||
            !string.Equals(Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Virtual Machine\Guest\Parameters",
                "VirtualMachineName", null) as string, "WintodoTest", StringComparison.Ordinal))
            throw new InvalidOperationException("Wheel input is restricted to WintodoTest.");
        using (var app = Process.GetProcessById(expectedPid))
            if (!string.Equals(app.ProcessName, "Wintodo", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Expected the Wintodo process.");

        var priorDpi = SetThreadDpiAwarenessContext(new IntPtr(-4));
        if (priorDpi == IntPtr.Zero) throw new InvalidOperationException("Could not select physical screen coordinates.");
        try
        {
            var target = new Point { X = screenX, Y = screenY };
            CheckTarget((uint)expectedPid, target);
            if (!SetCursorPos(screenX, screenY)) throw new InvalidOperationException("Could not position the guest pointer.");
            var input = new Input[1]; // INPUT_MOUSE
            input[0].Mouse.Flags = 0x0800; // MOUSEEVENTF_WHEEL
            int size = Marshal.SizeOf(typeof(Input)), sent = 0;
            foreach (int notches in bursts)
            {
                input[0].Mouse.Data = Math.Sign(notches) * notchesPerPulse * 120; // WHEEL_DELTA
                for (int n = 0; n < Math.Abs(notches); n++)
                {
                    Point cursor;
                    if (!GetCursorPos(out cursor) || cursor.X != screenX || cursor.Y != screenY)
                        throw new InvalidOperationException("Guest pointer moved away from the task list.");
                    CheckTarget((uint)expectedPid, cursor);
                    if (SendInput(1, input, size) != 1)
                        throw new InvalidOperationException("Guest wheel input was rejected: " + Marshal.GetLastWin32Error());
                    sent++;
                    Thread.Sleep(10);
                }
            }
            return sent;
        }
        finally { SetThreadDpiAwarenessContext(priorDpi); }
    }
}
