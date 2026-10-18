using Wintodo.Core.Models;
using Wintodo.Core.Presentation;
using Wintodo.Core.Queries;
using Wintodo.Services;

namespace Wintodo.ViewModels;

public sealed partial class TaskListViewModel
{
    internal Task LoadSmartListAsync(
        ListQueryKind kind,
        string title,
        bool isMyDay,
        string navigationKey,
        string backgroundSettingKey,
        string? backgroundTheme,
        CancellationToken cancellationToken = default)
    {
        _selectedList = null;
        _selectedNavigationKey = navigationKey;
        _selectedBackgroundSettingKey = backgroundSettingKey;
        IsUserListSelected = false;
        IsTaskListVisible = true;
        IsComposerVisible = kind != ListQueryKind.Completed;
        AreListCommandsVisible = true;
        CanReorderTasks = false;
        CanChangeSelectedBackground = true;
        BackgroundTheme = backgroundTheme;
        HasFixedHeaderGlyph = kind != ListQueryKind.MyDay;
        Title = title;
        Subtitle = isMyDay ? FormatToday() : string.Empty;
        HasSubtitle = isMyDay;
        ConfigureEmptyState(kind);
        FixedHeaderGlyph = EmptyGlyph;
        _composerKind = kind;
        ResetComposer();

        SortMode sortMode = TaskSorting.GetDefaultMode(kind);
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
        _selectedNavigationKey = list.NavigationKey;
        _selectedBackgroundSettingKey = null;
        NotifySelectedListIconChanged();
        IsUserListSelected = true;
        CanEditSelectedList = !list.IsDefault;
        IsTaskListVisible = true;
        IsComposerVisible = true;
        AreListCommandsVisible = true;
        CanReorderTasks = list.SortMode == SortMode.Manual;
        CanChangeSelectedBackground = true;
        BackgroundTheme = list.BackgroundTheme;
        FixedHeaderGlyph = "\uE80F";
        HasFixedHeaderGlyph = list.IsDefault;
        Title = list.DisplayName;
        Subtitle = string.Empty;
        HasSubtitle = false;
        EmptyGlyph = "\uE710";
        EmptyTitle = _resources.GetString("Empty_UserListTitle");
        EmptySubtitle = _resources.GetString("Empty_UserListSubtitle");
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
        HasNonDefaultSort = false;
        _loadVersion++;
        _selectedList = null;
        _selectedNavigationKey = null;
        _selectedBackgroundSettingKey = null;
        _currentQuery = null;
        IsUserListSelected = false;
        IsTaskListVisible = false;
        IsComposerVisible = false;
        AreListCommandsVisible = false;
        CanReorderTasks = false;
        CanChangeSelectedBackground = false;
        BackgroundTheme = null;
        HasFixedHeaderGlyph = false;
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
        HasNonDefaultSort = false;
        _loadVersion++;
        _selectedList = null;
        _selectedNavigationKey = null;
        _selectedBackgroundSettingKey = null;
        _currentQuery = null;
        _searchText = text.Trim();
        IsUserListSelected = false;
        IsTaskListVisible = true;
        IsComposerVisible = false;
        AreListCommandsVisible = false;
        CanReorderTasks = false;
        CanChangeSelectedBackground = false;
        BackgroundTheme = null;
        HasFixedHeaderGlyph = false;
        ResetComposer();
        Title = _resources.GetString("Search_Title");
        Subtitle = string.Empty;
        HasSubtitle = false;
        EmptyGlyph = "\uE721";
        EmptyTitle = string.Format(
            DisplayCulture.Current,
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
        if (_selectedList?.Id == list.Id)
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
        var states = await _taskStore.CaptureTaskUndoAsync([taskUid], Core.Services.TaskUndoFields.List, cancellationToken);
        await _taskStore.MoveTaskAsync(taskUid, destinationListId, cancellationToken: cancellationToken);
        RecordTaskUndo(states, "Undo_TaskMoved");
        await ReloadAsync(cancellationToken);
    }

    private async Task LoadAsync(ListQuery query, CancellationToken cancellationToken, bool preserveViewport = false)
    {
        _currentQuery = query;
        UpdateSortIndicator(query);
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

        RebuildVisibleItems(preserveViewport);
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

    internal Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        return _currentQuery is null
            ? (string.IsNullOrEmpty(_searchText) ? Task.CompletedTask : LoadSearchAsync(_searchText, cancellationToken))
            : LoadAsync(_currentQuery, cancellationToken, preserveViewport: true);
    }

    private TaskItemViewModel GetOrCreateItem(TodoTask task)
    {
        if (_itemCache.TryGetValue(task.Uid, out TaskItemViewModel? existing))
        {
            // An activation refresh must not overwrite an inline title draft.
            if (existing.IsEditingTitle) return existing;
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

}
