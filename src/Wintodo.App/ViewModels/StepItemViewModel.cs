using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wintodo.Core.Models;

namespace Wintodo.ViewModels;

public sealed partial class StepItemViewModel : ObservableObject
{
    private readonly Func<StepItemViewModel, string, Task> _rename;
    private readonly Func<StepItemViewModel, Task> _toggleCompletion;
    private readonly Func<StepItemViewModel, Task> _delete;
    private bool _isReady;

    internal StepItemViewModel(
        TodoTask task,
        Func<StepItemViewModel, string, Task> rename,
        Func<StepItemViewModel, Task> toggleCompletion,
        Func<StepItemViewModel, Task> delete)
    {
        Model = task;
        _rename = rename;
        _toggleCompletion = toggleCompletion;
        _delete = delete;
        Summary = task.Summary;
        IsCompleted = task.Status == Wintodo.Core.Models.TaskStatus.Completed;
        _isReady = true;
    }

    internal TodoTask Model { get; private set; }

    public string Uid => Model.Uid;

    public double SortOrder => Model.SortOrder;

    public string AutomationId => $"Todo.Detail.Step.{Uid}";

    public string CompleteAutomationId => $"Todo.Detail.StepComplete.{Uid}";

    public string DeleteAutomationId => $"Todo.Detail.StepDelete.{Uid}";

    [ObservableProperty]
    public partial string Summary { get; set; }

    [ObservableProperty]
    public partial bool IsCompleted { get; set; }

    partial void OnSummaryChanged(string value)
    {
        if (_isReady && !string.IsNullOrWhiteSpace(value))
        {
            _ = _rename(this, value);
        }
    }

    [RelayCommand]
    private Task ToggleCompletionAsync()
    {
        return _toggleCompletion(this);
    }

    [RelayCommand]
    private Task DeleteAsync()
    {
        return _delete(this);
    }

    internal void Update(TodoTask task)
    {
        _isReady = false;
        Model = task;
        Summary = task.Summary;
        IsCompleted = task.Status == Wintodo.Core.Models.TaskStatus.Completed;
        _isReady = true;
        OnPropertyChanged(nameof(Uid));
        OnPropertyChanged(nameof(SortOrder));
    }
}
