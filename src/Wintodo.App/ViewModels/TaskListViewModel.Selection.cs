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

    internal bool SelectionIsInMyDay => SelectedTasks.All(item => item.Model.MyDayDate == _clock.Today);

    internal string SelectionLabel(string key) => string.Format(
        Services.DisplayCulture.Current,
        _resources.GetString(SelectionCount == 1 && key is not ("Selection_Due" or "Selection_Today"
            or "Selection_Tomorrow" or "Selection_Pick") ? key + "_Single" : key), SelectionCount);

    private async Task ApplySelectionAsync(TaskBatchAction action, DateOnly? date = null, long? destinationListId = null)
    {
        string[] uids = SelectedTasks.Select(item => item.Uid).ToArray();
        if (uids.Length == 0) return;
        await Task.Yield();
        await _taskStore.ApplyTaskBatchAsync(uids, action, date: date,
            destinationListId: destinationListId, moveStarredToTop: _preferences.MoveStarredToTop);
        if (action == TaskBatchAction.Complete) _completedExpanded = true;
        await ReloadAsync();
        NavigationDataChanged?.Invoke();
    }

    internal async Task MoveTasksToListAsync(IReadOnlyList<string> uids, long listId)
    {
        await _taskStore.ApplyTaskBatchAsync(uids, TaskBatchAction.Move, destinationListId: listId);
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
        await _taskStore.ApplyTaskBatchAsync(items.Select(item => item.Uid).ToArray(),
            TaskBatchAction.SetDueDate, date: date);
        await ReloadAsync();
        NavigationDataChanged?.Invoke();
    }

    internal async Task ConfirmDeleteTasksAsync(IReadOnlyList<TaskItemViewModel> items,
        CancellationToken cancellationToken = default)
    {
        string[] uids = items.Select(item => item.Uid).ToArray();
        await _taskStore.ApplyTaskBatchAsync(uids, TaskBatchAction.Delete, cancellationToken);
        foreach (TaskItemViewModel item in items)
        {
            _activeTasks.Remove(item);
            _completedTasks.Remove(item);
        }
        RebuildVisibleItems();
        BeginUndo(items);
        NavigationDataChanged?.Invoke();
    }
}
