using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.ApplicationModel.Resources;
using Wintodo.ViewModels;

namespace Wintodo.Controls;

public sealed partial class ImportWizardView : UserControl
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(ImportWizardViewModel),
            typeof(ImportWizardView),
            new PropertyMetadata(null));

    private bool _isDetached;

    public ImportWizardView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    // The shell hosts this view with x:Load, so closing the wizard discards
    // the instance while the shared view model lives on. A discarded view
    // must stop listening: otherwise the next Reset() clears collections the
    // old repeaters still observe, and the command that reopens the wizard
    // fails inside the button's Click (found by the import gate).
    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        ViewModel.RemoveConfirmationRequested -= ConfirmRemovalAsync;
        Bindings.StopTracking();
        ListsRepeater.ItemsSource = null;
        SummaryRepeater.ItemsSource = null;
        _isDetached = true;
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        ViewModel.RemoveConfirmationRequested += ConfirmRemovalAsync;
        if (_isDetached)
        {
            _isDetached = false;
            Bindings.Update();
        }
    }

    private async Task<bool> ConfirmRemovalAsync(string message)
    {
        var resources = new ResourceLoader();
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = resources.GetString("Import_RemoveConfirmTitle"),
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap,
                Style = (Style)Application.Current.Resources["BodyTextBlockStyle"] },
            PrimaryButtonText = resources.GetString("Import_RemoveConfirmPrimary"),
            CloseButtonText = resources.GetString("Import_RemoveConfirmCancel"),
            DefaultButton = ContentDialogButton.Close,
        };
        AutomationProperties.SetAutomationId(dialog, "Todo.Import.RemoveConfirm");
        dialog.Opened += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot))
                SetPrimaryId(popup.Child);
        });
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private static void SetPrimaryId(DependencyObject root)
    {
        if (root is Button { Name: "PrimaryButton" } button)
            AutomationProperties.SetAutomationId(button, "Todo.Import.RemoveConfirm.Primary");
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            SetPrimaryId(VisualTreeHelper.GetChild(root, i));
    }

    public ImportWizardViewModel ViewModel
    {
        get => (ImportWizardViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static Visibility BoolToVisibility(bool value)
    {
        return value ? Visibility.Visible : Visibility.Collapsed;
    }

    public static Visibility EitherToVisibility(bool first, bool second)
    {
        return first || second ? Visibility.Visible : Visibility.Collapsed;
    }

    public static Visibility TextToVisibility(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible;
    }
}
