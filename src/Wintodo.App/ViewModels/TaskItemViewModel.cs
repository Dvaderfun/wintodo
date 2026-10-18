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
    private readonly Func<TaskItemViewModel, Task> _rename;

    internal TaskItemViewModel(
        TodoTask task,
        Func<TaskItemViewModel, Task> toggleCompletion,
        Func<TaskItemViewModel, Task> toggleImportance,
        Func<TaskItemViewModel, Task> delete,
        Func<TaskItemViewModel, long, Task> move,
        Func<TaskItemViewModel, Task> rename)
        : base($"Todo.Row.{task.Uid}")
    {
        Model = task;
        _toggleCompletion = toggleCompletion;
        _toggleImportance = toggleImportance;
        _delete = delete;
        _move = move;
        _rename = rename;
        Summary = task.Summary;
        IsCompleted = task.Status == Wintodo.Core.Models.TaskStatus.Completed;
        IsImportant = task.Priority is >= 1 and <= 4;
        Metadata = string.Empty;
        AccessibleName = task.Summary;
        CompletionDateText = string.Empty;
        HighlightText = string.Empty;
        EditTitle = task.Summary;
    }

    internal TodoTask Model { get; set; }

    internal void RefreshCompletionToggle() => OnPropertyChanged(nameof(IsCompleted));

    public string Uid => Model.Uid;

    public long ListId => Model.ListId;

    public double SortOrder => Model.SortOrder;

    public string CompleteAutomationId => $"Todo.Row.CompleteToggle.{Uid}";

    public string ImportanceAutomationId => $"Todo.Row.ImportanceToggle.{Uid}";

    public string EditAutomationId => $"Todo.Row.TitleEdit.{Uid}";

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

    [ObservableProperty]
    public partial bool IsCompactDensity { get; set; }

    [ObservableProperty]
    public partial bool IsEditingTitle { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CommitTitleEditCommand))]
    public partial string EditTitle { get; set; }

    [RelayCommand]
    private void BeginTitleEdit()
    {
        EditTitle = Summary;
        IsEditingTitle = true;
    }

    private bool CanCommitTitleEdit()
    {
        return !string.IsNullOrWhiteSpace(EditTitle);
    }

    [RelayCommand(CanExecute = nameof(CanCommitTitleEdit))]
    private async Task CommitTitleEditAsync()
    {
        EditTitle = EditTitle.Trim();
        await _rename(this);
        IsEditingTitle = false;
    }

    [RelayCommand]
    private void CancelTitleEdit()
    {
        EditTitle = Summary;
        IsEditingTitle = false;
    }

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
