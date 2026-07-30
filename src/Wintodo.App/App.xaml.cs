using Microsoft.UI.Xaml;
using Windows.Storage;
using Wintodo.Core.Services;
using Wintodo.Data;

namespace Wintodo;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    public static Window Window { get; private set; } = null!;

    public static Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; private set; } = null!;

    public static nint WindowHandle =>
        WinRT.Interop.WindowNative.GetWindowHandle(Window);

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        var clock = new SystemClock();
        string databasePath = Path.Combine(
            ApplicationData.Current.LocalFolder.Path,
            "wintodo.db");
        var store = new SqliteTaskStore(databasePath, clock);
        await store.InitializeAsync();

        var window = new MainWindow(store, clock);
        await window.InitializeAsync();
        Window = window;
        Window.Activate();
        window.ApplyDeferredPresenterState();
    }
}
