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
    private IReadOnlyList<ShellListItemViewModel> _lists = [];
    private ShellListItemViewModel? _selectedList;
    private ListQuery? _currentQuery;
    private DateOnly _lastObservedToday;
    private string _searchText = string.Empty;
    private bool _completedExpanded;
    private int _loadVersion;
    private string? _lastDeletedTaskUid;
    private CancellationTokenSource? _undoCancellation;
    private DateTimeOffset? _composerDue;
    private DateTimeOffset? _composerReminder;
    private string? _composerRecurrence;
    private bool _composerImportant;
    private bool _composerMyDay;
    private long? _pinnedComposerListId;
    private ListQueryKind _composerKind = ListQueryKind.MyDay;

    internal TaskListViewModel(ITaskStore taskStore, IClock clock)
    {
        _taskStore = taskStore;
        _clock = clock;
        _lastObservedToday = clock.Today;
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

    internal event Action<ShellListItemViewModel>? ExportListRequested;

    internal event Action<TaskItemViewModel>? TaskCompletionChanged;

    internal event Action<TaskItemViewModel>? DeleteTaskRequested;

    internal event Action? ShortcutsRequested;

    public ObservableCollection<TaskListEntryViewModel> VisibleItems { get; } = [];

    public ObservableCollection<TaskListGroupViewModel> TaskGroups { get; } = [];

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
    [NotifyCanExecuteChangedFor(nameof(RequestExportListCommand))]
    public partial bool IsUserListSelected { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RequestExportListCommand))]
    public partial bool IsTransferBusy { get; set; }

    [ObservableProperty]
    public partial bool IsTaskListVisible { get; set; } = true;

    [ObservableProperty]
    public partial bool IsComposerVisible { get; set; } = true;

    [ObservableProperty]
    public partial bool AreListCommandsVisible { get; set; } = true;

    [ObservableProperty]
    public partial bool CanReorderTasks { get; set; }

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
    public partial string ComposerTargetLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial long? ComposerTargetListId { get; set; }

    [ObservableProperty]
    public partial bool IsComposerTargetVisible { get; set; }

    [ObservableProperty]
    public partial bool HasComposerDue { get; set; }

    [ObservableProperty]
    public partial bool HasComposerReminder { get; set; }

    [ObservableProperty]
    public partial bool HasComposerRepeat { get; set; }

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

    [ObservableProperty]
    public partial bool IsCompactDensity { get; set; }

    internal bool AnimationsEnabled { get; set; } = true;

    internal ListColor SelectedColor => _selectedList?.Color ?? ListColor.Blue;

    internal void SetLists(IReadOnlyList<ShellListItemViewModel> lists)
    {
        _lists = lists;
        MoveTargets.Clear();
        foreach (ShellListItemViewModel list in lists)
        {
            MoveTargets.Add(new MoveTargetViewModel(
                list.Id,
                list.Uid,
                list.DisplayName));
        }

        ResolveComposerTarget();
    }

    internal Task LoadSmartListAsync(
        ListQueryKind kind,
        string title,
        bool isMyDay,
        CancellationToken cancellationToken = default)
    {
        _selectedList = null;
        IsUserListSelected = false;
        IsTaskListVisible = true;
        IsComposerVisible = kind != ListQueryKind.Completed;
        AreListCommandsVisible = true;
        CanReorderTasks = false;
        Title = title;
        Subtitle = isMyDay ? FormatToday() : string.Empty;
        HasSubtitle = isMyDay;
        ConfigureEmptyState(kind);
        _composerKind = kind;
        ResetComposer();

        SortMode sortMode = kind switch
        {
            ListQueryKind.Important => SortMode.Importance,
            ListQueryKind.Planned => SortMode.DueDate,
            ListQueryKind.Completed => SortMode.CreationDate,
            ListQueryKind.All => SortMode.CreationDate,
            _ => SortMode.Manual,
        };
        SortLabel = FormatSortLabel(sortMode, descending: false);
        _completedExpanded = kind == ListQueryKind.Completed;
        _searchText = string.Empty;
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
        IsTaskListVisible = true;
        IsComposerVisible = true;
        AreListCommandsVisible = true;
        CanReorderTasks = list.SortMode == SortMode.Manual;
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
        _completedExpanded = list.CompletedExpanded;
        _searchText = string.Empty;
        _composerKind = ListQueryKind.UserList;
        ResetComposer();

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
        IsTaskListVisible = false;
        IsComposerVisible = false;
        AreListCommandsVisible = false;
        CanReorderTasks = false;
        ResetComposer();
        Title = title;
        Subtitle = string.Empty;
        HasSubtitle = false;
        _activeTasks.Clear();
        _completedTasks.Clear();
        RebuildVisibleItems();
    }

    internal async Task LoadSearchAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        _loadVersion++;
        _selectedList = null;
        _currentQuery = null;
        _searchText = text.Trim();
        IsUserListSelected = false;
        IsTaskListVisible = true;
        IsComposerVisible = false;
        AreListCommandsVisible = false;
        CanReorderTasks = false;
        ResetComposer();
        Title = _resources.GetString("Search_Title");
        Subtitle = string.Empty;
        HasSubtitle = false;
        EmptyGlyph = "\uE721";
        EmptyTitle = string.Format(
            CultureInfo.CurrentCulture,
            _resources.GetString("Empty_SearchTitle"),
            _searchText);
        EmptySubtitle = _resources.GetString("Empty_SearchSubtitle");

        int loadVersion = _loadVersion;
        IReadOnlyList<TodoTask> tasks =
            await _taskStore.SearchAsync(_searchText, limit: 1000, cancellationToken);
        if (loadVersion != _loadVersion)
        {
            return;
        }

        LoadTaskCollections(tasks, highlightText: _searchText);
        RebuildVisibleItems();
    }

    internal async Task<bool> RefreshForDateChangeAsync(
        CancellationToken cancellationToken = default)
    {
        DateOnly today = _clock.Today;
        if (today == _lastObservedToday)
        {
            return false;
        }

        _lastObservedToday = today;
        if (_currentQuery?.Kind == ListQueryKind.MyDay)
        {
            Subtitle = FormatToday();
            await ReloadAsync(cancellationToken);
            return true;
        }

        foreach (TaskItemViewModel item in _activeTasks)
        {
            UpdateItemDisplay(item);
        }

        foreach (TaskItemViewModel item in _completedTasks)
        {
            UpdateItemDisplay(item);
        }

        RebuildVisibleItems();
        return true;
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
        if (!CanReorderTasks)
        {
            return;
        }

        _activeTasks.Clear();
        foreach (TaskListGroupViewModel group in TaskGroups)
        {
            foreach (TaskItemViewModel task in group)
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
        ResetComposer();
    }

    private bool CanAddTask()
    {
        return CanCreateTask && !string.IsNullOrWhiteSpace(NewTaskTitle);
    }

    [RelayCommand(CanExecute = nameof(CanAddTask))]
    private async Task AddTaskAsync()
    {
        await Task.Yield();
        long? listId = ComposerTargetListId;
        if (listId is null)
        {
            return;
        }

        DateTimeOffset? reminder = _composerReminder;
        var task = new TodoTask
        {
            ListId = listId.Value,
            Summary = NewTaskTitle.Trim(),
            Due = _composerDue,
            DueIsDateOnly = _composerDue is not null,
            RRule = _composerRecurrence,
            Priority = _composerImportant
                ? (int)TaskPriority.High
                : (int)TaskPriority.Undefined,
            MyDayDate = _composerMyDay ? _clock.Today : null,
        };
        TodoTask created = await _taskStore.CreateTaskAsync(task);
        if (reminder is not null)
        {
            await _taskStore.SetAlarmAsync(
                created.Uid,
                new TaskAlarm
                {
                    Action = AlarmAction.Display,
                    Trigger = reminder,
                    Description = created.Summary,
                });
        }

        ResetComposer(collapse: false);
        TaskItemViewModel item = GetOrCreateItem(created);
        UpdateItemDisplay(item);
        _activeTasks.Add(item);
        RebuildVisibleItems();
    }

    [RelayCommand]
    private void SetComposerList(long listId)
    {
        ShellListItemViewModel? target =
            _lists.FirstOrDefault(list => list.Id == listId);
        if (target is null)
        {
            return;
        }

        _pinnedComposerListId = target.Id;
        ComposerTargetListId = target.Id;
        ComposerTargetLabel = target.DisplayName;
        CanCreateTask = IsComposerVisible;
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

    private bool CanRequestExportList()
    {
        return _selectedList is not null && IsUserListSelected && !IsTransferBusy;
    }

    [RelayCommand(CanExecute = nameof(CanRequestExportList))]
    private void RequestExportList()
    {
        if (_selectedList is not null)
        {
            ExportListRequested?.Invoke(_selectedList);
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
    private async Task ChangeListBackgroundAsync(string value)
    {
        await Task.Yield();
        ShellListItemViewModel? list = _selectedList;
        string? theme = string.Equals(value, "None", StringComparison.Ordinal)
            ? null
            : value;
        if (list is null || !IsKnownBackgroundTheme(theme))
        {
            return;
        }

        await _taskStore.SetListBackgroundThemeAsync(list.Id, theme);
        list.BackgroundTheme = theme;
        NavigationDataChanged?.Invoke();
    }

    [RelayCommand]
    private void RequestShortcuts()
    {
        ShortcutsRequested?.Invoke();
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
            CanReorderTasks = sortMode == SortMode.Manual;
        }

        if (_currentQuery is not null)
        {
            _currentQuery.SortMode = sortMode;
            SortLabel = FormatSortLabel(sortMode, _currentQuery.SortDescending);
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
        else
        {
            SortLabel = FormatSortLabel(
                _currentQuery.SortMode,
                _currentQuery.SortDescending);
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
        bool expanded = !_completedExpanded;
        _completedExpanded = expanded;
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
    private async Task ToggleSelectedMyDayAsync()
    {
        await Task.Yield();
        TaskItemViewModel? item = SelectedTask;
        if (item is null)
        {
            return;
        }

        bool isInMyDay = item.Model.MyDayDate == _clock.Today;
        item.Model.MyDayDate = isInMyDay ? null : _clock.Today;
        await _taskStore.UpdateTaskAsync(item.Model);
        UpdateItemDisplay(item);
        if (isInMyDay && _currentQuery?.Kind == ListQueryKind.MyDay)
        {
            _activeTasks.Remove(item);
            _completedTasks.Remove(item);
            RebuildVisibleItems();
        }
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
        LoadTaskCollections(tasks, highlightText: string.Empty);

        RebuildVisibleItems();
    }

    private void LoadTaskCollections(
        IReadOnlyList<TodoTask> tasks,
        string highlightText)
    {
        _activeTasks.Clear();
        _completedTasks.Clear();
        foreach (TodoTask task in tasks)
        {
            TaskItemViewModel item = GetOrCreateItem(task);
            item.HighlightText = highlightText;
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
            MoveTaskAsync,
            RenameTaskAsync)
        {
            IsCompactDensity = IsCompactDensity,
        };
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

        if (AnimationsEnabled)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

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
        DeleteTaskRequested?.Invoke(item);
    }

    internal async Task ConfirmDeleteTaskAsync(
        TaskItemViewModel item,
        CancellationToken cancellationToken = default)
    {
        await _taskStore.DeleteTaskAsync(item.Uid, cancellationToken);
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
        TaskGroups.Clear();

        if (!string.IsNullOrEmpty(_searchText))
        {
            BuildSearchGroups();
        }
        else if (_currentQuery?.Kind == ListQueryKind.Planned)
        {
            BuildPlannedGroups();
        }
        else if (_currentQuery?.Kind == ListQueryKind.Completed)
        {
            AddTaskGroup(CreateHiddenGroup(_completedTasks));
        }
        else
        {
            AddTaskGroup(CreateHiddenGroup(_activeTasks));

            bool showCompleted = _selectedList?.ShowCompleted ?? true;
            if (showCompleted && _completedTasks.Count > 0)
            {
                var completedGroup = new TaskListGroupViewModel(
                    TaskListGroupKind.Completed,
                    _resources.GetString("CompletedHeader/Text"),
                    "Todo.List.CompletedHeader",
                    toggle: ToggleCompletedAsync,
                    accessibleName: string.Format(
                        CultureInfo.CurrentCulture,
                        _resources.GetString("CompletedHeader_AccessibleName"),
                        _completedTasks.Count))
                {
                    CountOverride = _completedTasks.Count,
                    IsExpanded = _completedExpanded,
                };
                if (_completedExpanded)
                {
                    AddItems(completedGroup, _completedTasks);
                }

                TaskGroups.Add(completedGroup);
                foreach (TaskItemViewModel item in completedGroup)
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

    private void BuildSearchGroups()
    {
        foreach (ShellListItemViewModel list in _lists.OrderBy(item => item.SortOrder))
        {
            List<TaskItemViewModel> matches =
                _activeTasks.Concat(_completedTasks)
                    .Where(item => item.ListId == list.Id)
                    .ToList();
            if (matches.Count == 0)
            {
                continue;
            }

            var group = new TaskListGroupViewModel(
                TaskListGroupKind.Named,
                list.DisplayName,
                $"Todo.List.SearchGroup.{list.Uid}");
            AddItems(group, matches);
            AddTaskGroup(group);
        }
    }

    private void BuildPlannedGroups()
    {
        var buckets = new Dictionary<string, List<TaskItemViewModel>>(StringComparer.Ordinal)
        {
            ["Overdue"] = [],
            ["Today"] = [],
            ["Tomorrow"] = [],
            ["ThisWeek"] = [],
            ["Later"] = [],
            ["NoDate"] = [],
        };
        foreach (TaskItemViewModel item in _activeTasks.Concat(_completedTasks))
        {
            buckets[GetPlannedBucket(item.Model.Due)].Add(item);
        }

        AddNamedBucket(
            buckets["Overdue"],
            "Planned_Overdue",
            "Overdue",
            isCritical: true);
        AddNamedBucket(buckets["Today"], "Planned_Today", "Today");
        AddNamedBucket(buckets["Tomorrow"], "Planned_Tomorrow", "Tomorrow");
        AddNamedBucket(buckets["ThisWeek"], "Planned_ThisWeek", "ThisWeek");
        AddNamedBucket(buckets["Later"], "Planned_Later", "Later");
        AddNamedBucket(buckets["NoDate"], "Planned_NoDate", "NoDate");
    }

    private void AddNamedBucket(
        IReadOnlyList<TaskItemViewModel> items,
        string resourceKey,
        string automationSuffix,
        bool isCritical = false)
    {
        if (items.Count == 0)
        {
            return;
        }

        var group = new TaskListGroupViewModel(
            TaskListGroupKind.Named,
            _resources.GetString(resourceKey),
            $"Todo.List.PlannedGroup.{automationSuffix}",
            isCritical);
        AddItems(group, items);
        AddTaskGroup(group);
    }

    private void AddTaskGroup(TaskListGroupViewModel group)
    {
        if (group.Count == 0)
        {
            return;
        }

        TaskGroups.Add(group);
        foreach (TaskItemViewModel item in group)
        {
            VisibleItems.Add(item);
        }
    }

    private static TaskListGroupViewModel CreateHiddenGroup(
        IEnumerable<TaskItemViewModel> items)
    {
        var group = new TaskListGroupViewModel(
            TaskListGroupKind.Hidden,
            string.Empty,
            string.Empty);
        AddItems(group, items);
        return group;
    }

    private static void AddItems(
        TaskListGroupViewModel group,
        IEnumerable<TaskItemViewModel> items)
    {
        foreach (TaskItemViewModel item in items)
        {
            group.Add(item);
        }
    }

    private string GetPlannedBucket(DateTimeOffset? due)
    {
        if (due is null)
        {
            return "NoDate";
        }

        DateOnly dueDate = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(due.Value, _clock.LocalTimeZone).DateTime);
        int offset = dueDate.DayNumber - _clock.Today.DayNumber;
        if (offset < 0)
        {
            return "Overdue";
        }

        if (offset == 0)
        {
            return "Today";
        }

        if (offset == 1)
        {
            return "Tomorrow";
        }

        DayOfWeek firstDayOfWeek =
            CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        int currentDayIndex =
            ((int)_clock.Today.DayOfWeek - (int)firstDayOfWeek + 7) % 7;
        DateOnly endOfWeek = _clock.Today.AddDays(6 - currentDayIndex);
        return dueDate <= endOfWeek ? "ThisWeek" : "Later";
    }

    private void UpdateItemDisplay(TaskItemViewModel item)
    {
        TodoTask task = item.Model;
        item.Summary = task.Summary;
        item.IsCompleted =
            task.Status == Wintodo.Core.Models.TaskStatus.Completed;
        item.IsImportant = task.Priority is >= 1 and <= 4;
        DateOnly? dueDate = task.Due is null
            ? null
            : DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTime(
                    task.Due.Value,
                    _clock.LocalTimeZone).DateTime);
        item.IsOverdue = dueDate < _clock.Today && !item.IsCompleted;
        item.IsDueToday = dueDate == _clock.Today && !item.IsCompleted;

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

        var accessibleParts = new List<string>(5) { task.Summary };
        if (task.Due is not null)
        {
            accessibleParts.Add(string.Format(
                CultureInfo.CurrentCulture,
                _resources.GetString("Task_Accessible_Due"),
                FormatDueDate(task.Due.Value)));
        }

        if (item.IsImportant)
        {
            accessibleParts.Add(_resources.GetString("Task_Accessible_Important"));
        }

        string? accessibleListName = FindListName(task.ListId);
        if (!string.IsNullOrEmpty(accessibleListName))
        {
            accessibleParts.Add(string.Format(
                CultureInfo.CurrentCulture,
                _resources.GetString("Task_Accessible_InList"),
                accessibleListName));
        }

        if (item.IsCompleted)
        {
            accessibleParts.Add(_resources.GetString("Task_Accessible_Completed"));
        }
        else
        {
            accessibleParts.Add(_resources.GetString("Task_Accessible_NotCompleted"));
        }

        item.AccessibleName = string.Join(", ", accessibleParts);
    }

    partial void OnIsCompactDensityChanged(bool value)
    {
        foreach (TaskItemViewModel item in _itemCache.Values)
        {
            item.IsCompactDensity = value;
        }
    }

    private static bool IsKnownBackgroundTheme(string? value)
    {
        return value is null
            or "Coast"
            or "Forest"
            or "Mist"
            or "Sunrise"
            or "Indigo"
            or "Petals"
            or "Graphite"
            or "Field";
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

    private void ResetComposer(bool collapse = true)
    {
        NewTaskTitle = string.Empty;
        _composerReminder = null;
        _composerRecurrence = null;
        _composerDue = _composerKind == ListQueryKind.Planned
            ? StartOfLocalDay(_clock.Today)
            : null;
        _composerImportant = _composerKind == ListQueryKind.Important;
        _composerMyDay = _composerKind == ListQueryKind.MyDay;
        if (collapse)
        {
            IsComposerExpanded = false;
        }

        UpdateComposerLabels();
        ResolveComposerTarget();
    }

    private void ResolveComposerTarget()
    {
        if (!IsComposerVisible)
        {
            IsComposerTargetVisible = false;
            CanCreateTask = false;
            return;
        }

        if (_composerKind == ListQueryKind.UserList)
        {
            ComposerTargetListId = _selectedList?.Id;
            ComposerTargetLabel = _selectedList?.DisplayName ?? string.Empty;
            IsComposerTargetVisible = false;
        }
        else
        {
            ShellListItemViewModel? target =
                _lists.FirstOrDefault(list => list.Id == _pinnedComposerListId)
                ?? _lists.FirstOrDefault();
            ComposerTargetListId = target?.Id;
            ComposerTargetLabel = target?.DisplayName ?? string.Empty;
            IsComposerTargetVisible = target is not null;
        }

        CanCreateTask = ComposerTargetListId is not null;
    }

    private void UpdateComposerLabels()
    {
        HasComposerDue = _composerDue is not null;
        HasComposerReminder = _composerReminder is not null;
        HasComposerRepeat = _composerRecurrence is not null;
        ComposerDueLabel = _composerDue is null
            ? _resources.GetString("Composer_DueDate")
            : FormatDueDate(_composerDue.Value);
        ComposerReminderLabel = _composerReminder is null
            ? _resources.GetString("Composer_RemindMe")
            : FormatReminder(_composerReminder.Value);
        ComposerRepeatLabel = _composerRecurrence switch
        {
            "FREQ=DAILY" => _resources.GetString("Composer_Daily"),
            "FREQ=WEEKLY" => _resources.GetString("Composer_Weekly"),
            _ => _resources.GetString("Composer_Repeat"),
        };
    }

    private string FormatReminder(DateTimeOffset reminder)
    {
        DateTime local =
            TimeZoneInfo.ConvertTime(reminder, _clock.LocalTimeZone).DateTime;
        string time = local.ToString("t", CultureInfo.CurrentCulture);
        return DateOnly.FromDateTime(local) == _clock.Today
            ? time
            : $"{FormatDueDate(reminder)} {time}";
    }

    private string FormatDueDate(DateTimeOffset due)
    {
        DateTime localDue =
            TimeZoneInfo.ConvertTime(due, _clock.LocalTimeZone).DateTime;
        DateOnly date = DateOnly.FromDateTime(localDue);
        int dayOffset = date.DayNumber - _clock.Today.DayNumber;
        return dayOffset switch
        {
            -1 => _resources.GetString("Task_Metadata_Yesterday"),
            0 => _resources.GetString("Task_Metadata_Today"),
            1 => _resources.GetString("Task_Metadata_Tomorrow"),
            >= 2 and <= 7 => localDue.ToString("dddd", CultureInfo.CurrentCulture),
            _ => localDue.ToString("d MMM", CultureInfo.CurrentCulture),
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
