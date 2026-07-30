using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Windows.ApplicationModel.Resources;
using Wintodo.Core.Models;
using Wintodo.Core.Queries;
using Wintodo.Core.Services;

namespace Wintodo.ViewModels;

public sealed partial class TaskListViewModel : ObservableObject
{
    private readonly ITaskStore _taskStore;
    private readonly IClock _clock;
    private readonly ResourceLoader _resources = new();
    private readonly Dictionary<string, TaskItemViewModel> _itemCache =
        new(StringComparer.Ordinal);
    private readonly List<TaskItemViewModel> _activeTasks = [];
    private readonly List<TaskItemViewModel> _completedTasks = [];
    private readonly CompletedHeaderViewModel _completedHeader;
    private IReadOnlyList<ShellListItemViewModel> _lists = [];
    private ShellListItemViewModel? _selectedList;
    private ListQuery? _currentQuery;
    private int _loadVersion;
    private string? _lastDeletedTaskUid;
    private CancellationTokenSource? _undoCancellation;
    private DateTimeOffset? _composerDue;
    private DateTimeOffset? _composerReminder;
    private string? _composerRecurrence;

    internal TaskListViewModel(ITaskStore taskStore, IClock clock)
    {
        _taskStore = taskStore;
        _clock = clock;
        _completedHeader = new CompletedHeaderViewModel(
            ToggleCompletedAsync,
            _resources.GetString("CompletedHeader/Text"));
        Title = _resources.GetString("Shell_MyDayTitle");
        Subtitle = FormatToday();
        EmptyGlyph = "\uE706";
        EmptyTitle = _resources.GetString("Empty_MyDayTitle");
        EmptySubtitle = _resources.GetString("Empty_MyDaySubtitle");
        SortLabel = _resources.GetString("TaskList_Sort");
        ShowCompletedMenuLabel = _resources.GetString("TaskList_HideCompleted");
        ComposerDueLabel = _resources.GetString("Composer_DueDate");
        ComposerReminderLabel = _resources.GetString("Composer_RemindMe");
        ComposerRepeatLabel = _resources.GetString("Composer_Repeat");
    }

    internal event Action? NavigationDataChanged;

    internal event Action<ShellListItemViewModel>? DeleteListRequested;

    internal event Action<TaskItemViewModel>? TaskCompletionChanged;

    public ObservableCollection<TaskListEntryViewModel> VisibleItems { get; } = [];

    public ObservableCollection<MoveTargetViewModel> MoveTargets { get; } = [];

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial string Subtitle { get; set; }

    [ObservableProperty]
    public partial bool HasSubtitle { get; set; } = true;

    [ObservableProperty]
    public partial int ActiveCount { get; set; }

    [ObservableProperty]
    public partial bool IsUserListSelected { get; set; }

    [ObservableProperty]
    public partial bool IsTaskListVisible { get; set; } = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTaskCommand))]
    public partial bool CanCreateTask { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTaskCommand))]
    public partial string NewTaskTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsComposerExpanded { get; set; }

    [ObservableProperty]
    public partial string ComposerDueLabel { get; set; }

    [ObservableProperty]
    public partial string ComposerReminderLabel { get; set; }

    [ObservableProperty]
    public partial string ComposerRepeatLabel { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; } = true;

    [ObservableProperty]
    public partial string EmptyGlyph { get; set; }

    [ObservableProperty]
    public partial string EmptyTitle { get; set; }

    [ObservableProperty]
    public partial string EmptySubtitle { get; set; }

    [ObservableProperty]
    public partial string SortLabel { get; set; }

    [ObservableProperty]
    public partial string ShowCompletedMenuLabel { get; set; }

    [ObservableProperty]
    public partial bool IsRenamingList { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CommitRenameListCommand))]
    public partial string RenameListName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial TaskItemViewModel? SelectedTask { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoDeleteCommand))]
    public partial bool IsUndoVisible { get; set; }

    [ObservableProperty]
    public partial string UndoMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CompletionAnnouncement { get; set; } = string.Empty;

    internal ListColor SelectedColor => _selectedList?.Color ?? ListColor.Blue;

    internal void SetLists(IReadOnlyList<ShellListItemViewModel> lists)
    {
        _lists = lists;
        MoveTargets.Clear();
        foreach (ShellListItemViewModel list in lists)
        {
            MoveTargets.Add(new MoveTargetViewModel(
                list.Id,
                list.DisplayName,
                $"Todo.Row.MoveTo.{list.Uid}"));
        }
    }

    internal Task LoadSmartListAsync(
        ListQueryKind kind,
        string title,
        bool isMyDay,
        CancellationToken cancellationToken = default)
    {
        _selectedList = null;
        IsUserListSelected = false;
        CanCreateTask = false;
        IsTaskListVisible = true;
        Title = title;
        Subtitle = isMyDay ? FormatToday() : string.Empty;
        HasSubtitle = isMyDay;
        ConfigureEmptyState(kind);

        SortMode sortMode = kind switch
        {
            ListQueryKind.Important => SortMode.Importance,
            ListQueryKind.Planned => SortMode.DueDate,
            ListQueryKind.Completed => SortMode.CreationDate,
            _ => SortMode.Manual,
        };
        return LoadAsync(
            new ListQuery
            {
                Kind = kind,
                SortMode = sortMode,
                IncludeCompleted = true,
            },
            cancellationToken);
    }

    internal Task LoadUserListAsync(
        ShellListItemViewModel list,
        CancellationToken cancellationToken = default)
    {
        _selectedList = list;
        IsUserListSelected = true;
        CanCreateTask = true;
        IsTaskListVisible = true;
        Title = list.DisplayName;
        Subtitle = string.Empty;
        HasSubtitle = false;
        EmptyGlyph = "\uE710";
        EmptyTitle = _resources.GetString("Empty_UserListTitle");
        EmptySubtitle = _resources.GetString("Empty_UserListSubtitle");
        SortLabel = FormatSortLabel(list.SortMode, list.SortDescending);
        ShowCompletedMenuLabel = list.ShowCompleted
            ? _resources.GetString("TaskList_HideCompleted")
            : _resources.GetString("TaskList_ShowCompleted");
        _completedHeader.IsExpanded = list.CompletedExpanded;

        return LoadAsync(
            new ListQuery
            {
                Kind = ListQueryKind.UserList,
                ListId = list.Id,
                SortMode = list.SortMode,
                SortDescending = list.SortDescending,
                IncludeCompleted = true,
            },
            cancellationToken);
    }

    internal void ShowSettings(string title)
    {
        _loadVersion++;
        _selectedList = null;
        _currentQuery = null;
        IsUserListSelected = false;
        CanCreateTask = false;
        IsTaskListVisible = false;
        Title = title;
        Subtitle = string.Empty;
        HasSubtitle = false;
        _activeTasks.Clear();
        _completedTasks.Clear();
        RebuildVisibleItems();
    }

    internal async Task DeleteListAsync(
        ShellListItemViewModel list,
        CancellationToken cancellationToken = default)
    {
        await _taskStore.DeleteListAsync(list.Id, cancellationToken);
        if (ReferenceEquals(_selectedList, list))
        {
            _selectedList = null;
            _currentQuery = null;
        }
    }

    internal async Task MoveTaskToListAsync(
        string taskUid,
        long destinationListId,
        CancellationToken cancellationToken = default)
    {
        await _taskStore.MoveTaskAsync(taskUid, destinationListId, cancellationToken: cancellationToken);
        await ReloadAsync(cancellationToken);
    }

    internal async Task PersistVisibleOrderAsync(CancellationToken cancellationToken = default)
    {
        _activeTasks.Clear();
        foreach (TaskListEntryViewModel entry in VisibleItems)
        {
            if (entry is TaskItemViewModel { IsCompleted: false } task)
            {
                _activeTasks.Add(task);
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

        bool wasCompleted = item.IsCompleted;
        item.Model = task;
        UpdateItemDisplay(item);
        if (wasCompleted == item.IsCompleted)
        {
            return;
        }

        if (item.IsCompleted)
        {
            _activeTasks.Remove(item);
            InsertBySortOrder(_completedTasks, item);
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
        BeginUndo(item);
    }

    [RelayCommand]
    private void BeginComposer()
    {
        if (CanCreateTask)
        {
            IsComposerExpanded = true;
        }
    }

    [RelayCommand]
    private void CancelComposer()
    {
        NewTaskTitle = string.Empty;
        _composerDue = null;
        _composerReminder = null;
        _composerRecurrence = null;
        UpdateComposerLabels();
        IsComposerExpanded = false;
    }

    private bool CanAddTask()
    {
        return CanCreateTask && !string.IsNullOrWhiteSpace(NewTaskTitle);
    }

    [RelayCommand(CanExecute = nameof(CanAddTask))]
    private async Task AddTaskAsync()
    {
        await Task.Yield();
        ShellListItemViewModel? list = _selectedList;
        if (list is null)
        {
            return;
        }

        var task = new TodoTask
        {
            ListId = list.Id,
            Summary = NewTaskTitle.Trim(),
            Due = _composerDue,
            DueIsDateOnly = _composerDue is not null,
            RRule = _composerRecurrence,
        };
        TodoTask created = await _taskStore.CreateTaskAsync(task);
        if (_composerReminder is not null)
        {
            await _taskStore.SetAlarmAsync(
                created.Uid,
                new TaskAlarm
                {
                    Action = AlarmAction.Display,
                    Trigger = _composerReminder,
                    Description = created.Summary,
                });
        }

        NewTaskTitle = string.Empty;
        _composerDue = null;
        _composerReminder = null;
        _composerRecurrence = null;
        UpdateComposerLabels();
        TaskItemViewModel item = GetOrCreateItem(created);
        UpdateItemDisplay(item);
        _activeTasks.Add(item);
        RebuildVisibleItems();
    }

    [RelayCommand]
    private void SetComposerDue(string value)
    {
        _composerDue = value switch
        {
            "Today" => StartOfLocalDay(_clock.Today),
            "Tomorrow" => StartOfLocalDay(_clock.Today.AddDays(1)),
            _ => null,
        };
        UpdateComposerLabels();
    }

    [RelayCommand]
    private void SetComposerReminder(string value)
    {
        DateTimeOffset now = _clock.UtcNow.ToLocalTime();
        _composerReminder = value switch
        {
            "LaterToday" => now.AddHours(3),
            "TomorrowMorning" => new DateTimeOffset(
                _clock.Today.AddDays(1).ToDateTime(new TimeOnly(9, 0)),
                TimeZoneInfo.Local.GetUtcOffset(
                    _clock.Today.AddDays(1).ToDateTime(new TimeOnly(9, 0)))),
            _ => null,
        };
        UpdateComposerLabels();
    }

    [RelayCommand]
    private void SetComposerRepeat(string value)
    {
        _composerRecurrence = value switch
        {
            "Daily" => "FREQ=DAILY",
            "Weekly" => "FREQ=WEEKLY",
            _ => null,
        };
        UpdateComposerLabels();
    }

    [RelayCommand]
    private void BeginRenameList()
    {
        if (_selectedList is null)
        {
            return;
        }

        RenameListName = _selectedList.DisplayName;
        IsRenamingList = true;
    }

    private bool CanCommitRenameList()
    {
        return _selectedList is not null && !string.IsNullOrWhiteSpace(RenameListName);
    }

    [RelayCommand(CanExecute = nameof(CanCommitRenameList))]
    private async Task CommitRenameListAsync()
    {
        await Task.Yield();
        ShellListItemViewModel? list = _selectedList;
        if (list is null)
        {
            return;
        }

        string displayName = RenameListName.Trim();
        await _taskStore.RenameListAsync(list.Id, displayName);
        list.DisplayName = displayName;
        Title = displayName;
        IsRenamingList = false;
        SetLists(_lists);
        NavigationDataChanged?.Invoke();
    }

    [RelayCommand]
    private void CancelRenameList()
    {
        IsRenamingList = false;
    }

    [RelayCommand]
    private void RequestDeleteList()
    {
        if (_selectedList is not null)
        {
            DeleteListRequested?.Invoke(_selectedList);
        }
    }

    [RelayCommand]
    private async Task ChangeListColorAsync(string value)
    {
        await Task.Yield();
        ShellListItemViewModel? list = _selectedList;
        if (list is null
            || !Enum.TryParse(value, ignoreCase: false, out ListColor color))
        {
            return;
        }

        await _taskStore.SetListColorAsync(list.Id, color);
        list.Color = color;
        OnPropertyChanged(nameof(SelectedColor));
        NavigationDataChanged?.Invoke();
    }

    [RelayCommand]
    private async Task SortByAsync(string value)
    {
        await Task.Yield();
        if (!Enum.TryParse(value, ignoreCase: false, out SortMode sortMode))
        {
            return;
        }

        if (_selectedList is not null)
        {
            _selectedList.SortMode = sortMode;
            await _taskStore.SetListSortAsync(
                _selectedList.Id,
                sortMode,
                _selectedList.SortDescending);
            SortLabel = FormatSortLabel(sortMode, _selectedList.SortDescending);
        }

        if (_currentQuery is not null)
        {
            _currentQuery.SortMode = sortMode;
            await ReloadAsync();
        }
    }

    [RelayCommand]
    private async Task ToggleSortDirectionAsync()
    {
        await Task.Yield();
        if (_currentQuery is null)
        {
            return;
        }

        _currentQuery.SortDescending = !_currentQuery.SortDescending;
        if (_selectedList is not null)
        {
            _selectedList.SortDescending = _currentQuery.SortDescending;
            await _taskStore.SetListSortAsync(
                _selectedList.Id,
                _selectedList.SortMode,
                _selectedList.SortDescending);
            SortLabel = FormatSortLabel(
                _selectedList.SortMode,
                _selectedList.SortDescending);
        }

        await ReloadAsync();
    }

    [RelayCommand]
    private async Task ToggleShowCompletedAsync()
    {
        await Task.Yield();
        if (_selectedList is null)
        {
            return;
        }

        _selectedList.ShowCompleted = !_selectedList.ShowCompleted;
        await _taskStore.SetListShowCompletedAsync(
            _selectedList.Id,
            _selectedList.ShowCompleted);
        ShowCompletedMenuLabel = _selectedList.ShowCompleted
            ? _resources.GetString("TaskList_HideCompleted")
            : _resources.GetString("TaskList_ShowCompleted");
        RebuildVisibleItems();
    }

    private async Task ToggleCompletedAsync()
    {
        await Task.Yield();
        bool expanded = !_completedHeader.IsExpanded;
        _completedHeader.IsExpanded = expanded;
        if (_selectedList is not null)
        {
            _selectedList.CompletedExpanded = expanded;
            await _taskStore.SetListCompletedExpandedAsync(
                _selectedList.Id,
                expanded);
        }

        RebuildVisibleItems();
    }

    [RelayCommand]
    private async Task DeleteSelectedTaskAsync()
    {
        if (SelectedTask is not null)
        {
            await DeleteTaskAsync(SelectedTask);
        }
    }

    [RelayCommand]
    private async Task ToggleSelectedCompletionAsync()
    {
        if (SelectedTask is not null)
        {
            await ToggleTaskCompletionAsync(SelectedTask);
        }
    }

    [RelayCommand]
    private async Task ToggleSelectedImportanceAsync()
    {
        if (SelectedTask is not null)
        {
            await ToggleTaskImportanceAsync(SelectedTask);
        }
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
        return IsUndoVisible && _lastDeletedTaskUid is not null;
    }

    [RelayCommand(CanExecute = nameof(CanUndoDelete))]
    private async Task UndoDeleteAsync()
    {
        await Task.Yield();
        string? uid = _lastDeletedTaskUid;
        if (uid is null)
        {
            return;
        }

        _undoCancellation?.Cancel();
        await _taskStore.RestoreTaskAsync(uid);
        CloseUndo();
        await ReloadAsync();
    }

    private async Task LoadAsync(ListQuery query, CancellationToken cancellationToken)
    {
        _currentQuery = query;
        int loadVersion = ++_loadVersion;
        IReadOnlyList<TodoTask> tasks =
            await _taskStore.GetTasksAsync(query, cancellationToken);
        if (loadVersion != _loadVersion)
        {
            return;
        }

        _activeTasks.Clear();
        _completedTasks.Clear();
        foreach (TodoTask task in tasks)
        {
            TaskItemViewModel item = GetOrCreateItem(task);
            UpdateItemDisplay(item);
            if (item.IsCompleted)
            {
                _completedTasks.Add(item);
            }
            else
            {
                _activeTasks.Add(item);
            }
        }

        if (_selectedList is null)
        {
            _completedHeader.IsExpanded = query.Kind == ListQueryKind.Completed;
        }

        RebuildVisibleItems();
    }

    private Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        return _currentQuery is null
            ? Task.CompletedTask
            : LoadAsync(_currentQuery, cancellationToken);
    }

    private TaskItemViewModel GetOrCreateItem(TodoTask task)
    {
        if (_itemCache.TryGetValue(task.Uid, out TaskItemViewModel? existing))
        {
            existing.Model = task;
            existing.Summary = task.Summary;
            existing.IsCompleted =
                task.Status == Wintodo.Core.Models.TaskStatus.Completed;
            existing.IsImportant = task.Priority is >= 1 and <= 4;
            return existing;
        }

        var item = new TaskItemViewModel(
            task,
            ToggleTaskCompletionAsync,
            ToggleTaskImportanceAsync,
            DeleteTaskAsync,
            MoveTaskAsync);
        _itemCache.Add(task.Uid, item);
        return item;
    }

    private async Task ToggleTaskCompletionAsync(TaskItemViewModel item)
    {
        await Task.Yield();
        if (item.IsCompleted)
        {
            await _taskStore.UncompleteTaskAsync(item.Uid);
            item.Model.Status = Wintodo.Core.Models.TaskStatus.NeedsAction;
            item.Model.PercentComplete = 0;
            item.Model.CompletedAt = null;
            item.IsCompleted = false;
            item.CompletionDateText = string.Empty;
            _completedTasks.Remove(item);
            InsertBySortOrder(_activeTasks, item);
            UpdateItemDisplay(item);
            RebuildVisibleItems();
            CompletionAnnouncement = string.Format(
                CultureInfo.CurrentCulture,
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
            CultureInfo.CurrentCulture,
            item.IsCompleted
                ? _resources.GetString("Task_CompletionNotification")
                : _resources.GetString("Task_UncompletionNotification"),
            item.Summary);
        TaskCompletionChanged?.Invoke(item);
        if (!item.IsCompleted)
        {
            return;
        }

        await Task.Delay(TimeSpan.FromMilliseconds(250));
        _activeTasks.Remove(item);
        InsertBySortOrder(_completedTasks, item);
        RebuildVisibleItems();
    }

    private async Task ToggleTaskImportanceAsync(TaskItemViewModel item)
    {
        await Task.Yield();
        item.Model.Priority = item.IsImportant
            ? (int)TaskPriority.Undefined
            : (int)TaskPriority.High;
        await _taskStore.UpdateTaskAsync(item.Model);
        item.IsImportant = !item.IsImportant;
        UpdateItemDisplay(item);

        if (_currentQuery?.Kind == ListQueryKind.Important && !item.IsImportant)
        {
            _activeTasks.Remove(item);
            _completedTasks.Remove(item);
            RebuildVisibleItems();
        }
    }

    private async Task DeleteTaskAsync(TaskItemViewModel item)
    {
        await Task.Yield();
        await _taskStore.DeleteTaskAsync(item.Uid);
        _activeTasks.Remove(item);
        _completedTasks.Remove(item);
        RebuildVisibleItems();
        BeginUndo(item);
    }

    private async Task MoveTaskAsync(TaskItemViewModel item, long destinationListId)
    {
        await Task.Yield();
        await _taskStore.MoveTaskAsync(item.Uid, destinationListId);
        await ReloadAsync();
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

    private void BeginUndo(TaskItemViewModel item)
    {
        _undoCancellation?.Cancel();
        _undoCancellation?.Dispose();
        _undoCancellation = new CancellationTokenSource();
        _lastDeletedTaskUid = item.Uid;
        UndoMessage = string.Format(
            CultureInfo.CurrentCulture,
            _resources.GetString("Undo_TaskDeleted"),
            item.Summary);
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
        _lastDeletedTaskUid = null;
        IsUndoVisible = false;
        _undoCancellation?.Dispose();
        _undoCancellation = null;
    }

    private void RebuildVisibleItems()
    {
        TaskItemViewModel? selected = SelectedTask;
        VisibleItems.Clear();
        foreach (TaskItemViewModel item in _activeTasks)
        {
            VisibleItems.Add(item);
        }

        bool showCompleted = _selectedList?.ShowCompleted ?? true;
        _completedHeader.Count = _completedTasks.Count;
        if (showCompleted && _completedTasks.Count > 0)
        {
            VisibleItems.Add(_completedHeader);
            if (_completedHeader.IsExpanded)
            {
                foreach (TaskItemViewModel item in _completedTasks)
                {
                    VisibleItems.Add(item);
                }
            }
        }

        ActiveCount = _activeTasks.Count;
        IsEmpty = _activeTasks.Count == 0 && _completedTasks.Count == 0;
        if (selected is not null && VisibleItems.Contains(selected))
        {
            SelectedTask = selected;
        }
        else
        {
            SelectedTask = null;
        }
    }

    private void UpdateItemDisplay(TaskItemViewModel item)
    {
        TodoTask task = item.Model;
        item.Summary = task.Summary;
        item.IsCompleted =
            task.Status == Wintodo.Core.Models.TaskStatus.Completed;
        item.IsImportant = task.Priority is >= 1 and <= 4;

        var metadata = new List<string>(4);
        if (_currentQuery?.Kind != ListQueryKind.UserList)
        {
            string? listName = FindListName(task.ListId);
            if (!string.IsNullOrEmpty(listName))
            {
                metadata.Add(listName);
            }
        }

        if (task.Due is not null)
        {
            metadata.Add(FormatDueDate(task.Due.Value));
        }

        if (!string.IsNullOrWhiteSpace(task.RRule))
        {
            metadata.Add(_resources.GetString("Task_Metadata_Repeats"));
        }

        if (task.MyDayDate is not null
            && _currentQuery?.Kind != ListQueryKind.MyDay)
        {
            metadata.Add(_resources.GetString("Task_Metadata_MyDay"));
        }

        item.Metadata = string.Join(" \u00B7 ", metadata);
        item.CompletionDateText = task.CompletedAt is null
            ? string.Empty
            : string.Format(
                CultureInfo.CurrentCulture,
                _resources.GetString("Task_Metadata_Completed"),
                task.CompletedAt.Value.ToLocalTime());

        var accessibleParts = new List<string>(4) { task.Summary };
        if (!string.IsNullOrEmpty(item.Metadata))
        {
            accessibleParts.Add(item.Metadata);
        }

        if (item.IsImportant)
        {
            accessibleParts.Add(_resources.GetString("Task_Accessible_Important"));
        }

        if (item.IsCompleted)
        {
            accessibleParts.Add(_resources.GetString("Task_Accessible_Completed"));
        }

        item.AccessibleName = string.Join(", ", accessibleParts);
    }

    private void ConfigureEmptyState(ListQueryKind kind)
    {
        (EmptyGlyph, EmptyTitle, EmptySubtitle) = kind switch
        {
            ListQueryKind.MyDay => (
                "\uE706",
                _resources.GetString("Empty_MyDayTitle"),
                _resources.GetString("Empty_MyDaySubtitle")),
            ListQueryKind.Important => (
                "\uE734",
                _resources.GetString("Empty_ImportantTitle"),
                _resources.GetString("Empty_ImportantSubtitle")),
            ListQueryKind.Planned => (
                "\uE787",
                _resources.GetString("Empty_PlannedTitle"),
                _resources.GetString("Empty_PlannedSubtitle")),
            ListQueryKind.Completed => (
                "\uE930",
                _resources.GetString("Empty_CompletedTitle"),
                _resources.GetString("Empty_CompletedSubtitle")),
            _ => (
                "\uE8FD",
                _resources.GetString("Empty_AllTitle"),
                _resources.GetString("Empty_AllSubtitle")),
        };
    }

    private void UpdateComposerLabels()
    {
        ComposerDueLabel = _composerDue is null
            ? _resources.GetString("Composer_DueDate")
            : FormatDueDate(_composerDue.Value);
        ComposerReminderLabel = _composerReminder is null
            ? _resources.GetString("Composer_RemindMe")
            : _composerReminder.Value.ToString("g", CultureInfo.CurrentCulture);
        ComposerRepeatLabel = _composerRecurrence switch
        {
            "FREQ=DAILY" => _resources.GetString("Composer_Daily"),
            "FREQ=WEEKLY" => _resources.GetString("Composer_Weekly"),
            _ => _resources.GetString("Composer_Repeat"),
        };
    }

    private string FormatDueDate(DateTimeOffset due)
    {
        DateOnly date = DateOnly.FromDateTime(due.LocalDateTime);
        int dayOffset = date.DayNumber - _clock.Today.DayNumber;
        return dayOffset switch
        {
            -1 => _resources.GetString("Task_Metadata_Yesterday"),
            0 => _resources.GetString("Task_Metadata_Today"),
            1 => _resources.GetString("Task_Metadata_Tomorrow"),
            >= 2 and <= 7 => due.ToString("dddd", CultureInfo.CurrentCulture),
            _ => due.ToString("d MMM", CultureInfo.CurrentCulture),
        };
    }

    private string FormatSortLabel(SortMode sortMode, bool descending)
    {
        string mode = _resources.GetString($"Sort_{sortMode}");
        string direction = descending
            ? _resources.GetString("Sort_Descending")
            : _resources.GetString("Sort_Ascending");
        return string.Format(
            CultureInfo.CurrentCulture,
            _resources.GetString("Sort_Label"),
            mode,
            direction);
    }

    private string? FindListName(long listId)
    {
        foreach (ShellListItemViewModel list in _lists)
        {
            if (list.Id == listId)
            {
                return list.DisplayName;
            }
        }

        return null;
    }

    private string FormatToday()
    {
        return _clock.Today.ToString("D", CultureInfo.CurrentCulture);
    }

    private static DateTimeOffset StartOfLocalDay(DateOnly date)
    {
        DateTime local = date.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    private static void InsertBySortOrder(
        List<TaskItemViewModel> items,
        TaskItemViewModel item)
    {
        int index = items.FindIndex(candidate => candidate.SortOrder > item.SortOrder);
        if (index < 0)
        {
            items.Add(item);
        }
        else
        {
            items.Insert(index, item);
        }
    }
}
