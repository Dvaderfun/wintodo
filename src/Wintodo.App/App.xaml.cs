using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using System.Diagnostics;
using Windows.ApplicationModel.Activation;
using Windows.Storage;
using Wintodo.Core.Services;
using Wintodo.Data;
using Wintodo.Import.MicrosoftTodo;
using Wintodo.Services;
using Wintodo.Sync;
using WinRT;

namespace Wintodo;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private readonly TaskCompletionSource<WindowsIntegrationService> _integrationReady =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _notificationsRegistered;
    private readonly AppActivationArguments _activation;

    public static Window Window { get; private set; } = null!;

    public static Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; private set; } = null!;

    public static nint WindowHandle =>
        WinRT.Interop.WindowNative.GetWindowHandle(Window);

    public static Microsoft.UI.WindowId WindowId =>
        Microsoft.UI.Win32Interop.GetWindowIdFromWindow(WindowHandle);

    internal App(AppActivationArguments activation)
    {
        _activation = activation;
        InitializeComponent();
        UnhandledException += (_, args) =>
            Trace.WriteLine($"WINTODO_UNHANDLED_EXCEPTION={args.Exception}");
#if DEBUG
        // UI gates collect the app's trace lines (exceptions, binding
        // failures) from a file; Debug builds only, like --import-fixture.
        if (GetCommandLineOption("--trace-file") is { Length: > 0 } traceFile)
        {
            Trace.Listeners.Add(new TextWriterTraceListener(Path.GetFullPath(traceFile)));
            Trace.AutoFlush = true;
        }

        DebugSettings.IsBindingTracingEnabled = true;
        DebugSettings.BindingFailed += (_, args) =>
            Trace.WriteLine($"WINTODO_BINDING_FAILED={args.Message}");
#endif
    }

    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        SqliteTaskStore? launchingStore = null;
        try
        {
            long launchStartTimestamp = Stopwatch.GetTimestamp();
            TraceStartupStage("activation-start", launchStartTimestamp);
            DisplayCulture.Initialize();
            DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            AppActivationArguments activation = _activation;
            TraceStartupStage("activation-received", launchStartTimestamp);
            if (activation.Kind == ExtendedActivationKind.ShareTarget)
            {
                // Query the WinRT contract interface explicitly; Native AOT may expose
                // an IInspectable wrapper rather than the concrete runtime class.
                var shareArgs = activation.Data.As<IShareTargetActivatedEventArgs>();
                await HandleShareAsync(shareArgs);
                return;
            }
            RegisterAppNotifications();
            TraceStartupStage("notifications", launchStartTimestamp);
            IClock clock = ResolveClock();
            string? commandLineDatabasePath = GetCommandLineOption("--database-path");
            string databasePath = ResolveDatabasePath(
                activation,
                commandLineDatabasePath);
            var store = new SqliteTaskStore(databasePath, clock);
            launchingStore = store;
            var windowsIntegration = new WindowsIntegrationService(
                store,
                clock
#if DEBUG
                , IsM7GateDatabasePath(commandLineDatabasePath)
                    ? databasePath
                    : null
#endif
            );
            var startupTaskService = new StartupTaskService();

            if (activation.Kind == ExtendedActivationKind.StartupTask)
            {
                await InitializeBackgroundAsync(store, windowsIntegration);
                return;
            }

            NotificationActivationResult? notificationResult = null;
            if (activation.Kind == ExtendedActivationKind.AppNotification
                && activation.Data is AppNotificationActivatedEventArgs notificationArgs)
            {
                await store.InitializeAsync();
                await windowsIntegration.InitializeAsync();
                _integrationReady.TrySetResult(windowsIntegration);
                notificationResult = await windowsIntegration.HandleNotificationAsync(
                    notificationArgs.Arguments);
                if (!notificationResult.Value.ShowWindow)
                {
                    await windowsIntegration.DisposeAsync();
                    await store.DisposeAsync();
                    UnregisterAppNotifications();
                    Current.Exit();
                    return;
                }
            }

            var syncHttpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30),
            };
            var syncService = new CalDavSyncService(
                store,
                new PasswordVaultCredentialStore(),
                clock,
                syncHttpClient);
            var window = new MainWindow(
                store,
                clock,
                windowsIntegration,
                startupTaskService,
                syncService,
                syncHttpClient,
                CreateImportEnvironment(commandLineDatabasePath, clock));
            TraceStartupStage("window-created", launchStartTimestamp);
            Window = window;
            Task firstFrame = ObserveFirstFrameAsync(launchStartTimestamp);
            Window.Activate();
            await firstFrame;
            TraceStartupStage("first-frame", launchStartTimestamp);
            await store.InitializeAsync();
            TraceStartupStage("store", launchStartTimestamp);
            if (string.IsNullOrWhiteSpace(commandLineDatabasePath))
                await store.CleanupInterruptedImportsAsync(ApplicationData.Current.LocalFolder.Path);
            Task integrationTask = windowsIntegration.InitializeAsync();
            await window.InitializeAsync();
            TraceStartupStage("window", launchStartTimestamp);
            Trace.WriteLine(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"WINTODO_PERF_INTERACTIVE_MS={Stopwatch.GetElapsedTime(launchStartTimestamp).TotalMilliseconds:F3}"));
            await integrationTask;
            TraceStartupStage("integration", launchStartTimestamp);
            _integrationReady.TrySetResult(windowsIntegration);

            if (activation.Kind == ExtendedActivationKind.Launch
                     && !string.IsNullOrWhiteSpace(args.Arguments))
            {
                await window.HandleJumpListArgumentAsync(args.Arguments);
            }
            else if (notificationResult is { TaskUid: not null } result)
            {
                await window.OpenTaskAsync(result.TaskUid);
            }

            window.ApplyDeferredPresenterState();
        }
        catch (UnsupportedDatabaseSchemaException exception)
        {
            _integrationReady.TrySetException(exception);
            Trace.WriteLine(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"WINTODO_DATABASE_SCHEMA_UNSUPPORTED={exception.DatabaseVersion};SUPPORTED={exception.SupportedVersion}"));
            if (launchingStore is not null)
            {
                await launchingStore.DisposeAsync();
            }
            UnregisterAppNotifications();
            if (Window is MainWindow window)
            {
                await window.ShowUnsupportedDatabaseAsync();
                window.Close();
            }
            else
            {
                Current.Exit();
            }
        }
        catch (Exception exception)
        {
            _integrationReady.TrySetException(exception);
            Trace.WriteLine($"WINTODO_STARTUP_EXCEPTION={exception}");
            throw;
        }
    }

    internal static void UnregisterAppNotifications()
    {
        if (Current is not App app || !app._notificationsRegistered)
        {
            return;
        }

        AppNotificationManager.Default.NotificationInvoked -= app.OnNotificationInvoked;
        AppNotificationManager.Default.Unregister();
        app._notificationsRegistered = false;
    }

    private static void TraceStartupStage(string stage, long launchStartTimestamp)
    {
        Trace.WriteLine(string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"WINTODO_STARTUP_STAGE={stage};MS={Stopwatch.GetElapsedTime(launchStartTimestamp).TotalMilliseconds:F3}"));
    }

    private void RegisterAppNotifications()
    {
        if (_notificationsRegistered)
        {
            return;
        }

        try
        {
            AppNotificationManager.Default.NotificationInvoked += OnNotificationInvoked;
            AppNotificationManager.Default.Register();
            _notificationsRegistered = true;
        }
        catch (Exception exception)
        {
            AppNotificationManager.Default.NotificationInvoked -= OnNotificationInvoked;
            Trace.WriteLine($"WINTODO_NOTIFICATION_REGISTRATION_EXCEPTION={exception}");
        }
    }

    private async void OnNotificationInvoked(
        AppNotificationManager sender,
        AppNotificationActivatedEventArgs args)
    {
        try
        {
            WindowsIntegrationService integration = await _integrationReady.Task;
            NotificationActivationResult result =
                await integration.HandleNotificationAsync(args.Arguments);
            if (Window is not MainWindow window)
            {
                if (!result.ShowWindow)
                {
                    Current.Exit();
                }

                return;
            }

            DispatcherQueue.TryEnqueue(async () =>
            {
                await window.RefreshForExternalChangeAsync();
                if (result.ShowWindow)
                {
                    window.Activate();
                    if (result.TaskUid is not null)
                    {
                        await window.OpenTaskAsync(result.TaskUid);
                    }
                }
            });
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"WINTODO_NOTIFICATION_ACTIVATION_EXCEPTION={exception}");
        }
    }

    private async Task InitializeBackgroundAsync(
        SqliteTaskStore store,
        WindowsIntegrationService windowsIntegration)
    {
        await store.InitializeAsync();
        await windowsIntegration.InitializeAsync();
        _integrationReady.TrySetResult(windowsIntegration);
        await windowsIntegration.DisposeAsync();
        await store.DisposeAsync();
        UnregisterAppNotifications();
        Current.Exit();
    }

    private static Task ObserveFirstFrameAsync(long launchStartTimestamp)
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<object>? handler = null;
        handler = (_, _) =>
        {
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= handler;
            double elapsedMilliseconds =
                Stopwatch.GetElapsedTime(launchStartTimestamp).TotalMilliseconds;
            Trace.WriteLine(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"WINTODO_PERF_FIRST_FRAME_MS={elapsedMilliseconds:F3}"));
            completion.TrySetResult();
        };
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += handler;
        return completion.Task;
    }

    private static string ResolveDatabasePath(
        AppActivationArguments activation,
        string? commandLinePath)
    {
        if (!string.IsNullOrWhiteSpace(commandLinePath))
        {
            return Path.GetFullPath(commandLinePath);
        }

#if DEBUG
        if (activation.Kind == ExtendedActivationKind.AppNotification
            && activation.Data is AppNotificationActivatedEventArgs notificationArgs
            && notificationArgs.Arguments.TryGetValue(
                WindowsIntegrationService.DebugDatabasePathArgument,
                out string? notificationDatabasePath)
            && IsM7GateDatabasePath(notificationDatabasePath))
        {
            return Path.GetFullPath(notificationDatabasePath);
        }
#endif

        string? overridePath =
            Environment.GetEnvironmentVariable("WINTODO_DATABASE_PATH");
        return string.IsNullOrWhiteSpace(overridePath)
            ? Path.Combine(ApplicationData.Current.LocalFolder.Path, "wintodo.db")
            : Path.GetFullPath(overridePath);
    }

#if DEBUG
    private static bool IsM7GateDatabasePath(string? path)
    {
        return !string.IsNullOrWhiteSpace(path)
            && string.Equals(
                Path.GetFileName(path),
                "m7-gate.db",
                StringComparison.OrdinalIgnoreCase)
            && File.Exists(Path.GetFullPath(path));
    }
#endif

    private static ImportEnvironment CreateImportEnvironment(string? commandLineDatabasePath, IClock clock)
    {
        // Scratch databases used by UI gates skip onboarding unless they opt in.
        bool onboardingAllowed = string.IsNullOrWhiteSpace(commandLineDatabasePath)
            || HasCommandLineFlag("--show-onboarding");
        string attachmentRoot = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
        IMicrosoftTodoConnector connector = new MicrosoftGraphConnector(
            GraphTodoClient.CreateHttpClient(),
            new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader().GetString("Import_SignInCompletePage"),
            new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader().GetString("Import_SignInFailedPage"),
            new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader().GetString("Import_SignInPageTitle"),
            clock);
#if DEBUG
        string? fixture = GetCommandLineOption("--import-fixture");
        if (!string.IsNullOrWhiteSpace(fixture))
        {
            connector = new FixtureMicrosoftTodoConnector(
                Path.GetFullPath(fixture),
                clock,
                expireFirstSignIn: HasCommandLineFlag("--import-fixture-expire-once"),
                throttleOnce: HasCommandLineFlag("--import-fixture-throttle-once"));
        }
#endif
        return new ImportEnvironment(connector, onboardingAllowed, attachmentRoot);
    }

    private static bool HasCommandLineFlag(string flag)
    {
        return Environment.GetCommandLineArgs()
            .Skip(1)
            .Contains(flag, StringComparer.Ordinal);
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
