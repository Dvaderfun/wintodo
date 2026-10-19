using System.ComponentModel;
using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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
        ViewModel.PropertyChanged -= OnStepChanged;
        Bindings.StopTracking();
        ListsRepeater.ItemsSource = null;
        SummaryRepeater.ItemsSource = null;
        _isDetached = true;
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        ViewModel.RemoveConfirmationRequested += ConfirmRemovalAsync;
        ViewModel.PropertyChanged += OnStepChanged;
        QueueStepFocus();
        if (_isDetached)
        {
            _isDetached = false;
            Bindings.Update();
        }
    }

    private void OnStepChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ImportWizardViewModel.Step) or nameof(ImportWizardViewModel.IsBusy))
            QueueStepFocus();
    }

    private void QueueStepFocus() => DispatcherQueue.TryEnqueue(() =>
    {
        if (ViewModel.IsBusy) return;
        Control? target = ViewModel.Step switch
        {
            ImportWizardStep.Connect => SignInButton,
            ImportWizardStep.Choose => FindFirstCheckBox(ListsRepeater),
            ImportWizardStep.Review => ImportButton,
            ImportWizardStep.Done => FinishButton,
            _ => null,
        };
        target?.Focus(FocusState.Programmatic);
    });

    private static CheckBox? FindFirstCheckBox(DependencyObject root)
    {
        if (root is CheckBox box) return box;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindFirstCheckBox(VisualTreeHelper.GetChild(root, i)) is CheckBox found) return found;
        return null;
    }

    private void OnEscape(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (HasOpenDialog(XamlRoot)) return;
        ICommand command = ViewModel.IsReviewStep ? ViewModel.BackCommand : ViewModel.CancelCommand;
        if (command.CanExecute(null)) command.Execute(null);
        args.Handled = true;
    }

    private void OnEnter(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (HasOpenDialog(XamlRoot)) return;
        ICommand? command = ViewModel.Step switch
        {
            ImportWizardStep.Connect => ViewModel.SignInCommand,
            ImportWizardStep.Choose => ViewModel.ContinueCommand,
            ImportWizardStep.Review => ViewModel.ImportCommand,
            ImportWizardStep.Done => ViewModel.FinishCommand,
            _ => null,
        };
        if (command?.CanExecute(null) == true) command.Execute(null);
        args.Handled = true;
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

    public static bool HasOpenDialog(XamlRoot root)
        => VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Any(popup => ContainsDialog(popup.Child));

    private static bool ContainsDialog(DependencyObject root)
    {
        if (root is ContentDialog) return true;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (ContainsDialog(VisualTreeHelper.GetChild(root, i))) return true;
        return false;
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
