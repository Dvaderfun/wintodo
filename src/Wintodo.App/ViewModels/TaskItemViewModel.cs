using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wintodo.Core.Models;

namespace Wintodo.ViewModels;

public sealed partial class TaskItemViewModel : TaskListEntryViewModel
{
    private readonly Func<TaskItemViewModel, Task> _toggleCompletion;
    private readonly Func<TaskItemViewModel, Task> _toggleImportance;
    private readonly Func<TaskItemViewModel, Task> _delete;
    private readonly Func<TaskItemViewModel, long, Task> _move;

    internal TaskItemViewModel(
        TodoTask task,
        Func<TaskItemViewModel, Task> toggleCompletion,
        Func<TaskItemViewModel, Task> toggleImportance,
        Func<TaskItemViewModel, Task> delete,
        Func<TaskItemViewModel, long, Task> move)
        : base($"Todo.Row.{task.Uid}")
    {
        Model = task;
        _toggleCompletion = toggleCompletion;
        _toggleImportance = toggleImportance;
        _delete = delete;
        _move = move;
        Summary = task.Summary;
        IsCompleted = task.Status == Wintodo.Core.Models.TaskStatus.Completed;
        IsImportant = task.Priority is >= 1 and <= 4;
        Metadata = string.Empty;
        AccessibleName = task.Summary;
        CompletionDateText = string.Empty;
        HighlightText = string.Empty;
    }

    internal TodoTask Model { get; set; }

    public string Uid => Model.Uid;

    public long ListId => Model.ListId;

    public double SortOrder => Model.SortOrder;

    public string CompleteAutomationId => $"Todo.Row.CompleteToggle.{Uid}";

    public string ImportanceAutomationId => $"Todo.Row.ImportanceToggle.{Uid}";

    public string ContextAutomationId => $"Todo.Row.Context.{Uid}";

    [ObservableProperty]
    public partial string Summary { get; set; }

    [ObservableProperty]
    public partial bool IsCompleted { get; set; }

    [ObservableProperty]
    public partial bool IsImportant { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial string Metadata { get; set; }

    [ObservableProperty]
    public partial string AccessibleName { get; set; }

    [ObservableProperty]
    public partial string CompletionDateText { get; set; }

    [ObservableProperty]
    public partial string HighlightText { get; set; }

    [ObservableProperty]
    public partial bool IsOverdue { get; set; }

    [ObservableProperty]
    public partial bool IsDueToday { get; set; }

    [RelayCommand]
    private Task ToggleCompletionAsync()
    {
        return _toggleCompletion(this);
    }

    [RelayCommand]
    private Task ToggleImportanceAsync()
    {
        return _toggleImportance(this);
    }

    [RelayCommand]
    private Task DeleteAsync()
    {
        return _delete(this);
    }

    [RelayCommand]
    private Task MoveAsync(long listId)
    {
        return _move(this, listId);
    }
}
