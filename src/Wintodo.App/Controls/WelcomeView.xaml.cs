using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Wintodo.ViewModels;

namespace Wintodo.Controls;

public sealed partial class WelcomeView : UserControl
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(ShellViewModel),
            typeof(WelcomeView),
            new PropertyMetadata(null));

    public WelcomeView()
    {
        InitializeComponent();
    }

    public ShellViewModel ViewModel
    {
        get => (ShellViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }
}
