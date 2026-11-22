using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wintodo.Core.Services;

namespace Wintodo.ViewModels;

public sealed partial class TaskListViewModel
{
    private IReadOnlyList<TaskItemViewModel> _selectedTasks = Array.Empty<TaskItemViewModel>();
    public IReadOnlyList<TaskItemViewModel> SelectedTasks => _selectedTasks;
    internal DayOfWeek FirstDayOfWeek => _preferences.FirstDayOfWeek;

    [ObservableProperty]
    public partial int SelectionCount { get; private set; }

    internal event Action<IReadOnlyList<TaskItemViewModel>>? DeleteTasksRequested;
    internal event Action? DueDatePickerRequested;
    internal event Func<IReadOnlyList<string>, Task>? TaskDatesChanged;

    internal void SetSelection(IEnumerable<TaskItemViewModel> items)
    {
        TaskItemViewModel[] selection = items.Distinct().ToArray();
        if (_selectedTasks.SequenceEqual(selection)) return;
        foreach (TaskItemViewModel item in _selectedTasks) item.IsSelected = false;
        _selectedTasks = Array.AsReadOnly(selection);
        foreach (TaskItemViewModel item in selection) item.IsSelected = true;
        SelectionCount = selection.Length;
        SelectedTask = selection.Length == 1 ? selection[0] : null;
        OnPropertyChanged(nameof(SelectedTasks));
    }

    internal TaskItemViewModel[] ActionTargets(TaskItemViewModel row) =>
        SelectedTasks.Contains(row) ? SelectedTasks.ToArray() : [row];

    internal bool SelectionIsInMyDay => SelectedTasks.All(item => Wintodo.Core.Presentation.TaskDateLabels.IsInMyDay(item.Model, _clock));

    internal string SelectionLabel(string key) => string.Format(
        Services.DisplayCulture.Current,
        _resources.GetString(SelectionCount == 1 && key is not ("Selection_Due" or "Selection_Today"
            or "Selection_Tomorrow" or "Selection_Pick") ? key + "_Single" : key), SelectionCount);

    private async Task ApplySelectionAsync(TaskBatchAction action, DateOnly? date = null, long? destinationListId = null)
    {
        string[] uids = SelectedTasks.Select(item => item.Uid).ToArray();
        if (uids.Length == 0) return;
        await Task.Yield();
        var states = await _taskStore.CaptureTaskUndoAsync(uids, UndoFields(action));
        await _taskStore.ApplyTaskBatchAsync(uids, action, date: date,
            destinationListId: destinationListId, moveStarredToTop: _preferences.MoveStarredToTop);
        RecordTaskUndo(states, action == TaskBatchAction.Move ? "Undo_TaskMoved" : "Undo_TasksChanged");
        if (action is TaskBatchAction.AddToMyDay or TaskBatchAction.RemoveFromMyDay or TaskBatchAction.SetDueDate
            && TaskDatesChanged is not null)
            await TaskDatesChanged(uids);
        if (action == TaskBatchAction.Complete) _completedExpanded = true;
        await ReloadAsync();
        NavigationDataChanged?.Invoke();
    }

    internal async Task MoveTasksToListAsync(IReadOnlyList<string> uids, long listId)
    {
        var states = await _taskStore.CaptureTaskUndoAsync(uids, TaskUndoFields.List);
        await _taskStore.ApplyTaskBatchAsync(uids, TaskBatchAction.Move, destinationListId: listId);
        RecordTaskUndo(states, "Undo_TaskMoved");
        await ReloadAsync();
        NavigationDataChanged?.Invoke();
    }

    internal async Task AddDroppedTasksAsync(IReadOnlyList<string> uids, Core.Queries.ListQueryKind target)
    {
        TaskBatchAction action = target switch
        {
            Core.Queries.ListQueryKind.MyDay => TaskBatchAction.AddToMyDay,
            Core.Queries.ListQueryKind.Important => TaskBatchAction.MarkImportant,
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        };
        await _taskStore.ApplyTaskBatchAsync(uids, action, moveStarredToTop: _preferences.MoveStarredToTop);
        if (action == TaskBatchAction.AddToMyDay && TaskDatesChanged is not null)
            await TaskDatesChanged(uids);
        await ReloadAsync();
        NavigationDataChanged?.Invoke();
    }

    [RelayCommand]
    private async Task MoveSelectionAsync(long listId) =>
        await ApplySelectionAsync(TaskBatchAction.Move, destinationListId: listId);

    [RelayCommand]
    private async Task SetSelectionDueAsync(string choice)
    {
        if (choice == "Pick") { DueDatePickerRequested?.Invoke(); return; }
        await ApplySelectionAsync(TaskBatchAction.SetDueDate,
            _clock.Today.AddDays(choice == "Tomorrow" ? 1 : 0));
    }

    internal async Task SetTasksDueDateAsync(IReadOnlyList<TaskItemViewModel> items, DateOnly date)
    {
        string[] uids = items.Select(item => item.Uid).ToArray();
        var states = await _taskStore.CaptureTaskUndoAsync(uids, TaskUndoFields.Due);
        await _taskStore.ApplyTaskBatchAsync(uids, TaskBatchAction.SetDueDate, date: date);
        RecordTaskUndo(states, "Undo_TasksChanged");
        if (TaskDatesChanged is not null) await TaskDatesChanged(uids);
        await ReloadAsync();
        NavigationDataChanged?.Invoke();
    }

    internal async Task ConfirmDeleteTasksAsync(IReadOnlyList<TaskItemViewModel> items,
        CancellationToken cancellationToken = default)
    {
        string[] uids = items.Select(item => item.Uid).ToArray();
        var states = await _taskStore.CaptureTaskUndoAsync(uids, TaskUndoFields.Deleted, cancellationToken);
        await _taskStore.ApplyTaskBatchAsync(uids, TaskBatchAction.Delete, cancellationToken);
        foreach (TaskItemViewModel item in items)
        {
            _activeTasks.Remove(item);
            _completedTasks.Remove(item);
        }
        RebuildVisibleItems();
        RecordTaskUndo(states, items.Count == 1 ? "Undo_TaskDeleted" : "Undo_TasksDeleted");
        NavigationDataChanged?.Invoke();
    }
}
