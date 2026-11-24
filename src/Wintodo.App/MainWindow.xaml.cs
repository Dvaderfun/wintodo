using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.Windows.ApplicationModel.Resources;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT;
using Wintodo.Core.Services;
using Wintodo.Data;
using Wintodo.Pages;
using Wintodo.Services;
using Wintodo.Sync;
using Wintodo.ViewModels;

namespace Wintodo;

public sealed partial class MainWindow : Window
{
    private const double DefaultWidthDip = 1180;
    private const double DefaultHeightDip = 820;
    private const double MinimumWidthDip = 640;
    private const double MinimumHeightDip = 480;

    private readonly SqliteTaskStore _store;
    private readonly ShellStateService _stateService;
    private readonly WindowsIntegrationService _windowsIntegration;
    private readonly CalDavSyncService _syncService;
    private readonly HttpClient _syncHttpClient;
    private readonly ShellViewModel _shellModel;
    private readonly Lazy<ShellPage> _shell;
    private ShellPage Shell => _shell.Value;
    private readonly OverlappedPresenter? _presenter;
    private readonly TaskCompletionSource _startupFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private WindowPlacementState _restoredPlacement;
    private bool _lastNonMinimizedWasMaximized;
    private bool _isWindowMinimized;
    private bool _hasPresentedShell;
    private long _externalChangeVersion;
    private bool _isInitialized;
    private bool _isActivationRefreshRunning;

    internal MainWindow(
        SqliteTaskStore store,
        IClock clock,
        ShellStateService stateService,
        WindowsIntegrationService windowsIntegration,
        IStartupTaskService startupTaskService,
        CalDavSyncService syncService,
        HttpClient syncHttpClient,
        ImportEnvironment importEnvironment)
    {
        _store = store;
        _stateService = stateService;
        _windowsIntegration = windowsIntegration;
        _syncService = syncService;
        _syncHttpClient = syncHttpClient;

        InitializeComponent();
#if !DEBUG && !WINTODO_PRIVATE_PREVIEW
        SystemBackdrop = new MicaBackdrop(); // Base Mica, as in the shipping window.
#endif
        Title = new ResourceLoader().GetString("MainWindowTitle");
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        // AOT can return the base projection even for the overlapped presenter.
        var presenter = AppWindow.Presenter;
        _presenter = presenter.Kind == AppWindowPresenterKind.Overlapped ? presenter.As<OverlappedPresenter>() : null;
#if DEBUG || WINTODO_PRIVATE_PREVIEW
        StartupArtwork = PrepareStartupLogoAsync();
#endif
        _shellModel = new ShellViewModel(store, clock, stateService, startupTaskService, syncService, windowsIntegration, importEnvironment);
        _shell = new Lazy<ShellPage>(() =>
        {
            var page = new ShellPage(store, clock, windowsIntegration, _shellModel);
            page.SearchFocusRequested += TitleSearch.FocusSearch;
            page.SearchClearRequested += TitleSearch.TryClear;
            page.ThemeChanged += OnShellThemeChanged;
            return page;
        });
#if !DEBUG && !WINTODO_PRIVATE_PREVIEW
        _ = Shell; // Production still constructs the page while SQLite opens.
#endif

        WindowRoot.SizeChanged += OnWindowRootSizeChanged;
        AppWindow.Changed += OnAppWindowChanged;
        AppWindow.Closing += OnWindowClosing;
        Activated += OnActivated;
        Closed += OnClosed;
        TitleSearch.SearchRequested += OnSearchRequested;
    }

    internal void ShowInitialShell(WindowStartupState state)
    {
        // The title bar remains usable (including minimize/close); navigation and commands
        // must not run against an unfinished recovery or the wrong initial selection.
        TitleSearch.IsEnabled = false;
        int themeIndex = ShellViewModel.ParseStartupTheme(state.ThemeSetting);
        _shellModel.AppearanceThemeIndex = themeIndex;
        OnShellThemeChanged(ShellPage.ThemeForIndex(themeIndex));
#if DEBUG || WINTODO_PRIVATE_PREVIEW
        SystemBackdrop = WindowRoot.RequestedTheme == ElementTheme.Default
            ? new MicaBackdrop() : new StartupMicaBackdrop(WindowRoot.RequestedTheme);
        if (_startupLogo is not null) _startupLogo.RequestedTheme = WindowRoot.RequestedTheme;
#endif
#if !DEBUG && !WINTODO_PRIVATE_PREVIEW
        AttachInitialShell();
#endif
        ApplyInitialBounds(state.Placement);
        CaptureRestoredBounds();
        _hasPresentedShell = true;
#if DEBUG || WINTODO_PRIVATE_PREVIEW
        HideNativeEraseUntilFirstFrame();
#endif
        // Maximize can show the HWND. Attach content and set restore bounds before either call.
        if (state.Placement?.IsMaximized == true) _presenter?.Maximize();
        _lastNonMinimizedWasMaximized = _presenter?.State == OverlappedPresenterState.Maximized;
        Activate();
    }

    internal async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Task<string>? navigation = null;
#if DEBUG || WINTODO_PRIVATE_PREVIEW
        // Local preferences, navigation counts and onboarding can load without constructing the page.
        navigation = _shellModel.InitializeAsync(cancellationToken);
#endif
        _externalChangeVersion = await _store.GetExternalChangeVersionAsync(cancellationToken);
#if DEBUG || WINTODO_PRIVATE_PREVIEW
        // Yield expensive template construction to pending first-window layout. Never require
        // a rendering event for real work: a minimized/occluded window must initialize too.
        await Task.WhenAny(_firstCommittedSurface.Task, navigation);
        await YieldToWindowLayoutAsync();
        AttachInitialShell();
#endif
        _isWindowMinimized = _presenter?.State == OverlappedPresenterState.Minimized;
        await Shell.SetWindowMinimizedAsync(_isWindowMinimized);
        await Shell.InitializeAsync(navigation, cancellationToken);
    }

    private void AttachInitialShell()
    {
        Shell.IsEnabled = false;
        RootFrame.Content = Shell;
    }

    internal void CompleteStartup()
    {
        _startupFinished.TrySetResult();
#if DEBUG || WINTODO_PRIVATE_PREVIEW
        // Also release if startup exited before it could present the local screen.
        _releaseStartupCloak?.Invoke();
#endif
    }

    private void ApplyInitialBounds(WindowPlacementState? placement)
    {
        var current = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        var display = placement is { } saved
            ? DisplayArea.GetFromPoint(new PointInt32(saved.X, saved.Y), DisplayAreaFallback.Nearest)
            : current;
        var work = display.WorkArea;
        // WorkArea has a display-relative origin; AppWindow uses screen coordinates.
        int left = display.OuterBounds.X + work.X;
        int top = display.OuterBounds.Y + work.Y;
        if (current.DisplayId.Value != display.DisplayId.Value)
        {
            // Obtain the destination monitor's window DPI while the HWND is still hidden.
            AppWindow.Move(new PointInt32(left, top));
        }
        double scale = GetDpiScale();
        int Pixels(double dip, int limit) => (int)Math.Min(limit, Math.Round(dip * scale, MidpointRounding.AwayFromZero));
        if (_presenter is not null)
        {
            _presenter.PreferredMinimumWidth = Pixels(MinimumWidthDip, work.Width);
            _presenter.PreferredMinimumHeight = Pixels(MinimumHeightDip, work.Height);
        }
        int width = Pixels(Math.Max(MinimumWidthDip, placement?.WidthDip ?? DefaultWidthDip), work.Width);
        int height = Pixels(Math.Max(MinimumHeightDip, placement?.HeightDip ?? DefaultHeightDip), work.Height);
        PointInt32 position = AppWindow.Position;
        int x = Math.Clamp(placement?.X ?? position.X, left, left + work.Width - width);
        int y = Math.Clamp(placement?.Y ?? position.Y, top, top + work.Height - height);
        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }

    private double GetDpiScale()
    {
        nint hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        uint dpi = GetDpiForWindow(hwnd);
        return dpi == 0 ? 1 : dpi / 96.0;
    }

    private async void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!_hasPresentedShell || _closeApproved || (!args.DidPositionChange && !args.DidSizeChange && !args.DidPresenterChange)) return;
        var state = _presenter?.State;
        if (state == OverlappedPresenterState.Restored)
        {
            CaptureRestoredBounds();
        }
        bool minimized = state == OverlappedPresenterState.Minimized;
#if DEBUG || WINTODO_PRIVATE_PREVIEW
        if (minimized) _releaseStartupCloak?.Invoke();
#endif
        if (!minimized) _lastNonMinimizedWasMaximized = state == OverlappedPresenterState.Maximized;
        if (_isWindowMinimized == minimized) return;
        _isWindowMinimized = minimized;
        if (_shell.IsValueCreated) await Shell.SetWindowMinimizedAsync(minimized);
    }

    private void OnWindowRootSizeChanged(object sender, SizeChangedEventArgs args)
    {
        TitleSearch.UpdateWindowWidth(args.NewSize.Width);
    }

    // WindowRoot is the window content root, so this also drives the title bar,
    // its search box, and the colors the framework picks for the caption buttons.
    private void OnShellThemeChanged(ElementTheme theme)
    {
        WindowRoot.RequestedTheme = theme;
    }

    internal async Task CompleteInitialNavigationAsync(
        string? jumpListArgument, string? notificationTaskUid,
        CancellationToken cancellationToken = default)
    {
        bool focusComposer = false;
        if (!string.IsNullOrWhiteSpace(jumpListArgument))
            focusComposer = await Shell.SelectJumpListTargetAsync(jumpListArgument, cancellationToken);
        else if (notificationTaskUid is not null)
            await Shell.OpenTaskAsync(notificationTaskUid, cancellationToken);

#if DEBUG || WINTODO_PRIVATE_PREVIEW
        RemoveStartupLogo();
        TitleSearch.Visibility = Visibility.Visible;
#endif
        _isInitialized = true;
        Shell.IsEnabled = true;
        TitleSearch.IsEnabled = true;
        if (focusComposer) Shell.FocusComposer();
#if DEBUG || WINTODO_PRIVATE_PREVIEW
        // If the logo never committed, give the ready shell one layout opportunity.
        // A missing frame notification or slow OS integration must not keep it hidden.
        if (_releaseStartupCloak is not null)
        {
            await Task.WhenAny(_firstCommittedSurface.Task, YieldToWindowLayoutAsync());
            _releaseStartupCloak?.Invoke();
        }
#endif
    }

    internal Task OpenTaskAsync(
        string uid,
        CancellationToken cancellationToken = default)
    {
        return Shell.OpenTaskAsync(uid, cancellationToken);
    }

    internal Task RefreshForExternalChangeAsync(
        CancellationToken cancellationToken = default)
    {
        return _isInitialized ? Shell.RefreshForExternalChangeAsync(cancellationToken) : Task.CompletedTask;
    }

    private async void OnSearchRequested(string text)
    {
        if (_isInitialized)
        {
            await Shell.SearchAsync(text);
        }
    }

    private async void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState != WindowActivationState.Deactivated
            && _isInitialized && !_closePending && !_isActivationRefreshRunning)
        {
            _isActivationRefreshRunning = true;
            try
            {
                long version = await _store.GetExternalChangeVersionAsync();
                System.Diagnostics.Trace.WriteLine($"WINTODO_EXTERNAL_VERSION={_externalChangeVersion}->{version}");
                await Shell.RefreshForActivationAsync(reloadExternalChanges: version != _externalChangeVersion);
                _externalChangeVersion = version;
            }
            finally { _isActivationRefreshRunning = false; }
        }
    }

    private void CaptureRestoredBounds()
    {
        var position = AppWindow.Position;
        var size = AppWindow.Size;
        double scale = GetDpiScale();
        _restoredPlacement = new(position.X, position.Y, size.Width / scale, size.Height / scale, false);
    }

    private WindowPlacementState GetLivePlacement()
    {
        var state = _presenter?.State;
        if (state == OverlappedPresenterState.Restored) CaptureRestoredBounds();
        // Minimized windows resume in their last non-minimized state. Never save iconic bounds.
        return _restoredPlacement with { IsMaximized = state == OverlappedPresenterState.Maximized
            || (state == OverlappedPresenterState.Minimized && _lastNonMinimizedWasMaximized) };
    }

    private async void OnClosed(object sender, WindowEventArgs args)
    {
#if DEBUG || WINTODO_PRIVATE_PREVIEW
        _releaseStartupCloak?.Invoke();
#endif
        WindowRoot.SizeChanged -= OnWindowRootSizeChanged;
        AppWindow.Changed -= OnAppWindowChanged;
        AppWindow.Closing -= OnWindowClosing;
        Activated -= OnActivated;
        TitleSearch.SearchRequested -= OnSearchRequested;
        if (_shell.IsValueCreated)
        {
            Shell.SearchFocusRequested -= TitleSearch.FocusSearch;
            Shell.SearchClearRequested -= TitleSearch.TryClear;
            Shell.ThemeChanged -= OnShellThemeChanged;
            Shell.StopMidnightRefresh();
        }
        await _windowsIntegration.DisposeAsync();
        _syncService.Dispose();
        _syncHttpClient.Dispose();
        await _store.DisposeAsync();
        App.UnregisterAppNotifications();
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);
}
