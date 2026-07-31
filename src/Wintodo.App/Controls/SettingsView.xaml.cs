using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
        InitializeComponent();
    }

    public ShellViewModel ViewModel
    {
        get => (ShellViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static Visibility BoolToVisibility(bool value)
    {
        return value ? Visibility.Visible : Visibility.Collapsed;
    }
}
