using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Globalization;
using Windows.ApplicationModel;
using Wintodo.ViewModels;
using Wintodo.Services;

namespace Wintodo.Controls;

public sealed partial class SettingsView : UserControl
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(ShellViewModel),
            typeof(SettingsView),
            new PropertyMetadata(null));

    public SettingsView()
    {
        PackageVersion version = Package.Current.Id.Version;
        VersionText = string.Format(
            DisplayCulture.Current,
            new ResourceLoader().GetString("Settings_VersionFormat"),
            version.Major,
            version.Minor,
            version.Build,
            version.Revision);
        WeekStartOptions = [
            new ResourceLoader().GetString("Settings_WeekStartSystem"),
            .. DisplayCulture.Current.DateTimeFormat.DayNames,
        ];
        InitializeComponent();
    }

    public string VersionText { get; }

    public string[] WeekStartOptions { get; }

    public ShellViewModel ViewModel
    {
        get => (ShellViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static Visibility BoolToVisibility(bool value)
    {
        return value ? Visibility.Visible : Visibility.Collapsed;
    }

    public static Visibility InvertBoolToVisibility(bool value)
    {
        return value ? Visibility.Collapsed : Visibility.Visible;
    }

    [RelayCommand]
    private async Task ConfigureCalDavAsync()
    {
        CalDavDialog.XamlRoot = XamlRoot;
        await CalDavDialog.ShowAsync();
    }

    private void OnCalDavDialogOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        // A dismissed dialog keeps whatever was half-typed, so reopening it
        // restores the server and username but never the password.
        ViewModel.CalDavPassword = string.Empty;
        CalDavDialogError.IsOpen = false;
        DispatcherQueue.TryEnqueue(() =>
        {
            foreach (Popup popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot))
            {
                SetDialogButtonId(popup.Child, "PrimaryButton", "Todo.Settings.Sync.Connect");
                SetDialogButtonId(popup.Child, "CloseButton", "Todo.Dialog.CalDav.Cancel");
            }
        });
        CalDavServerBox.Focus(FocusState.Programmatic);
    }

    private void OnCalDavDialogClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        ViewModel.CalDavPassword = string.Empty;
    }

    public static bool CanSubmitCalDav(string server, string username, string password, bool isBusy)
    {
        return !isBusy && !string.IsNullOrWhiteSpace(server)
            && !string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password);
    }

    public static bool Not(bool value) => !value;

    private static void SetDialogButtonId(DependencyObject root, string name, string id)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is FrameworkElement element
                && (element.Name == name || AutomationProperties.GetAutomationId(element) == name))
            {
                AutomationProperties.SetAutomationId(element, id);
                return;
            }
            SetDialogButtonId(child, name, id);
        }
    }

    private async void OnCalDavDialogPrimaryClick(
        ContentDialog sender,
        ContentDialogButtonClickEventArgs args)
    {
        if (ViewModel is not { } viewModel || !viewModel.ConnectCalDavCommand.CanExecute(null))
        {
            args.Cancel = true;
            return;
        }

        // The deferral keeps the dialog on screen for the round trip, so a
        // rejected credential is reported where it was typed instead of behind
        // a dialog that already dismissed itself.
        ContentDialogButtonClickDeferral deferral = args.GetDeferral();
        CalDavDialogError.IsOpen = false;
        try
        {
            await viewModel.ConnectCalDavCommand.ExecuteAsync(null);
            if (!viewModel.IsSyncConnected)
            {
                args.Cancel = true;
                CalDavDialogError.Message = viewModel.SyncErrorMessage;
                CalDavDialogError.IsOpen = true;
                viewModel.IsSyncErrorOpen = false;
            }
        }
        finally
        {
            deferral.Complete();
        }
    }
}
