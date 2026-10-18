using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Wintodo;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        // Capture the consumable platform payload before WinUI initialization.
        AppActivationArguments activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        System.Diagnostics.Trace.WriteLine($"WINTODO_EARLY_ACTIVATION={activation.Kind}");
        Application.Start(parameters =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App(activation);
        });
    }
}
