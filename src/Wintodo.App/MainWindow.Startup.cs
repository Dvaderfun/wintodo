using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Wintodo;

public sealed partial class MainWindow
{
#if DEBUG || WINTODO_PRIVATE_PREVIEW
    private FrameworkElement? _startupLogo;

    private void PrepareStartupLogo()
    {
        // Keep the shell in layout with a live XamlRoot, but clip it until navigation is ready.
        // The title bar still owns drag regions and Windows still owns its caption buttons.
        RootFrame.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, 0, 0) };
        TitleSearch.Visibility = Visibility.Collapsed;
        var template = (DataTemplate)WindowRoot.Resources["StartupLogoTemplate"];
        _startupLogo = (FrameworkElement)template.LoadContent();
        WindowRoot.Children.Add(_startupLogo);
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
