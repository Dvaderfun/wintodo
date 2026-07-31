using Microsoft.UI.Xaml;
using System.Diagnostics;
using Windows.Storage;
using Wintodo.Core.Services;
using Wintodo.Data;
using Wintodo.Services;

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

    public static Microsoft.UI.WindowId WindowId =>
        Microsoft.UI.Win32Interop.GetWindowIdFromWindow(WindowHandle);

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        try
        {
            long launchStartTimestamp = Stopwatch.GetTimestamp();
            DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            IClock clock = ResolveClock();
            string databasePath = ResolveDatabasePath();
            var store = new SqliteTaskStore(databasePath, clock);
            var window = new MainWindow(store, clock);
            Window = window;
            var firstFrameCompletion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler<object>? firstFrameHandler = null;
            firstFrameHandler = (_, _) =>
            {
                Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= firstFrameHandler;
                double elapsedMilliseconds =
                    Stopwatch.GetElapsedTime(launchStartTimestamp).TotalMilliseconds;
                Trace.WriteLine(string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"WINTODO_PERF_FIRST_FRAME_MS={elapsedMilliseconds:F3}"));
                firstFrameCompletion.TrySetResult();
            };
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += firstFrameHandler;
            Window.Activate();
            await firstFrameCompletion.Task;
            await store.InitializeAsync();
            await window.InitializeAsync();
            window.ApplyDeferredPresenterState();
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"WINTODO_STARTUP_EXCEPTION={exception}");
            throw;
        }
    }

    private static string ResolveDatabasePath()
    {
        string? commandLinePath = GetCommandLineOption("--database-path");
        if (!string.IsNullOrWhiteSpace(commandLinePath))
        {
            return Path.GetFullPath(commandLinePath);
        }

        string? overridePath =
            Environment.GetEnvironmentVariable("WINTODO_DATABASE_PATH");
        return string.IsNullOrWhiteSpace(overridePath)
            ? Path.Combine(ApplicationData.Current.LocalFolder.Path, "wintodo.db")
            : Path.GetFullPath(overridePath);
    }

    private static IClock ResolveClock()
    {
        string? clockFile = GetCommandLineOption("--clock-file");
        return string.IsNullOrWhiteSpace(clockFile)
            ? new SystemClock()
            : new FileBackedClock(clockFile);
    }

    internal static string? GetCommandLineOption(string option)
    {
        string[] commandLineArguments = Environment.GetCommandLineArgs();
        for (int index = 1; index < commandLineArguments.Length; index++)
        {
            if (!string.Equals(
                    commandLineArguments[index],
                    option,
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (index + 1 >= commandLineArguments.Length)
            {
                throw new ArgumentException(
                    $"{option} requires a value.",
                    nameof(option));
            }

            return commandLineArguments[index + 1];
        }

        return null;
    }
}
