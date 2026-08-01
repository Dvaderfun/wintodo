using System.Collections.ObjectModel;
using System.Diagnostics;
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
    private const string AppearanceThemeSetting = "appearance.theme";
    private const string DensitySetting = "appearance.density";
    private const string ShowListBackgroundsSetting = "appearance.show_list_backgrounds";
    private const string DetailPaneWidthSetting = "appearance.detail_pane_width";
    private const double MinimumDetailPaneWidth = 320;
    private const double MaximumDetailPaneWidth = 720;
    private const double DefaultDetailPaneWidth = 360;
    private readonly ITaskStore _taskStore;
    private readonly ShellStateService _stateService;
    private readonly ResourceLoader _resources = new();
    private string _selectedNavigationKey = MyDayNavigationKey;
    private string _selectedNavigationTitle = string.Empty;
    private bool _selectedNavigationIsMyDay = true;
    private bool _areAppearanceSettingsLoaded;
    private TaskItemViewModel? _pendingRowDelete;
    private bool _isDetailDeletePending;
    private CancellationTokenSource? _detailPaneWidthDebounce;

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
        TaskList.ExportListRequested += OnExportListRequested;
        TaskList.ShortcutsRequested += OnShortcutsRequested;
        TaskList.DeleteTaskRequested += OnRowDeleteRequested;
        Detail = new TaskDetailViewModel(taskStore, clock);
        Detail.CloseRequested += CloseDetail;
        Detail.TaskChanged += TaskList.ApplyDetailTaskUpdate;
        Detail.TaskDeleted += TaskList.ApplyDetailTaskDeleted;
        Detail.DeleteRequested += OnDetailDeleteRequested;
    }

    internal event Action? NavigationStructureChanged;

    internal event Action<string>? NavigationRequested;

    internal event Action<ShellListItemViewModel>? DeleteListRequested;

    internal event Action? ImportIcalRequested;

    internal event Action? ExportAllRequested;

    internal event Action<ShellListItemViewModel>? ExportListRequested;

    internal event Action? KeyboardShortcutsRequested;

    internal event Action<string>? DeleteTaskRequested;

    internal event Action? AppearanceSettingsChanged;

    public ObservableCollection<ShellListItemViewModel> UserLists { get; } = [];

    public ObservableCollection<ShellGroupViewModel> Groups { get; } = [];

    public TaskListViewModel TaskList { get; }

    public TaskDetailViewModel Detail { get; }

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
    public partial bool IsDeleteTaskDialogLoaded { get; set; }

    [ObservableProperty]
    public partial bool IsImportPreviewDialogLoaded { get; set; }

    [ObservableProperty]
    public partial bool IsImportErrorDialogLoaded { get; set; }

    [ObservableProperty]
    public partial bool IsTaskListLoaded { get; set; }

    [ObservableProperty]
    public partial bool IsDetailPaneLoaded { get; set; }

    [ObservableProperty]
    public partial bool IsDetailPaneOpen { get; set; }

    [ObservableProperty]
    public partial bool IsSettingsVisible { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportIcalCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportAllCommand))]
    public partial bool IsTransferBusy { get; set; }

    [ObservableProperty]
    public partial bool IsTransferStatusOpen { get; set; }

    [ObservableProperty]
    public partial string TransferStatusTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TransferStatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool TransferStatusIsError { get; set; }

    [ObservableProperty]
    public partial int DetailInstanceCount { get; set; }

    [ObservableProperty]
    public partial int AppearanceThemeIndex { get; set; }

    [ObservableProperty]
    public partial int DensityIndex { get; set; }

    [ObservableProperty]
    public partial bool ShowListBackgrounds { get; set; }

    [ObservableProperty]
    public partial double DetailPaneWidth { get; set; } = DefaultDetailPaneWidth;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateGroupCommand))]
    public partial string NewGroupName { get; set; } = string.Empty;

    internal async Task<string> InitializeAsync(CancellationToken cancellationToken = default)
    {
        Task<IReadOnlyList<TodoList>> listsTask = _taskStore.GetListsAsync(cancellationToken);
        Task<IReadOnlyList<ListGroup>> groupsTask = _taskStore.GetGroupsAsync(cancellationToken);
        Task<string?> themeTask = _taskStore.GetSettingAsync(
            AppearanceThemeSetting,
            cancellationToken);
        Task<string?> densityTask = _taskStore.GetSettingAsync(
            DensitySetting,
            cancellationToken);
        Task<string?> backgroundsTask = _taskStore.GetSettingAsync(
            ShowListBackgroundsSetting,
            cancellationToken);
        Task<string?> detailWidthTask = _taskStore.GetSettingAsync(
            DetailPaneWidthSetting,
            cancellationToken);
        await Task.WhenAll(
            listsTask,
            groupsTask,
            themeTask,
            densityTask,
            backgroundsTask,
            detailWidthTask);
        IReadOnlyList<TodoList> lists = await listsTask;
        if (lists.Count == 0)
        {
            lists = [await CreateDefaultListAsync(cancellationToken)];
        }

        LoadNavigationData(lists, await groupsTask);

        AppearanceThemeIndex = ParseChoice(await themeTask, maximum: 2);
        DensityIndex = ParseChoice(await densityTask, maximum: 1);
        ShowListBackgrounds = bool.TryParse(await backgroundsTask, out bool show)
            && show;
        DetailPaneWidth = ParseDetailPaneWidth(await detailWidthTask);
        TaskList.IsCompactDensity = DensityIndex == 1;
        _areAppearanceSettingsLoaded = true;
        AppearanceSettingsChanged?.Invoke();

        return await _stateService.LoadSelectedNavigationAsync(cancellationToken)
            ?? MyDayNavigationKey;
    }

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
    }

    internal async Task SearchAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        CloseDetail();
        if (!string.IsNullOrWhiteSpace(text))
        {
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
                "smart:important" => ListQueryKind.Important,
                "smart:planned" => ListQueryKind.Planned,
                "smart:all" => ListQueryKind.All,
                "smart:completed" => ListQueryKind.Completed,
                _ => ListQueryKind.MyDay,
            };
            await TaskList.LoadSmartListAsync(
                kind,
                _selectedNavigationTitle,
                _selectedNavigationIsMyDay,
                cancellationToken);
        }
    }

    internal void SelectSettings()
    {
        CloseDetail();
        IsSettingsVisible = true;
        _selectedNavigationKey = "settings";
        _selectedNavigationTitle = _resources.GetString("Shell_SettingsTitle");
        _selectedNavigationIsMyDay = false;
        TaskList.ShowSettings(_resources.GetString("Shell_SettingsTitle"));
    }

    internal async Task RefreshNavigationAsync(CancellationToken cancellationToken = default)
    {
        Task<IReadOnlyList<TodoList>> listsTask = _taskStore.GetListsAsync(cancellationToken);
        Task<IReadOnlyList<ListGroup>> groupsTask = _taskStore.GetGroupsAsync(cancellationToken);
        await Task.WhenAll(listsTask, groupsTask);
        LoadNavigationData(await listsTask, await groupsTask);
        NavigationStructureChanged?.Invoke();
    }

    internal void SetTransferBusy(bool value)
    {
        if (value)
        {
            IsTransferStatusOpen = false;
        }

        IsTransferBusy = value;
        TaskList.IsTransferBusy = value;
    }

    internal void ShowTransferStatus(
        string title,
        string message,
        bool isError)
    {
        TransferStatusTitle = title;
        TransferStatusMessage = message;
        TransferStatusIsError = isError;
        IsTransferStatusOpen = true;
    }

    internal async Task OpenDetailAsync(
        TaskItemViewModel task,
        CancellationToken cancellationToken = default)
    {
        await Detail.LoadAsync(task.Uid, cancellationToken);
        IsDetailPaneLoaded = true;
        IsDetailPaneOpen = true;
    }

    internal void CloseDetail()
    {
        IsDetailPaneOpen = false;
    }

    internal async Task ConfirmDeleteListAsync(
        ShellListItemViewModel list,
        CancellationToken cancellationToken = default)
    {
        await TaskList.DeleteListAsync(list, cancellationToken);
        UserLists.Remove(list);
        if (UserLists.Count == 0)
        {
            UserLists.Add(new ShellListItemViewModel(
                await CreateDefaultListAsync(cancellationToken)));
        }

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

    private bool CanTransferData()
    {
        return !IsTransferBusy;
    }

    [RelayCommand(CanExecute = nameof(CanTransferData))]
    private void ImportIcal()
    {
        ImportIcalRequested?.Invoke();
    }

    [RelayCommand(CanExecute = nameof(CanTransferData))]
    private void ExportAll()
    {
        ExportAllRequested?.Invoke();
    }

    private Task<TodoList> CreateDefaultListAsync(CancellationToken cancellationToken)
    {
        return _taskStore.CreateListAsync(
            _resources.GetString("DefaultList_Name"),
            cancellationToken: cancellationToken);
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

    private void OnExportListRequested(ShellListItemViewModel list)
    {
        ExportListRequested?.Invoke(list);
    }

    private void OnShortcutsRequested()
    {
        KeyboardShortcutsRequested?.Invoke();
    }

    private void OnRowDeleteRequested(TaskItemViewModel item)
    {
        _pendingRowDelete = item;
        _isDetailDeletePending = false;
        DeleteTaskRequested?.Invoke(item.Summary);
    }

    private void OnDetailDeleteRequested(string summary)
    {
        _pendingRowDelete = null;
        _isDetailDeletePending = true;
        DeleteTaskRequested?.Invoke(summary);
    }

    internal async Task ConfirmDeleteTaskAsync(
        CancellationToken cancellationToken = default)
    {
        TaskItemViewModel? row = _pendingRowDelete;
        bool isDetail = _isDetailDeletePending;
        CancelDeleteTask();
        if (row is not null)
        {
            await TaskList.ConfirmDeleteTaskAsync(row, cancellationToken);
            return;
        }

        if (isDetail)
        {
            await Detail.ConfirmDeleteAsync(cancellationToken);
        }
    }

    internal void CancelDeleteTask()
    {
        _pendingRowDelete = null;
        _isDetailDeletePending = false;
    }

    partial void OnAppearanceThemeIndexChanged(int value)
    {
        SaveAppearanceSetting(AppearanceThemeSetting, value.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
    }

    partial void OnDensityIndexChanged(int value)
    {
        TaskList.IsCompactDensity = value == 1;
        SaveAppearanceSetting(DensitySetting, value.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
    }

    partial void OnShowListBackgroundsChanged(bool value)
    {
        SaveAppearanceSetting(ShowListBackgroundsSetting, value.ToString());
    }

    partial void OnDetailPaneWidthChanged(double value)
    {
        if (!_areAppearanceSettingsLoaded)
        {
            return;
        }

        _detailPaneWidthDebounce?.Cancel();
        _detailPaneWidthDebounce?.Dispose();
        _detailPaneWidthDebounce = new CancellationTokenSource();
        _ = PersistDetailPaneWidthAsync(value, _detailPaneWidthDebounce.Token);
    }

    private async Task PersistDetailPaneWidthAsync(
        double value,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
            await _taskStore.SetSettingAsync(
                DetailPaneWidthSetting,
                value.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"WINTODO_DETAIL_WIDTH_SETTING_ERROR={exception}");
        }
    }

    private static double ParseDetailPaneWidth(string? value)
    {
        return double.TryParse(
                value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double width)
            ? Math.Clamp(width, MinimumDetailPaneWidth, MaximumDetailPaneWidth)
            : DefaultDetailPaneWidth;
    }

    private void SaveAppearanceSetting(string key, string value)
    {
        AppearanceSettingsChanged?.Invoke();
        if (_areAppearanceSettingsLoaded)
        {
            _ = PersistAppearanceSettingAsync(key, value);
        }
    }

    private async Task PersistAppearanceSettingAsync(string key, string value)
    {
        try
        {
            await _taskStore.SetSettingAsync(key, value);
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"WINTODO_APPEARANCE_SETTING_ERROR={exception}");
        }
    }

    private static int ParseChoice(string? value, int maximum)
    {
        return int.TryParse(
                value,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out int choice)
            ? Math.Clamp(choice, 0, maximum)
            : 0;
    }
}
