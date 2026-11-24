using System.Diagnostics;
using CommunityToolkit.Mvvm.Input;
using Wintodo.Core.Models;
using Wintodo.Core.Queries;
using Wintodo.Services;
using Wintodo.Core.Services;

namespace Wintodo.ViewModels;

public sealed partial class ShellViewModel
{
    internal async Task SelectAsync(
        string navigationKey,
        string title,
        bool isMyDay,
        CancellationToken cancellationToken = default)
    {
        CloseDetail();
        IsSettingsVisible = false;
        _selectedNavigationKey = navigationKey;
        _selectedNavigationTitle = title;
        _selectedNavigationIsMyDay = isMyDay;
        await LoadSelectedTaskListAsync(cancellationToken);
        await _stateService.SaveSelectedNavigationAsync(navigationKey, cancellationToken);
        if (navigationKey.StartsWith(UserListNavigationPrefix, StringComparison.Ordinal))
        {
            UserListVisited?.Invoke(navigationKey[UserListNavigationPrefix.Length..]);
        }
    }

    internal async Task SearchAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        CloseDetail();
        if (!string.IsNullOrWhiteSpace(text))
        {
            LeaveImportSurfaces();
            IsSettingsVisible = false;
            await TaskList.LoadSearchAsync(text, cancellationToken);
            return;
        }

        if (_selectedNavigationKey == "settings")
        {
            IsSettingsVisible = true;
            TaskList.ShowSettings(_resources.GetString("Shell_SettingsTitle"));
            return;
        }

        await LoadSelectedTaskListAsync(cancellationToken);
    }

    private async Task LoadSelectedTaskListAsync(
        CancellationToken cancellationToken)
    {
        string navigationKey = _selectedNavigationKey;
        if (navigationKey.StartsWith(UserListNavigationPrefix, StringComparison.Ordinal))
        {
            string uid = navigationKey[UserListNavigationPrefix.Length..];
            ShellListItemViewModel? list = FindList(uid);
            if (list is not null)
            {
                await TaskList.LoadUserListAsync(list, cancellationToken);
            }
        }
        else
        {
            ListQueryKind kind = navigationKey switch
            {
                ImportantNavigationKey => ListQueryKind.Important,
                PlannedNavigationKey => ListQueryKind.Planned,
                AllNavigationKey => ListQueryKind.All,
                CompletedNavigationKey => ListQueryKind.Completed,
                _ => ListQueryKind.MyDay,
            };
            await TaskList.LoadSmartListAsync(
                kind,
                _selectedNavigationTitle,
                _selectedNavigationIsMyDay,
                navigationKey,
                GetFixedBackgroundSettingKey(navigationKey),
                GetFixedCategoryBackgroundTheme(navigationKey),
                cancellationToken);
        }

        UpdateFixedCategoryIconBrushes();
    }

    internal void SelectSettings()
    {
        CloseDetail();
        IsSettingsVisible = true;
        _selectedNavigationKey = "settings";
        _selectedNavigationTitle = _resources.GetString("Shell_SettingsTitle");
        _selectedNavigationIsMyDay = false;
        TaskList.ShowSettings(_resources.GetString("Shell_SettingsTitle"));
        _ = RefreshDatabaseStatsAsync();
    }

    internal async Task RefreshNavigationAsync(CancellationToken cancellationToken = default)
    {
        Task<IReadOnlyList<TodoList>> listsTask = _taskStore.GetListsAsync(cancellationToken);
        Task<IReadOnlyList<ListGroup>> groupsTask = _taskStore.GetGroupsAsync(cancellationToken);
        Task<IReadOnlyDictionary<ListQueryKind, int>> smartCountsTask =
            _taskStore.GetSmartListTaskCountsAsync(cancellationToken);
        await Task.WhenAll(listsTask, groupsTask, smartCountsTask);
        LoadNavigationData(await listsTask, await groupsTask);
        ApplySmartListTaskCounts(await smartCountsTask);
        await RefreshSyncOptionsAsync(cancellationToken);
        NavigationStructureChanged?.Invoke();
    }

    internal async Task ConfirmDeleteListAsync(
        ShellListItemViewModel list,
        CancellationToken cancellationToken = default)
    {
        if (list.IsDefault)
        {
            return;
        }

        IReadOnlyList<TodoTask> tasks = await _taskStore.GetTasksForExportAsync(list.Id, cancellationToken);
        try
        {
            await TaskList.DeleteListAsync(list, cancellationToken);
        }
        catch (PendingTaskMoveException)
        {
            ShowPendingTaskMoveGuidance();
            return;
        }
        Undo.Record(string.Format(DisplayCulture.Current, _resources.GetString("Undo_ListDeleted"), list.DisplayName),
            tasks.Select(task => task.Uid), token => _taskStore.RestoreListAsync(list.Id, token));
        ShellListItemViewModel? current = UserLists.FirstOrDefault(candidate => candidate.Id == list.Id);
        if (current is not null) UserLists.Remove(current);
        RebuildGroups();
        TaskList.SetLists(UserLists);
        TaskList.RefreshOpenSuggestions();
        NavigationStructureChanged?.Invoke();
        if (_selectedNavigationKey == list.NavigationKey)
        {
            NavigationRequested?.Invoke(MyDayNavigationKey);
        }
    }

    internal async Task MoveListToGroupAsync(
        string listUid,
        long? groupId,
        CancellationToken cancellationToken = default)
    {
        ShellListItemViewModel? list = FindList(listUid);
        if (list is null || list.GroupId == groupId)
        {
            return;
        }

        await _taskStore.MoveListToGroupAsync(list.Id, groupId, cancellationToken);
        list.GroupId = groupId;
        RebuildGroups();
        NavigationStructureChanged?.Invoke();
    }

    [RelayCommand]
    private Task RemoveListFromGroupAsync(ShellListItemViewModel list)
    {
        return MoveListToGroupAsync(list.Uid, groupId: null);
    }

    internal async Task ReorderListAsync(
        string listUid,
        string targetUid,
        CancellationToken cancellationToken = default)
    {
        ShellListItemViewModel? dragged = FindList(listUid);
        ShellListItemViewModel? target = FindList(targetUid);
        if (dragged is null || target is null || ReferenceEquals(dragged, target))
        {
            return;
        }

        var ordered = new List<ShellListItemViewModel>(UserLists);
        ordered.Remove(dragged);
        int targetIndex = ordered.IndexOf(target);
        ordered.Insert(targetIndex, dragged);

        if (dragged.GroupId != target.GroupId)
        {
            await _taskStore.MoveListToGroupAsync(
                dragged.Id,
                target.GroupId,
                cancellationToken);
            dragged.GroupId = target.GroupId;
        }

        for (int index = 0; index < ordered.Count; index++)
        {
            ShellListItemViewModel list = ordered[index];
            double sortOrder = (index + 1) * 1024d;
            list.SortOrder = sortOrder;
            await _taskStore.ReorderListAsync(list.Id, sortOrder, cancellationToken);
        }

        UserLists.Clear();
        foreach (ShellListItemViewModel list in ordered)
        {
            UserLists.Add(list);
        }

        RebuildGroups();
        TaskList.SetLists(UserLists);
        NavigationStructureChanged?.Invoke();
    }

    [RelayCommand]
    private void BeginCreateList()
    {
        IsCreatingGroup = false;
        NewGroupName = string.Empty;
        IsCreatingList = true;
    }

    [RelayCommand]
    private void CancelCreateList()
    {
        NewListName = string.Empty;
        IsCreatingList = false;
    }

    private bool CanCreateList()
    {
        return !string.IsNullOrWhiteSpace(NewListName);
    }

    [RelayCommand(CanExecute = nameof(CanCreateList))]
    private async Task CreateListAsync()
    {
        await Task.Yield();
        TodoList created = await _taskStore.CreateListAsync(NewListName.Trim());
        var list = new ShellListItemViewModel(created);
        UserLists.Add(list);
        NewListName = string.Empty;
        IsCreatingList = false;
        RebuildGroups();
        TaskList.SetLists(UserLists);
        NavigationStructureChanged?.Invoke();
        NavigationRequested?.Invoke(list.NavigationKey);
    }

    [RelayCommand]
    private void BeginCreateGroup()
    {
        IsCreatingList = false;
        NewListName = string.Empty;
        IsCreatingGroup = true;
    }

    [RelayCommand]
    private void CancelCreateGroup()
    {
        NewGroupName = string.Empty;
        IsCreatingGroup = false;
    }

    private bool CanCreateGroup()
    {
        return !string.IsNullOrWhiteSpace(NewGroupName);
    }

    [RelayCommand(CanExecute = nameof(CanCreateGroup))]
    private async Task CreateGroupAsync()
    {
        await Task.Yield();
        ListGroup created = await _taskStore.CreateGroupAsync(NewGroupName.Trim());
        Groups.Add(new ShellGroupViewModel(created));
        NewGroupName = string.Empty;
        IsCreatingGroup = false;
        NavigationStructureChanged?.Invoke();
    }

    [RelayCommand]
    private async Task SelectListAndBeginRenameAsync(ShellListItemViewModel list)
    {
        if (list.IsDefault)
        {
            return;
        }

        await Task.Yield();
        await SelectAsync(
            list.NavigationKey,
            list.DisplayName,
            isMyDay: false);
        NavigationRequested?.Invoke(list.NavigationKey);
        TaskList.BeginRenameListCommand.Execute(null);
    }

    [RelayCommand]
    private void RequestDeleteList(ShellListItemViewModel list)
    {
        if (list.IsDefault)
        {
            return;
        }

        DeleteListRequested?.Invoke(list);
    }

    private void LoadNavigationData(
        IReadOnlyList<TodoList> lists,
        IReadOnlyList<ListGroup> groups)
    {
        UserLists.Clear();
        foreach (TodoList list in lists)
        {
            UserLists.Add(new ShellListItemViewModel(list));
        }

        DefaultList = UserLists.FirstOrDefault(list => list.IsDefault);
        Groups.Clear();
        foreach (ListGroup group in groups)
        {
            Groups.Add(new ShellGroupViewModel(group));
        }

        RebuildGroups();
        TaskList.SetLists(UserLists);
        UpdateFixedCategoryIconBrushes();
    }

    private void ApplySmartListTaskCounts(
        IReadOnlyDictionary<ListQueryKind, int> counts)
    {
        MyDayTaskCount = counts.GetValueOrDefault(ListQueryKind.MyDay);
        ImportantTaskCount = counts.GetValueOrDefault(ListQueryKind.Important);
        PlannedTaskCount = counts.GetValueOrDefault(ListQueryKind.Planned);
        AllTaskCount = counts.GetValueOrDefault(ListQueryKind.All);
        CompletedTaskCount = counts.GetValueOrDefault(ListQueryKind.Completed);
    }

    private void RebuildGroups()
    {
        foreach (ShellGroupViewModel group in Groups)
        {
            group.Lists.Clear();
        }

        foreach (ShellListItemViewModel list in UserLists)
        {
            if (list.GroupId is null)
            {
                continue;
            }

            ShellGroupViewModel? group = Groups.FirstOrDefault(
                candidate => candidate.Id == list.GroupId.Value);
            group?.Lists.Add(list);
        }
    }

    private ShellListItemViewModel? FindList(string uid)
    {
        return UserLists.FirstOrDefault(
            list => string.Equals(list.Uid, uid, StringComparison.Ordinal));
    }

    private async void OnNavigationDataChanged()
    {
        await RefreshNavigationCountsAsync();
    }

    internal async Task RefreshNavigationCountsAsync(
        CancellationToken cancellationToken = default)
    {
        int refreshVersion = ++_navigationCountRefreshVersion;
        try
        {
            Task<IReadOnlyDictionary<long, int>> listCountsTask =
                _taskStore.GetIncompleteTaskCountsAsync(cancellationToken);
            Task<IReadOnlyDictionary<ListQueryKind, int>> smartCountsTask =
                _taskStore.GetSmartListTaskCountsAsync(cancellationToken);
            await Task.WhenAll(listCountsTask, smartCountsTask);
            if (refreshVersion != _navigationCountRefreshVersion)
            {
                return;
            }

            IReadOnlyDictionary<long, int> listCounts = await listCountsTask;
            foreach (ShellListItemViewModel list in UserLists)
            {
                list.IncompleteTaskCount = listCounts.GetValueOrDefault(list.Id);
            }

            ApplySmartListTaskCounts(await smartCountsTask);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"WINTODO_NAVIGATION_COUNT_EXCEPTION={DiagnosticException.Describe(exception)}");
        }

        if (refreshVersion != _navigationCountRefreshVersion)
        {
            return;
        }

        UpdateFixedCategoryIconBrushes();
        RebuildGroups();
        NavigationStructureChanged?.Invoke();
    }

    private void OnDeleteListRequested(ShellListItemViewModel list)
    {
        DeleteListRequested?.Invoke(list);
    }

    private void OnExportListRequested(ShellListItemViewModel list)
    {
        ExportListRequested?.Invoke(list);
    }

    internal ShellListItemViewModel? GetQuickAddFallback() =>
        DefaultList ?? UserLists.OrderBy(list => list.SortOrder).FirstOrDefault();

}
