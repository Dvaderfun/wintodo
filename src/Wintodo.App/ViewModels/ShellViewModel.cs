using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Windows.ApplicationModel.Resources;
using Wintodo.Core.Models;
using Wintodo.Core.Queries;
using Wintodo.Core.Services;
using Wintodo.Services;

namespace Wintodo.ViewModels;

public sealed partial class ShellViewModel : ObservableObject
{
    internal const string MyDayNavigationKey = "smart:my-day";
    private const string UserListNavigationPrefix = "list:";
    private readonly ITaskStore _taskStore;
    private readonly ShellStateService _stateService;
    private readonly ResourceLoader _resources = new();
    private string _selectedNavigationKey = MyDayNavigationKey;

    internal ShellViewModel(
        ITaskStore taskStore,
        IClock clock,
        ShellStateService stateService)
    {
        _taskStore = taskStore;
        _stateService = stateService;
        TaskList = new TaskListViewModel(taskStore, clock);
        TaskList.NavigationDataChanged += OnNavigationDataChanged;
        TaskList.DeleteListRequested += OnDeleteListRequested;
    }

    internal event Action? NavigationStructureChanged;

    internal event Action<string>? NavigationRequested;

    internal event Action<ShellListItemViewModel>? DeleteListRequested;

    public ObservableCollection<ShellListItemViewModel> UserLists { get; } = [];

    public ObservableCollection<ShellGroupViewModel> Groups { get; } = [];

    public TaskListViewModel TaskList { get; }

    [ObservableProperty]
    public partial bool IsCreatingList { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateListCommand))]
    public partial string NewListName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsCreatingGroup { get; set; }

    [ObservableProperty]
    public partial bool IsDeleteListDialogLoaded { get; set; }

    [ObservableProperty]
    public partial bool IsTaskListLoaded { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateGroupCommand))]
    public partial string NewGroupName { get; set; } = string.Empty;

    internal async Task<string> InitializeAsync(CancellationToken cancellationToken = default)
    {
        Task<IReadOnlyList<TodoList>> listsTask = _taskStore.GetListsAsync(cancellationToken);
        Task<IReadOnlyList<ListGroup>> groupsTask = _taskStore.GetGroupsAsync(cancellationToken);
        await Task.WhenAll(listsTask, groupsTask);
        LoadNavigationData(await listsTask, await groupsTask);

        return await _stateService.LoadSelectedNavigationAsync(cancellationToken)
            ?? MyDayNavigationKey;
    }

    internal async Task SelectAsync(
        string navigationKey,
        string title,
        bool isMyDay,
        CancellationToken cancellationToken = default)
    {
        _selectedNavigationKey = navigationKey;
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
                "smart:important" => ListQueryKind.Important,
                "smart:planned" => ListQueryKind.Planned,
                "smart:all" => ListQueryKind.All,
                "smart:completed" => ListQueryKind.Completed,
                _ => ListQueryKind.MyDay,
            };
            await TaskList.LoadSmartListAsync(kind, title, isMyDay, cancellationToken);
        }

        await _stateService.SaveSelectedNavigationAsync(navigationKey, cancellationToken);
    }

    internal void SelectSettings()
    {
        _selectedNavigationKey = "settings";
        TaskList.ShowSettings(_resources.GetString("Shell_SettingsTitle"));
    }

    internal async Task ConfirmDeleteListAsync(
        ShellListItemViewModel list,
        CancellationToken cancellationToken = default)
    {
        await TaskList.DeleteListAsync(list, cancellationToken);
        UserLists.Remove(list);
        RebuildGroups();
        TaskList.SetLists(UserLists);
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

        Groups.Clear();
        foreach (ListGroup group in groups)
        {
            Groups.Add(new ShellGroupViewModel(group));
        }

        RebuildGroups();
        TaskList.SetLists(UserLists);
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

    private void OnNavigationDataChanged()
    {
        RebuildGroups();
        NavigationStructureChanged?.Invoke();
    }

    private void OnDeleteListRequested(ShellListItemViewModel list)
    {
        DeleteListRequested?.Invoke(list);
    }
}
