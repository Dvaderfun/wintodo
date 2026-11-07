using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Wintodo;

public sealed partial class MainWindow
{
    internal async Task ShowUnsupportedDatabaseAsync()
    {
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
