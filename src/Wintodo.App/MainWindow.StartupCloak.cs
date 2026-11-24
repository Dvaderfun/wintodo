#if DEBUG || WINTODO_PRIVATE_PREVIEW
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;
using Wintodo.Core.Services;

namespace Wintodo;

public sealed partial class MainWindow
{
    private bool _startupLogoDecoded;
    private Action? _releaseStartupCloak;
    private readonly TaskCompletionSource _firstCommittedSurface = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // A visible HWND can precede the first XAML pixels. Keep the native erase off the
    // desktop until a meaningful frame has been committed. Startup never waits on this.
    private void HideNativeEraseUntilFirstFrame()
    {
        try { ArmFirstFrameGuard(); }
        catch (Exception ex)
        {
            _releaseStartupCloak?.Invoke();
            System.Diagnostics.Trace.WriteLine($"WINTODO_STARTUP_CLOAK_EXCEPTION={DiagnosticException.Describe(ex)}");
        }
    }

    private void ArmFirstFrameGuard()
    {
        const int DWMWA_CLOAK = 13;
        nint hwnd = WindowNative.GetWindowHandle(this);
        bool cloaked = false;

        void Release(bool final)
        {
            if (_releaseStartupCloak is null) return;
            if (cloaked)
            {
                int off = 0;
                int hr = DwmSetWindowAttribute(hwnd, DWMWA_CLOAK, ref off, sizeof(int));
                if (hr < 0)
                {
                    System.Diagnostics.Trace.WriteLine($"WINTODO_STARTUP_UNCLOAK_HR={hr:X8}");
                    if (!final) return; // Normal startup completion will retry.
                }
            }
            _releaseStartupCloak = null;
            CompositionTarget.Rendered -= OnRendered;
            _firstCommittedSurface.TrySetResult();
        }

        async void OnRendered(object? sender, RenderedEventArgs args)
        {
            if ((!_startupLogoDecoded && !_isInitialized) || !WindowRoot.IsLoaded || !IsWindowVisible(hwnd)) return;
            CompositionTarget.Rendered -= OnRendered;
            try { await Compositor.RequestCommitAsync(); }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"WINTODO_FIRST_COMMIT_EXCEPTION={DiagnosticException.Describe(ex)}"); }
            finally { Release(final: false); }
        }

        // Arm every way out before cloaking. Ready, minimize and close also call Release.
        _releaseStartupCloak = () => Release(final: true);
        CompositionTarget.Rendered += OnRendered;
        int on = 1;
        cloaked = DwmSetWindowAttribute(hwnd, DWMWA_CLOAK, ref on, sizeof(int)) >= 0;
        if (!cloaked) Release(final: true);
    }

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hwnd);
}
#endif
