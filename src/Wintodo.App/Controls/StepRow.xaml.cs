using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;
using Wintodo.ViewModels;
using Wintodo.Services;

namespace Wintodo.Controls;

public sealed partial class StepRow : UserControl
{
    private static readonly ResourceLoader StringResources = new();

    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(StepItemViewModel),
            typeof(StepRow),
            new PropertyMetadata(null));

    public StepRow()
    {
        InitializeComponent();
    }

    public StepItemViewModel ViewModel
    {
        get => (StepItemViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static bool Not(bool value)
    {
        return !value;
    }

    public static Visibility BoolToVisibility(bool value)
    {
        return value ? Visibility.Visible : Visibility.Collapsed;
    }

    public static string CompletionName(bool isCompleted)
    {
        return isCompleted
            ? StringResources.GetString("Detail_MarkStepNotCompleted")
            : StringResources.GetString("Detail_MarkStepCompleted");
    }

    public static string DeleteName(string summary)
    {
        return string.Format(
            DisplayCulture.Current,
            StringResources.GetString("Detail_DeleteStep"),
            summary);
    }
}
