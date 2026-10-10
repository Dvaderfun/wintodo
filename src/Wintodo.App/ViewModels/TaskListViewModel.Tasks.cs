using CommunityToolkit.Mvvm.Input;
using Wintodo.Core.Models;
using Wintodo.Core.Presentation;
using Wintodo.Core.Queries;
using Wintodo.Core.Services;
using Wintodo.Services;

namespace Wintodo.ViewModels;

public sealed partial class TaskListViewModel
{
    internal async Task ReorderActiveTaskAsync(string taskUid, string targetUid, bool insertAfter)
    {
        if (!CanReorderTasks)
        {
            return;
        }

        int source = _activeTasks.FindIndex(item => item.Uid == taskUid);
        int target = _activeTasks.FindIndex(item => item.Uid == targetUid);
        if (source < 0 || target < 0)
        {
            return;
        }

        int destination = TaskReordering.GetDestinationIndex(source, target, insertAfter);
        if (source == destination)
        {
            return;
        }

        TaskItemViewModel item = _activeTasks[source];
        _activeTasks.RemoveAt(source);
        _activeTasks.Insert(destination, item);
        RebuildVisibleItems();
        await PersistVisibleOrderAsync();
    }

    internal async Task PersistVisibleOrderAsync(CancellationToken cancellationToken = default)
    {
        if (!CanReorderTasks)
        {
            return;
        }

        _activeTasks.Clear();
        foreach (IList<object> group in TaskGroups.OfType<IList<object>>())
        {
            foreach (TaskItemViewModel task in group.OfType<TaskItemViewModel>())
            {
                if (!task.IsCompleted)
                {
                    _activeTasks.Add(task);
                }
            }
        }

        for (int index = 0; index < _activeTasks.Count; index++)
        {
            TaskItemViewModel item = _activeTasks[index];
            double sortOrder = (index + 1) * 1024d;
            item.Model.SortOrder = sortOrder;
            await _taskStore.ReorderTaskAsync(item.Uid, sortOrder, cancellationToken);
        }
    }

    internal void ApplyDetailTaskUpdate(TodoTask task)
    {
        if (!_itemCache.TryGetValue(task.Uid, out TaskItemViewModel? item))
        {
            return;
        }

        TodoTask previous = item.Model;
        bool wasCompleted = item.IsCompleted;
        bool navigationCountsChanged =
            previous.Status != task.Status
            || previous.Priority != task.Priority
            || previous.Due != task.Due
            || previous.MyDayDate != task.MyDayDate;
        item.Model = task;
        UpdateItemDisplay(item);
        if (wasCompleted == item.IsCompleted)
        {
            if (previous.SortOrder != task.SortOrder && _currentQuery?.SortMode == SortMode.Manual)
            {
                _activeTasks.Remove(item);
                _completedTasks.Remove(item);
                InsertManualItem(item.IsCompleted ? _completedTasks : _activeTasks, item);
            }
            RebuildVisibleItems();
            if (navigationCountsChanged)
            {
                NavigationDataChanged?.Invoke();
            }

            return;
        }

        if (item.IsCompleted)
        {
            _activeTasks.Remove(item);
            InsertBySortOrder(_completedTasks, item);
            if (ReferenceEquals(SelectedTask, item)) _completedExpanded = true;
        }
        else
        {
            _completedTasks.Remove(item);
            InsertBySortOrder(_activeTasks, item);
        }

        RebuildVisibleItems();
    }

    internal void ApplyDetailTaskDeleted(string uid)
    {
        if (!_itemCache.TryGetValue(uid, out TaskItemViewModel? item))
        {
            return;
        }

        _activeTasks.Remove(item);
        _completedTasks.Remove(item);
        RebuildVisibleItems();
        BeginUndo([item]);
        NavigationDataChanged?.Invoke();
    }

    [RelayCommand]
    private async Task DeleteSelectedTaskAsync()
    {
        await Task.Yield();
        if (SelectionCount > 0) DeleteTasksRequested?.Invoke(SelectedTasks.ToArray());
    }

    [RelayCommand]
    private async Task ToggleSelectedCompletionAsync()
    {
        if (SelectionCount == 1 && SelectedTask is not null)
        {
            await ToggleTaskCompletionAsync(SelectedTask);
        }
        else await ApplySelectionAsync(SelectedTasks.All(item => item.IsCompleted)
            ? TaskBatchAction.Uncomplete : TaskBatchAction.Complete);
    }

    [RelayCommand]
    private async Task ToggleSelectedImportanceAsync()
    {
        if (SelectionCount == 1 && SelectedTask is not null)
        {
            await ToggleTaskImportanceAsync(SelectedTask);
        }
        else await ApplySelectionAsync(SelectedTasks.All(item => item.IsImportant)
            ? TaskBatchAction.RemoveImportance : TaskBatchAction.MarkImportant);
    }

    [RelayCommand]
    private async Task ToggleSelectedMyDayAsync()
    {
        await ApplySelectionAsync(SelectionIsInMyDay
            ? TaskBatchAction.RemoveFromMyDay : TaskBatchAction.AddToMyDay);
    }

    [RelayCommand]
    private void BeginEditSelectedTask()
    {
        SelectedTask?.BeginTitleEditCommand.Execute(null);
    }

    [RelayCommand]
    private async Task MoveSelectedUpAsync()
    {
        await MoveSelectedByAsync(-1);
    }

    [RelayCommand]
    private async Task MoveSelectedDownAsync()
    {
        await MoveSelectedByAsync(1);
    }

    private bool CanUndoDelete()
    {
        return IsUndoVisible && _lastDeletedTaskUids is not null;
    }

    [RelayCommand(CanExecute = nameof(CanUndoDelete))]
    private async Task UndoDeleteAsync()
    {
        await Task.Yield();
        IReadOnlyList<string>? uids = _lastDeletedTaskUids;
        if (uids is null)
        {
            return;
        }

        _undoCancellation?.Cancel();
        await _taskStore.ApplyTaskBatchAsync(uids, TaskBatchAction.Restore);
        CloseUndo();
        await ReloadAsync();
        NavigationDataChanged?.Invoke();
    }

    private async Task ToggleTaskCompletionAsync(TaskItemViewModel item)
    {
        await Task.Yield();
        VisibleItemsUpdating?.Invoke(true);
        try
        {
            if (item.IsCompleted)
            {
                await _taskStore.UncompleteTaskAsync(item.Uid);
                item.Model.Status = Wintodo.Core.Models.TaskStatus.NeedsAction;
                item.Model.PercentComplete = 0;
                item.Model.CompletedAt = null;
                item.IsCompleted = false;
                item.CompletionDateText = string.Empty;
                CompletionReordering?.Invoke();
                _completedTasks.Remove(item);
                InsertBySortOrder(_activeTasks, item);
                UpdateItemDisplay(item);
                RebuildVisibleItems(viewportAlreadyCaptured: true);
                CompletionAnnouncement = string.Format(
                    DisplayCulture.Current,
                    _resources.GetString("Task_UncompletionNotification"),
                    item.Summary);
                TaskCompletionChanged?.Invoke(item);
                return;
            }

            await _taskStore.CompleteTaskAsync(item.Uid);
            TodoTask updated = await _taskStore.GetTaskAsync(item.Uid)
                ?? throw new InvalidOperationException($"Task '{item.Uid}' disappeared after completion.");
            item.Model = updated;
            item.IsCompleted =
                updated.Status == Wintodo.Core.Models.TaskStatus.Completed;
            UpdateItemDisplay(item);
            CompletionAnnouncement = string.Format(
                DisplayCulture.Current,
                item.IsCompleted
                    ? _resources.GetString("Task_CompletionNotification")
                    : _resources.GetString("Task_UncompletionNotification"),
                item.Summary);
            TaskCompletionChanged?.Invoke(item);
            if (!item.IsCompleted)
            {
                VisibleItemsUpdated?.Invoke();
                return;
            }

            if (AreAnimationsEnabled())
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250));
            }

            CompletionReordering?.Invoke();
            _activeTasks.Remove(item);
            InsertBySortOrder(_completedTasks, item);
            if (ReferenceEquals(SelectedTask, item)) _completedExpanded = true;
            RebuildVisibleItems(viewportAlreadyCaptured: true);
        }
        catch
        {
            VisibleItemsUpdated?.Invoke();
            throw;
        }
    }

    private async Task ToggleTaskImportanceAsync(TaskItemViewModel item)
    {
        await Task.Yield();
        item.Model.Priority = item.IsImportant
            ? (int)TaskPriority.Undefined
            : (int)TaskPriority.High;
        await _taskStore.UpdateTaskAsync(item.Model, moveStarredToTop: _preferences.MoveStarredToTop);
        item.IsImportant = !item.IsImportant;
        UpdateItemDisplay(item);
        if (_currentQuery?.SortMode == SortMode.Manual && _preferences.MoveStarredToTop && item.IsImportant)
        {
            _activeTasks.Remove(item);
            _completedTasks.Remove(item);
            InsertManualItem(item.IsCompleted ? _completedTasks : _activeTasks, item);
            RebuildVisibleItems();
        }

        if (_currentQuery?.Kind == ListQueryKind.Important && !item.IsImportant)
        {
            _activeTasks.Remove(item);
            _completedTasks.Remove(item);
            RebuildVisibleItems();
        }

        NavigationDataChanged?.Invoke();
    }

    private async Task DeleteTaskAsync(TaskItemViewModel item)
    {
        await Task.Yield();
        DeleteTaskRequested?.Invoke(item);
    }

    internal async Task ConfirmDeleteTaskAsync(
        TaskItemViewModel item,
        CancellationToken cancellationToken = default)
    {
        await ConfirmDeleteTasksAsync([item], cancellationToken);
    }

    private async Task MoveTaskAsync(TaskItemViewModel item, long destinationListId)
    {
        await Task.Yield();
        await _taskStore.MoveTaskAsync(item.Uid, destinationListId);
        await ReloadAsync();
        NavigationDataChanged?.Invoke();
    }

    private async Task RenameTaskAsync(TaskItemViewModel item)
    {
        await Task.Yield();
        item.Model.Summary = item.EditTitle;
        await _taskStore.UpdateTaskAsync(item.Model);
        UpdateItemDisplay(item);
        RebuildVisibleItems();
    }

    private async Task MoveSelectedByAsync(int offset)
    {
        await Task.Yield();
        TaskItemViewModel? item = SelectedTask;
        if (item is null || item.IsCompleted)
        {
            return;
        }

        int index = _activeTasks.IndexOf(item);
        int destination = index + offset;
        if (index < 0 || destination < 0 || destination >= _activeTasks.Count)
        {
            return;
        }

        _activeTasks.RemoveAt(index);
        _activeTasks.Insert(destination, item);
        RebuildVisibleItems();
        await PersistVisibleOrderAsync();
    }

    private void BeginUndo(IReadOnlyList<TaskItemViewModel> items)
    {
        _undoCancellation?.Cancel();
        _undoCancellation?.Dispose();
        _undoCancellation = new CancellationTokenSource();
        _lastDeletedTaskUids = items.Select(item => item.Uid).ToArray();
        UndoMessage = string.Format(
            DisplayCulture.Current,
            _resources.GetString(items.Count == 1 ? "Undo_TaskDeleted" : "Undo_TasksDeleted"),
            items.Count == 1 ? items[0].Summary : items.Count);
        IsUndoVisible = true;
        _ = DismissUndoAsync(_undoCancellation.Token);
    }

    private async Task DismissUndoAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            CloseUndo();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void CloseUndo()
    {
        _lastDeletedTaskUids = null;
        IsUndoVisible = false;
        _undoCancellation?.Dispose();
        _undoCancellation = null;
    }

}
