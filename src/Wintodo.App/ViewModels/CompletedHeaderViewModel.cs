using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Wintodo.ViewModels;

public sealed partial class CompletedHeaderViewModel : TaskListEntryViewModel
{
    private readonly Func<Task> _toggle;

    internal CompletedHeaderViewModel(Func<Task> toggle, string accessibleName)
        : base("Todo.List.CompletedSection")
    {
        _toggle = toggle;
        AccessibleName = accessibleName;
    }

    public string AccessibleName { get; }

    [ObservableProperty]
    public partial int Count { get; set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    [RelayCommand]
    private Task ToggleAsync()
    {
        return _toggle();
    }
}
