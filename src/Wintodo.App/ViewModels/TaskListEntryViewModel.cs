using CommunityToolkit.Mvvm.ComponentModel;

namespace Wintodo.ViewModels;

public abstract class TaskListEntryViewModel : ObservableObject
{
    protected TaskListEntryViewModel(string automationId)
    {
        AutomationId = automationId;
    }

    public string AutomationId { get; }
}
