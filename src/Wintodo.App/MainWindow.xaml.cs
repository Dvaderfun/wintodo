using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.Windows.ApplicationModel.Resources;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using Wintodo.Core.Services;
using Wintodo.Data;
using Wintodo.Pages;
using Wintodo.Services;

namespace Wintodo;

public sealed partial class MainWindow : Window
{
    private const double DefaultWidthDip = 1180;
    private const double DefaultHeightDip = 820;
    private const double MinimumWidthDip = 640;
    private const double MinimumHeightDip = 480;

    private readonly SqliteTaskStore _store;
    private readonly IClock _clock;
    private readonly ShellStateService _stateService;
    private ShellPage? _shellPage;
    private PointInt32 _restoredPosition;
    private SizeInt32 _restoredSize;
    private bool _maximizeAfterActivation;

    public MainWindow(SqliteTaskStore store, IClock clock)
    {
        _store = store;
        _clock = clock;
        _stateService = new ShellStateService(store);

        InitializeComponent();
        Title = new ResourceLoader().GetString("MainWindowTitle");
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        ConfigurePresenter();
        ResizeInDips(DefaultWidthDip, DefaultHeightDip);
        CaptureRestoredBounds();

        WindowRoot.SizeChanged += OnWindowRootSizeChanged;
        AppWindow.Changed += OnAppWindowChanged;
        Closed += OnClosed;
    }

    internal async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _shellPage = new ShellPage(_store, _clock, _stateService);
        RootFrame.Content = _shellPage;
        WindowPlacementState? placement =
            await _stateService.LoadWindowPlacementAsync(cancellationToken);
        if (placement is not null)
        {
            ApplyPlacement(placement.Value);
        }

        await _shellPage.InitializeAsync(cancellationToken);
    }

    internal void ApplyDeferredPresenterState()
    {
        if (_maximizeAfterActivation && AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Maximize();
        }
    }

    private void ConfigurePresenter()
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter)
        {
            return;
        }

        presenter.PreferredMinimumWidth = DipToPixels(MinimumWidthDip);
        presenter.PreferredMinimumHeight = DipToPixels(MinimumHeightDip);
    }

    private void ApplyPlacement(WindowPlacementState placement)
    {
        int width = Math.Max(DipToPixels(placement.WidthDip), DipToPixels(MinimumWidthDip));
        int height = Math.Max(DipToPixels(placement.HeightDip), DipToPixels(MinimumHeightDip));
        AppWindow.MoveAndResize(new RectInt32(placement.X, placement.Y, width, height));
        CaptureRestoredBounds();
        _maximizeAfterActivation = placement.IsMaximized;
    }

    private void ResizeInDips(double widthDip, double heightDip)
    {
        AppWindow.Resize(new SizeInt32(DipToPixels(widthDip), DipToPixels(heightDip)));
    }

    private int DipToPixels(double dip)
    {
        double scale = GetDpiScale();
        return checked((int)Math.Round(dip * scale, MidpointRounding.AwayFromZero));
    }

    private double PixelsToDips(int pixels)
    {
        return pixels / GetDpiScale();
    }

    private double GetDpiScale()
    {
        nint hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        uint dpi = GetDpiForWindow(hwnd);
        return dpi == 0 ? 1 : dpi / 96.0;
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if ((args.DidPositionChange || args.DidSizeChange || args.DidPresenterChange)
            && sender.Presenter is OverlappedPresenter
            {
                State: OverlappedPresenterState.Restored,
            })
        {
            CaptureRestoredBounds();
        }
    }

    private void OnWindowRootSizeChanged(object sender, SizeChangedEventArgs args)
    {
        TitleSearch.UpdateWindowWidth(args.NewSize.Width);
    }

    private void CaptureRestoredBounds()
    {
        _restoredPosition = AppWindow.Position;
        _restoredSize = AppWindow.Size;
    }

    private async void OnClosed(object sender, WindowEventArgs args)
    {
        WindowRoot.SizeChanged -= OnWindowRootSizeChanged;
        AppWindow.Changed -= OnAppWindowChanged;
        bool isMaximized = AppWindow.Presenter is OverlappedPresenter
        {
            State: OverlappedPresenterState.Maximized,
        };
        var placement = new WindowPlacementState(
            _restoredPosition.X,
            _restoredPosition.Y,
            PixelsToDips(_restoredSize.Width),
            PixelsToDips(_restoredSize.Height),
            isMaximized);

        try
        {
            await _stateService.SaveWindowPlacementAsync(placement);
        }
        finally
        {
            await _store.DisposeAsync();
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);
}
