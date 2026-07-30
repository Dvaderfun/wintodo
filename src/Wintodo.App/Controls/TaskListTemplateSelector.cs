using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Wintodo.ViewModels;

namespace Wintodo.Controls;

public sealed class TaskListTemplateSelector : DataTemplateSelector
{
    public DataTemplate? TaskTemplate { get; set; }

    public DataTemplate? CompletedHeaderTemplate { get; set; }

    protected override DataTemplate SelectTemplateCore(
        object item,
        DependencyObject container)
    {
        return item switch
        {
            TaskItemViewModel => TaskTemplate
                ?? throw new InvalidOperationException("TaskTemplate is required."),
            CompletedHeaderViewModel => CompletedHeaderTemplate
                ?? throw new InvalidOperationException("CompletedHeaderTemplate is required."),
            _ => throw new ArgumentOutOfRangeException(nameof(item)),
        };
    }
}
