using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.Windows.ApplicationModel.Resources;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinRT;
using Wintodo.Core.Services;
using Wintodo.Data;
using Wintodo.Pages;
using Wintodo.Services;
using Wintodo.Sync;

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
    private readonly ShellPage _shellPage;
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
        Title = new ResourceLoader().GetString("MainWindowTitle");
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        // AOT can return the base projection even for the overlapped presenter.
        var presenter = AppWindow.Presenter;
        _presenter = presenter.Kind == AppWindowPresenterKind.Overlapped ? presenter.As<OverlappedPresenter>() : null;
        _shellPage = new ShellPage(store, clock, stateService, windowsIntegration, startupTaskService, syncService, importEnvironment);
        _shellPage.SearchFocusRequested += TitleSearch.FocusSearch;
        _shellPage.SearchClearRequested += TitleSearch.TryClear;
        _shellPage.ThemeChanged += OnShellThemeChanged;

        WindowRoot.SizeChanged += OnWindowRootSizeChanged;
        AppWindow.Changed += OnAppWindowChanged;
        AppWindow.Closing += OnWindowClosing;
        Activated += OnActivated;
        Closed += OnClosed;
        TitleSearch.SearchRequested += OnSearchRequested;
    }

    internal void ShowInitialShell(WindowPlacementState? placement)
    {
        // The title bar remains usable (including minimize/close); navigation and commands
        // must not run against an unfinished recovery or the wrong initial selection.
        _shellPage.IsEnabled = false;
        TitleSearch.IsEnabled = false;
        RootFrame.Content = _shellPage;
        ApplyInitialBounds(placement);
        CaptureRestoredBounds();
        _hasPresentedShell = true;
        // Maximize can show the HWND. Attach content and set restore bounds before either call.
        if (placement?.IsMaximized == true) _presenter?.Maximize();
        _lastNonMinimizedWasMaximized = _presenter?.State == OverlappedPresenterState.Maximized;
        Activate();
    }

    internal async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _externalChangeVersion = await _store.GetExternalChangeVersionAsync(cancellationToken);
        _isWindowMinimized = _presenter?.State == OverlappedPresenterState.Minimized;
        await _shellPage.SetWindowMinimizedAsync(_isWindowMinimized);
        await _shellPage.InitializeAsync(cancellationToken);
        _isInitialized = true;
        _shellPage.IsEnabled = true;
        TitleSearch.IsEnabled = true;
    }

    internal void CompleteStartup() => _startupFinished.TrySetResult();

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
        if (!minimized) _lastNonMinimizedWasMaximized = state == OverlappedPresenterState.Maximized;
        if (_isWindowMinimized == minimized) return;
        _isWindowMinimized = minimized;
        await _shellPage.SetWindowMinimizedAsync(minimized);
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

    internal Task HandleJumpListArgumentAsync(
        string argument,
        CancellationToken cancellationToken = default)
    {
        return _shellPage.HandleJumpListArgumentAsync(argument, cancellationToken);
    }

    internal Task OpenTaskAsync(
        string uid,
        CancellationToken cancellationToken = default)
    {
        return _shellPage.OpenTaskAsync(uid, cancellationToken);
    }

    internal Task RefreshForExternalChangeAsync(
        CancellationToken cancellationToken = default)
    {
        return _isInitialized ? _shellPage.RefreshForExternalChangeAsync(cancellationToken) : Task.CompletedTask;
    }

    private async void OnSearchRequested(string text)
    {
        if (_isInitialized)
        {
            await _shellPage.SearchAsync(text);
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
                await _shellPage.RefreshForActivationAsync(reloadExternalChanges: version != _externalChangeVersion);
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
        WindowRoot.SizeChanged -= OnWindowRootSizeChanged;
        AppWindow.Changed -= OnAppWindowChanged;
        AppWindow.Closing -= OnWindowClosing;
        Activated -= OnActivated;
        TitleSearch.SearchRequested -= OnSearchRequested;
        _shellPage.SearchFocusRequested -= TitleSearch.FocusSearch;
        _shellPage.SearchClearRequested -= TitleSearch.TryClear;
        _shellPage.ThemeChanged -= OnShellThemeChanged;
        _shellPage.StopMidnightRefresh();
        await _windowsIntegration.DisposeAsync();
        _syncService.Dispose();
        _syncHttpClient.Dispose();
        await _store.DisposeAsync();
        App.UnregisterAppNotifications();
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);
}
