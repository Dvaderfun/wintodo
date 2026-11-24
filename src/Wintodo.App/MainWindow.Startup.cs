using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.ApplicationModel.Resources;
#if DEBUG || WINTODO_PRIVATE_PREVIEW
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Wintodo.Core.Services;
#endif

namespace Wintodo;

public sealed partial class MainWindow
{
#if DEBUG || WINTODO_PRIVATE_PREVIEW
    private Grid? _startupLogo;
    internal Task StartupArtwork { get; }

    private async Task PrepareStartupLogoAsync()
    {
        // Keep the shell in layout with a live XamlRoot, but clip it until navigation is ready.
        // The title bar still owns drag regions and Windows still owns its caption buttons.
        RootFrame.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, 0, 0) };
        TitleSearch.Visibility = Visibility.Collapsed;
        var template = (DataTemplate)WindowRoot.Resources["StartupLogoTemplate"];
        var surface = (Grid)template.LoadContent();
        var image = (Image)surface.Children[0];
        image.ImageOpened += OnStartupLogoOpened;
        _startupLogo = surface;
        Grid.SetRowSpan(surface, 2);
        WindowRoot.Children.Add(_startupLogo);
        try
        {
            // Resolve the scale-qualified asset through its logical package URI. Decode this
            // small local image before showing the HWND rather than deferring decode to layout.
            StorageFile file;
            try { file = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/Square150x150Logo.png")); }
            catch (FileNotFoundException)
            {
                // A loose package can expose just the physical scale-200 candidate.
                file = await Windows.ApplicationModel.Package.Current.InstalledLocation.GetFileAsync(@"Assets\Square150x150Logo.scale-200.png");
            }
            using var stream = await file.OpenReadAsync();
            var bitmap = new BitmapImage();
            bitmap.SetSource(stream);
            image.Source = bitmap;
            _startupLogoDecoded = bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0;
        }
        catch (Exception ex)
        {
            // A damaged package image must never block an otherwise usable window.
            System.Diagnostics.Trace.WriteLine($"WINTODO_STARTUP_ARTWORK_EXCEPTION={DiagnosticException.Describe(ex)}");
        }
    }

    private Task YieldToWindowLayoutAsync()
    {
        var opportunity = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => opportunity.TrySetResult()))
            opportunity.TrySetResult();
        return opportunity.Task;
    }

    private void OnStartupLogoOpened(object sender, RoutedEventArgs args)
    {
        _startupLogoDecoded = true;
        ((Image)sender).ImageOpened -= OnStartupLogoOpened;
    }

    private void RemoveStartupLogo()
    {
        if (_startupLogo is not null) WindowRoot.Children.Remove(_startupLogo);
        _startupLogo = null;
        RootFrame.Clip = null;
    }
#endif

    internal async Task ShowUnsupportedDatabaseAsync()
    {
#if DEBUG || WINTODO_PRIVATE_PREVIEW
        // Validation failed before ShowInitialShell. Only present the recovery dialog.
        RemoveStartupLogo();
#endif
        // Store validation now precedes activation. A ContentDialog needs a loaded XamlRoot.
        if (!WindowRoot.IsLoaded)
        {
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnLoaded(object sender, RoutedEventArgs args) => loaded.TrySetResult();
            WindowRoot.Loaded += OnLoaded;
            Activate();
            try { await loaded.Task; }
            finally { WindowRoot.Loaded -= OnLoaded; }
        }
        else Activate();
        var resources = new ResourceLoader();
        var dialog = new ContentDialog
        {
            XamlRoot = WindowRoot.XamlRoot,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
            Title = resources.GetString("Database_NewerSchemaTitle"),
            Content = resources.GetString("Database_NewerSchemaMessage"),
            CloseButtonText = resources.GetString("Database_Close"),
            DefaultButton = ContentDialogButton.Close,
        };
        AutomationProperties.SetAutomationId(dialog, "Todo.Startup.NewerSchemaDialog");
        dialog.Opened += (_, _) =>
        {
            if (!SetSchemaDialogCloseId(dialog.XamlRoot))
            {
                DispatcherQueue.TryEnqueue(() => SetSchemaDialogCloseId(dialog.XamlRoot));
            }
        };
        await dialog.ShowAsync();
    }

    private static bool SetSchemaDialogCloseId(XamlRoot root)
    {
        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(root))
        {
            if (FindSchemaDialogCloseButton(popup.Child) is { } button)
            {
                AutomationProperties.SetAutomationId(button, "Todo.Startup.NewerSchemaClose");
                return true;
            }
        }
        return false;
    }

    private static Button? FindSchemaDialogCloseButton(DependencyObject? root)
    {
        if (root is null)
        {
            return null;
        }
        if (root is Button { Name: "CloseButton" } button)
        {
            return button;
        }
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            if (FindSchemaDialogCloseButton(VisualTreeHelper.GetChild(root, index)) is { } child)
            {
                return child;
            }
        }
        return null;
    }
}
