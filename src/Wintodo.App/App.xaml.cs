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
            Trace.WriteLine($"WINTODO_UNHANDLED_EXCEPTION={DiagnosticException.Describe(args.Exception)}");
#if DEBUG
        // UI gates collect safe app diagnostics from a file; Debug builds
        // only, like --import-fixture.
        if (GetCommandLineOption("--trace-file") is { Length: > 0 } traceFile)
        {
            Trace.Listeners.Add(new TextWriterTraceListener(Path.GetFullPath(traceFile)));
            Trace.AutoFlush = true;
        }

        // The native binding tracer writes raw error details independently of
        // BindingFailed handlers, which cannot sanitize its debug output.
        DebugSettings.IsBindingTracingEnabled = false;
#endif
    }

    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        SqliteTaskStore? launchingStore = null;
        MainWindow? launchingWindow = null;
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
#if WINTODO_PRIVATE_PREVIEW
            // The side-by-side appearance preview must never open a Store or scratch database
            // through an inherited environment variable, launch argument, or notification.
            string? commandLineDatabasePath = null;
            string databasePath = Path.Combine(ApplicationData.Current.LocalFolder.Path, "wintodo.db");
#else
            string? commandLineDatabasePath = GetCommandLineOption("--database-path");
            string databasePath = ResolveDatabasePath(
                activation,
                commandLineDatabasePath);
#endif
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
                notificationResult = await windowsIntegration.HandleNotificationAsync(
                    notificationArgs.Arguments);
                if (!notificationResult.Value.ShowWindow)
                {
                    _integrationReady.TrySetResult(windowsIntegration);
                    await windowsIntegration.DisposeAsync();
                    await store.DisposeAsync();
                    UnregisterAppNotifications();
                    Current.Exit();
                    return;
                }
            }

            Task openStore = notificationResult.HasValue ? Task.CompletedTask : store.InitializeAsync();
            var stateService = new ShellStateService(store);
            ImportEnvironment importEnvironment = CreateImportEnvironment(commandLineDatabasePath, clock);
            Task<(WindowPlacementState? Placement, Task Recovery)> windowState =
                PrepareWindowStoreAsync(openStore, store, stateService, importEnvironment.AttachmentRoot, launchStartTimestamp);
            var syncHttpClient = CalDavClient.CreateHttpClient();
            var syncService = new CalDavSyncService(
                store,
                new PasswordVaultCredentialStore(),
                clock,
                syncHttpClient);
            var window = new MainWindow(
                store,
                clock,
                stateService,
                windowsIntegration,
                startupTaskService,
                syncService,
                syncHttpClient,
                importEnvironment);
            TraceStartupStage("window-created", launchStartTimestamp);
            launchingWindow = window;
            Window = window;
            var (placement, recovery) = await windowState;
            ObserveStartupFrame(window, launchStartTimestamp, interactive: false);
            window.ShowInitialShell(placement);
            await recovery;
            Task shellTask = window.InitializeAsync();
            Task integrationTask = windowsIntegration.InitializeAsync();
            await shellTask;
            TraceStartupStage("window", launchStartTimestamp);
            if (activation.Kind == ExtendedActivationKind.Launch
                     && !string.IsNullOrWhiteSpace(args.Arguments))
            {
                await window.HandleJumpListArgumentAsync(args.Arguments);
            }
            else if (notificationResult is { TaskUid: not null } result)
            {
                await window.OpenTaskAsync(result.TaskUid);
            }
            ObserveStartupFrame(window, launchStartTimestamp, interactive: true);

            await integrationTask;
            TraceStartupStage("integration", launchStartTimestamp);
            _integrationReady.TrySetResult(windowsIntegration);
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
            if (launchingWindow is { } window)
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
            Trace.WriteLine($"WINTODO_STARTUP_EXCEPTION={DiagnosticException.Describe(exception)}");
            throw;
        }
        finally
        {
            launchingWindow?.CompleteStartup();
        }
    }

    private static async Task<(WindowPlacementState? Placement, Task Recovery)> PrepareWindowStoreAsync(
        Task openStore, SqliteTaskStore store, ShellStateService state, string attachmentRoot, long timestamp)
    {
        // Keep this continuation off the UI thread: XAML and the initial SQLite reads can overlap.
        await openStore.ConfigureAwait(false);
        TraceStartupStage("store", timestamp);
        Task<WindowPlacementState?> placement = state.LoadWindowPlacementAsync();
        // A large recovery may outlast window creation. Paint the shell at the saved bounds
        // while it runs, but don't admit imports or other writes until it has finished.
        Task recovery = store.CleanupInterruptedImportsAsync(attachmentRoot);
        try
        {
            return (await placement.ConfigureAwait(false), recovery);
        }
        catch
        {
            await recovery.ConfigureAwait(false);
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
            Trace.WriteLine($"WINTODO_NOTIFICATION_REGISTRATION_EXCEPTION={DiagnosticException.Describe(exception)}");
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
            Trace.WriteLine($"WINTODO_NOTIFICATION_ACTIVATION_EXCEPTION={DiagnosticException.Describe(exception)}");
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

    private static void ObserveStartupFrame(Window window, long launchStartTimestamp, bool interactive)
    {
        void Detach()
        {
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnRendering;
            window.Closed -= OnClosed;
        }
        void OnClosed(object sender, WindowEventArgs args) => Detach();
        void OnRendering(object? sender, object args)
        {
            Detach();
            double elapsedMilliseconds =
                Stopwatch.GetElapsedTime(launchStartTimestamp).TotalMilliseconds;
            string marker = interactive ? "WINTODO_PERF_INTERACTIVE_MS" : "WINTODO_PERF_FIRST_FRAME_MS";
            Trace.WriteLine(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{marker}={elapsedMilliseconds:F3}"));
            TraceStartupStage(interactive ? "interactive-frame" : "first-frame", launchStartTimestamp);
        }
        window.Closed += OnClosed;
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnRendering;
    }

    private static string ResolveDatabasePath(
        AppActivationArguments activation,
        string? commandLinePath)
    {
        return LaunchOptions.ResolveDatabasePath(
            commandLinePath,
            () => ResolveLowerPriorityDatabasePath(activation),
            () => Path.Combine(ApplicationData.Current.LocalFolder.Path, SqliteTaskStore.DefaultDatabaseFileName));
    }

    private static string? ResolveLowerPriorityDatabasePath(AppActivationArguments activation)
    {
#if DEBUG
        if (activation.Kind == ExtendedActivationKind.AppNotification
            && activation.Data is AppNotificationActivatedEventArgs notificationArgs
            && notificationArgs.Arguments.TryGetValue(
                WindowsIntegrationService.DebugDatabasePathArgument,
                out string? notificationDatabasePath)
            && IsM7GateDatabasePath(notificationDatabasePath))
        {
            return notificationDatabasePath;
        }
#endif

        return Environment.GetEnvironmentVariable("WINTODO_DATABASE_PATH");
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
        bool onboardingAllowed = LaunchOptions.AllowsOnboarding(
            commandLineDatabasePath,
            () => HasCommandLineFlag("--show-onboarding"));
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
        return LaunchOptions.HasFlag(Environment.GetCommandLineArgs(), flag);
    }

    private static IClock ResolveClock()
    {
        return LaunchOptions.CreateClock(GetCommandLineOption("--clock-file"));
    }

    internal static string? GetCommandLineOption(string option)
    {
        return LaunchOptions.GetValue(Environment.GetCommandLineArgs(), option);
    }
}
