using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Globalization;
using Windows.ApplicationModel;
using Windows.System;
using Wintodo.ViewModels;

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
            CultureInfo.CurrentCulture,
            new ResourceLoader().GetString("Settings_VersionFormat"),
            version.Major,
            version.Minor,
            version.Build,
            version.Revision);
        InitializeComponent();
    }

    public string VersionText { get; }

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

    // Enter anywhere in the credential form submits it, the way every other
    // sign-in surface in Windows behaves.
    private void OnCredentialKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter)
        {
            return;
        }

        if (ViewModel?.ConnectCalDavCommand.CanExecute(null) == true)
        {
            ViewModel.ConnectCalDavCommand.Execute(null);
        }

        args.Handled = true;
    }
}
