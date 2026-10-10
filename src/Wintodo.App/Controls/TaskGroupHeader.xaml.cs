using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Wintodo.ViewModels;

namespace Wintodo.Controls;

public sealed partial class TaskGroupHeader : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(TaskListGroupViewModel), typeof(TaskGroupHeader), new PropertyMetadata(null));

    public TaskGroupHeader() => InitializeComponent();

    public TaskListGroupViewModel ViewModel
    {
        get => (TaskListGroupViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }
}
