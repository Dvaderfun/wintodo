using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.ApplicationModel.Resources;
using Wintodo.Core.Models;
using Wintodo.Core.Queries;
using Wintodo.Core.Services;
using Wintodo.Services;
using Wintodo.Sync;

namespace Wintodo.ViewModels;

public sealed partial class ShellViewModel : ObservableObject
{
    internal const string MyDayNavigationKey = "smart:my-day";
    internal const string ImportantNavigationKey = "smart:important";
    internal const string PlannedNavigationKey = "smart:planned";
    internal const string AllNavigationKey = "smart:all";
    internal const string CompletedNavigationKey = "smart:completed";
    private const string UserListNavigationPrefix = "list:";
    private const string AppearanceThemeSetting = "appearance.theme";
    private const string DensitySetting = "appearance.density";
    private const string ShowListBackgroundsSetting = "appearance.show_list_backgrounds";
    private const string TintFixedCategoryIconsSetting =
        "appearance.tint_fixed_category_icons";
    private const string FixedBackgroundSettingPrefix =
        "appearance.fixed_category_background.";
    private const string DetailPaneWidthSetting = "appearance.detail_pane_width";
    private const double MinimumDetailPaneWidth = 320;
    private const double MaximumDetailPaneWidth = 720;
    private const double DefaultDetailPaneWidth = 360;
    private readonly ITaskStore _taskStore;
    private readonly ShellStateService _stateService;
    private readonly IStartupTaskService _startupTaskService;
    private readonly ResourceLoader _resources = new();
    private readonly Dictionary<string, string?> _fixedCategoryBackgrounds =
        new(StringComparer.Ordinal);
    private string _selectedNavigationKey = MyDayNavigationKey;
    private string _selectedNavigationTitle = string.Empty;
    private bool _selectedNavigationIsMyDay = true;
    private bool _areAppearanceSettingsLoaded;
    private bool _areWindowsSettingsLoaded;
    private bool _isApplyingStartupTaskState;
    private TaskItemViewModel? _pendingRowDelete;
    private IReadOnlyList<TaskItemViewModel>? _pendingBulkDelete;
    private bool _isDetailDeletePending;
    private int _detailOpenVersion;
    private CancellationTokenSource? _detailPaneWidthDebounce;
    private int _navigationCountRefreshVersion;

    internal ShellViewModel(
        ITaskStore taskStore,
        IClock clock,
        ShellStateService stateService,
        IStartupTaskService startupTaskService,
        CalDavSyncService syncService,
        WindowsIntegrationService windowsIntegration)
    {
        _taskStore = taskStore;
        _stateService = stateService;
        _startupTaskService = startupTaskService;
        Notifications = windowsIntegration.NotificationPreferences;
        Notifications.SaveFailed += OnNotificationSettingsFailed;
        windowsIntegration.NotificationSettingsFailed += OnNotificationSettingsFailed;
        InitializeSync(syncService);
        Preferences = new TaskPreferences(taskStore);
        Preferences.SaveFailed += OnPreferencesSaveFailed;
        TaskList = new TaskListViewModel(taskStore, clock, Preferences);
        TaskList.NavigationDataChanged += OnNavigationDataChanged;
        TaskList.DeleteListRequested += OnDeleteListRequested;
        TaskList.ExportListRequested += OnExportListRequested;
        TaskList.ShortcutsRequested += OnShortcutsRequested;
        TaskList.DeleteTaskRequested += OnRowDeleteRequested;
        TaskList.DeleteTasksRequested += OnRowsDeleteRequested;
        TaskList.CustomListIconRequested += OnCustomListIconRequested;
        TaskList.BackgroundThemeChanged += OnBackgroundThemeChanged;
        Detail = new TaskDetailViewModel(taskStore, clock, Preferences);
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

    internal event Action? CustomListIconRequested;

    internal event Action<string>? DeleteTaskRequested;

    internal event Action? AppearanceSettingsChanged;

    internal event Action<string>? UserListVisited;

    public ObservableCollection<ShellListItemViewModel> UserLists { get; } = [];

    public ObservableCollection<ShellGroupViewModel> Groups { get; } = [];

    [ObservableProperty]
    public partial ShellListItemViewModel? DefaultList { get; set; }

    public TaskPreferences Preferences { get; }

    [ObservableProperty]
    public partial bool IsTaskPreferencesErrorOpen { get; set; }

    [ObservableProperty]
    public partial string TaskPreferencesErrorMessage { get; set; } = string.Empty;

    private void OnPreferencesSaveFailed(Exception exception)
    {
        Trace.WriteLine($"WINTODO_TASK_PREFERENCES_ERROR={exception}");
        TaskPreferencesErrorMessage = _resources.GetString("Settings_TasksSaveFailed");
        IsTaskPreferencesErrorOpen = true;
    }

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
    public partial bool IsListIconDialogLoaded { get; set; }

    [ObservableProperty]
    public partial string CustomListIconInput { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCustomListIconError))]
    public partial string CustomListIconError { get; set; } = string.Empty;

    public bool HasCustomListIconError => CustomListIconError.Length > 0;

    [ObservableProperty]
    public partial bool IsImportPreviewDialogLoaded { get; set; }

    [ObservableProperty]
    public partial bool IsImportErrorDialogLoaded { get; set; }

    [ObservableProperty]
    public partial bool IsShortcutsDialogLoaded { get; set; }

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
    public partial bool TintFixedCategoryIcons { get; set; } = true;

    [ObservableProperty]
    public partial int MyDayTaskCount { get; set; }

    [ObservableProperty]
    public partial int ImportantTaskCount { get; set; }

    [ObservableProperty]
    public partial int PlannedTaskCount { get; set; }

    [ObservableProperty]
    public partial int AllTaskCount { get; set; }

    [ObservableProperty]
    public partial int CompletedTaskCount { get; set; }

    [ObservableProperty]
    public partial Brush MyDayIconBrush { get; set; } =
        ResolveAppearanceBrush("TextFillColorPrimaryBrush");

    [ObservableProperty]
    public partial Brush ImportantIconBrush { get; set; } =
        ResolveAppearanceBrush("TextFillColorPrimaryBrush");

    [ObservableProperty]
    public partial Brush PlannedIconBrush { get; set; } =
        ResolveAppearanceBrush("TextFillColorPrimaryBrush");

    [ObservableProperty]
    public partial Brush AllIconBrush { get; set; } =
        ResolveAppearanceBrush("TextFillColorPrimaryBrush");

    [ObservableProperty]
    public partial Brush CompletedIconBrush { get; set; } =
        ResolveAppearanceBrush("TextFillColorPrimaryBrush");

    [ObservableProperty]
    public partial Brush TasksIconBrush { get; set; } =
        ResolveAppearanceBrush("TextFillColorPrimaryBrush");

    [ObservableProperty]
    public partial bool LaunchAtLogin { get; set; }

    [ObservableProperty]
    public partial bool CanChangeLaunchAtLogin { get; set; }

    [ObservableProperty]
    public partial double DetailPaneWidth { get; set; } = DefaultDetailPaneWidth;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateGroupCommand))]
    public partial string NewGroupName { get; set; } = string.Empty;

    internal async Task<string> InitializeAsync(CancellationToken cancellationToken = default)
    {
        await Preferences.LoadAsync(cancellationToken);
        await _taskStore.EnsureDefaultListAsync(
            _resources.GetString("DefaultList_Name"),
            cancellationToken);
        Task<IReadOnlyList<TodoList>> listsTask = _taskStore.GetListsAsync(cancellationToken);
        Task<IReadOnlyList<ListGroup>> groupsTask = _taskStore.GetGroupsAsync(cancellationToken);
        Task<IReadOnlyDictionary<ListQueryKind, int>> smartCountsTask =
            _taskStore.GetSmartListTaskCountsAsync(cancellationToken);
        Task<string?> themeTask = _taskStore.GetSettingAsync(
            AppearanceThemeSetting,
            cancellationToken);
        Task<string?> densityTask = _taskStore.GetSettingAsync(
            DensitySetting,
            cancellationToken);
        Task<string?> backgroundsTask = _taskStore.GetSettingAsync(
            ShowListBackgroundsSetting,
            cancellationToken);
        Task<string?> tintFixedIconsTask = _taskStore.GetSettingAsync(
            TintFixedCategoryIconsSetting,
            cancellationToken);
        Task<string?> myDayBackgroundTask = _taskStore.GetSettingAsync(
            GetFixedBackgroundSettingKey(MyDayNavigationKey),
            cancellationToken);
        Task<string?> importantBackgroundTask = _taskStore.GetSettingAsync(
            GetFixedBackgroundSettingKey(ImportantNavigationKey),
            cancellationToken);
        Task<string?> plannedBackgroundTask = _taskStore.GetSettingAsync(
            GetFixedBackgroundSettingKey(PlannedNavigationKey),
            cancellationToken);
        Task<string?> allBackgroundTask = _taskStore.GetSettingAsync(
            GetFixedBackgroundSettingKey(AllNavigationKey),
            cancellationToken);
        Task<string?> completedBackgroundTask = _taskStore.GetSettingAsync(
            GetFixedBackgroundSettingKey(CompletedNavigationKey),
            cancellationToken);
        Task<string?> detailWidthTask = _taskStore.GetSettingAsync(
            DetailPaneWidthSetting,
            cancellationToken);
        Task<string?> selectedNavigationTask =
            _stateService.LoadSelectedNavigationAsync(cancellationToken);
        Task<StartupTaskSettingState> startupTask = LoadStartupTaskStateAsync();
        await Task.WhenAll(
            listsTask,
            groupsTask,
            smartCountsTask,
            themeTask,
            densityTask,
            backgroundsTask,
            tintFixedIconsTask,
            myDayBackgroundTask,
            importantBackgroundTask,
            plannedBackgroundTask,
            allBackgroundTask,
            completedBackgroundTask,
            detailWidthTask,
            selectedNavigationTask);
        LoadNavigationData(await listsTask, await groupsTask);
        ApplySmartListTaskCounts(await smartCountsTask);
        _fixedCategoryBackgrounds[MyDayNavigationKey] =
            NormalizeBackgroundTheme(await myDayBackgroundTask);
        _fixedCategoryBackgrounds[ImportantNavigationKey] =
            NormalizeBackgroundTheme(await importantBackgroundTask);
        _fixedCategoryBackgrounds[PlannedNavigationKey] =
            NormalizeBackgroundTheme(await plannedBackgroundTask);
        _fixedCategoryBackgrounds[AllNavigationKey] =
            NormalizeBackgroundTheme(await allBackgroundTask);
        _fixedCategoryBackgrounds[CompletedNavigationKey] =
            NormalizeBackgroundTheme(await completedBackgroundTask);

        AppearanceThemeIndex = ParseChoice(await themeTask, maximum: 2);
        DensityIndex = ParseChoice(await densityTask, maximum: 1);
        ShowListBackgrounds = bool.TryParse(await backgroundsTask, out bool show)
            && show;
        TintFixedCategoryIcons =
            !bool.TryParse(await tintFixedIconsTask, out bool tintFixedIcons)
            || tintFixedIcons;
        DetailPaneWidth = ParseDetailPaneWidth(await detailWidthTask);
        TaskList.IsCompactDensity = DensityIndex == 1;
        _areAppearanceSettingsLoaded = true;
        AppearanceSettingsChanged?.Invoke();

        _ = ApplyStartupTaskStateWhenReadyAsync(startupTask);

        await InitializeSyncAsync(cancellationToken);

        return await selectedNavigationTask ?? MyDayNavigationKey;
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
        int version = ++_detailOpenVersion;
        await Detail.LoadAsync(task.Uid, cancellationToken);
        if (version != _detailOpenVersion || TaskList.SelectionCount != 1) return;
        IsDetailPaneLoaded = true;
        IsDetailPaneOpen = true;
    }

    internal void CloseDetail()
    {
        ++_detailOpenVersion;
        IsDetailPaneOpen = false;
    }

    internal async Task ConfirmDeleteListAsync(
        ShellListItemViewModel list,
        CancellationToken cancellationToken = default)
    {
        if (list.IsDefault)
        {
            return;
        }

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

    internal string? GetFixedCategoryBackgroundTheme(string navigationKey)
    {
        if (DefaultList?.NavigationKey == navigationKey)
        {
            return DefaultList.BackgroundTheme;
        }

        return _fixedCategoryBackgrounds.GetValueOrDefault(navigationKey);
    }

    private static string GetFixedBackgroundSettingKey(string navigationKey)
    {
        string suffix = navigationKey switch
        {
            MyDayNavigationKey => "my_day",
            ImportantNavigationKey => "important",
            PlannedNavigationKey => "planned",
            AllNavigationKey => "all",
            CompletedNavigationKey => "completed",
            _ => throw new ArgumentOutOfRangeException(
                nameof(navigationKey),
                navigationKey,
                "Only fixed smart lists have background setting keys."),
        };
        return FixedBackgroundSettingPrefix + suffix;
    }

    private static string? NormalizeBackgroundTheme(string? value)
    {
        return value is "Coast"
            or "Forest"
            or "Mist"
            or "Sunrise"
            or "Indigo"
            or "Petals"
            or "Graphite"
            or "Field"
            ? value
            : null;
    }

    private void UpdateFixedCategoryIconBrushes()
    {
        MyDayIconBrush = ResolveAppearanceBrush(
            ResolveFixedCategoryIconBrushKey(MyDayNavigationKey));
        ImportantIconBrush = ResolveAppearanceBrush(
            ResolveFixedCategoryIconBrushKey(ImportantNavigationKey));
        PlannedIconBrush = ResolveAppearanceBrush(
            ResolveFixedCategoryIconBrushKey(PlannedNavigationKey));
        AllIconBrush = ResolveAppearanceBrush(
            ResolveFixedCategoryIconBrushKey(AllNavigationKey));
        CompletedIconBrush = ResolveAppearanceBrush(
            ResolveFixedCategoryIconBrushKey(CompletedNavigationKey));
        TasksIconBrush = ResolveAppearanceBrush(
            ResolveFixedCategoryIconBrushKey(DefaultList?.NavigationKey));
        TaskList.FixedHeaderIconBrush = ResolveAppearanceBrush(
            ResolveFixedCategoryIconBrushKey(_selectedNavigationKey));
    }

    internal void RefreshFixedCategoryIconBrushes()
    {
        UpdateFixedCategoryIconBrushes();
        OnPropertyChanged(nameof(MyDayIconBrush));
        OnPropertyChanged(nameof(ImportantIconBrush));
        OnPropertyChanged(nameof(PlannedIconBrush));
        OnPropertyChanged(nameof(AllIconBrush));
        OnPropertyChanged(nameof(CompletedIconBrush));
        OnPropertyChanged(nameof(TasksIconBrush));
        TaskList.RefreshFixedHeaderIconBrush();
    }

    private string ResolveFixedCategoryIconBrushKey(string? navigationKey)
    {
        if (!TintFixedCategoryIcons || !IsFixedCategoryNavigationKey(navigationKey))
        {
            return "TextFillColorPrimaryBrush";
        }

        string? backgroundTheme =
            GetFixedCategoryBackgroundTheme(navigationKey!);
        if (backgroundTheme is not null)
        {
            return backgroundTheme switch
            {
                "Coast" => "ListAccentBlueBrush",
                "Forest" => "ListAccentGreenBrush",
                "Mist" => "ListAccentTealBrush",
                "Sunrise" => "ListAccentOrangeBrush",
                "Indigo" => "ListAccentPurpleBrush",
                "Petals" => "ListAccentPinkBrush",
                "Graphite" => "ListAccentGraphiteBrush",
                "Field" => "ListAccentLimeBrush",
                _ => "ListAccentBlueBrush",
            };
        }

        if (DefaultList is { } defaultList
            && defaultList.NavigationKey == navigationKey)
        {
            return defaultList.AccentResourceKey;
        }

        return navigationKey switch
        {
            MyDayNavigationKey => "ListAccentYellowBrush",
            ImportantNavigationKey => "ListAccentPinkBrush",
            PlannedNavigationKey => "ListAccentBlueBrush",
            AllNavigationKey => "ListAccentPurpleBrush",
            CompletedNavigationKey => "ListAccentGreenBrush",
            _ => "TextFillColorPrimaryBrush",
        };
    }

    private bool IsFixedCategoryNavigationKey(string? navigationKey)
    {
        return navigationKey is MyDayNavigationKey
            or ImportantNavigationKey
            or PlannedNavigationKey
            or AllNavigationKey
            or CompletedNavigationKey
            || DefaultList?.NavigationKey == navigationKey;
    }

    private static Brush ResolveAppearanceBrush(string resourceKey)
    {
        return (Brush)Application.Current.Resources[resourceKey];
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
            Trace.WriteLine($"WINTODO_NAVIGATION_COUNT_EXCEPTION={exception}");
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

    private void OnShortcutsRequested()
    {
        KeyboardShortcutsRequested?.Invoke();
    }

    private void OnCustomListIconRequested(string? iconEmoji)
    {
        CustomListIconInput = iconEmoji ?? string.Empty;
        CustomListIconError = string.Empty;
        IsListIconDialogLoaded = true;
        CustomListIconRequested?.Invoke();
    }

    private void OnBackgroundThemeChanged(string navigationKey, string? theme)
    {
        if (_fixedCategoryBackgrounds.ContainsKey(navigationKey))
        {
            _fixedCategoryBackgrounds[navigationKey] = theme;
        }

        UpdateFixedCategoryIconBrushes();
        AppearanceSettingsChanged?.Invoke();
    }

    internal bool ValidateCustomListIconInput()
    {
        if (!ListEmoji.TryNormalize(CustomListIconInput, out _))
        {
            CustomListIconError = _resources.GetString("ListIcon_Invalid");
            return false;
        }

        CustomListIconError = string.Empty;
        return true;
    }

    internal async Task CommitCustomListIconAsync()
    {
        if (!ListEmoji.TryNormalize(CustomListIconInput, out string? normalized))
        {
            return;
        }

        await TaskList.SetSelectedListIconAsync(normalized);
    }

    private void OnRowDeleteRequested(TaskItemViewModel item)
    {
        _pendingBulkDelete = null;
        _pendingRowDelete = item;
        _isDetailDeletePending = false;
        DeleteTaskRequested?.Invoke(item.Summary);
    }

    private void OnRowsDeleteRequested(IReadOnlyList<TaskItemViewModel> items)
    {
        if (items.Count == 1) { OnRowDeleteRequested(items[0]); return; }
        _pendingRowDelete = null;
        _isDetailDeletePending = false;
        _pendingBulkDelete = items.ToArray();
        DeleteTaskRequested?.Invoke(string.Empty);
    }

    internal int PendingDeleteCount => _pendingBulkDelete?.Count ?? 1;

    private void OnDetailDeleteRequested(string summary)
    {
        _pendingBulkDelete = null;
        _pendingRowDelete = null;
        _isDetailDeletePending = true;
        DeleteTaskRequested?.Invoke(summary);
    }

    internal async Task ConfirmDeleteTaskAsync(
        CancellationToken cancellationToken = default)
    {
        TaskItemViewModel? row = _pendingRowDelete;
        IReadOnlyList<TaskItemViewModel>? bulk = _pendingBulkDelete;
        bool isDetail = _isDetailDeletePending;
        CancelDeleteTask();
        if (bulk is not null)
        {
            await TaskList.ConfirmDeleteTasksAsync(bulk, cancellationToken);
            return;
        }
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
        _pendingBulkDelete = null;
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

    partial void OnTintFixedCategoryIconsChanged(bool value)
    {
        UpdateFixedCategoryIconBrushes();
        SaveAppearanceSetting(TintFixedCategoryIconsSetting, value.ToString());
    }

    partial void OnLaunchAtLoginChanged(bool value)
    {
        if (_areWindowsSettingsLoaded && !_isApplyingStartupTaskState)
        {
            _ = UpdateStartupTaskAsync(value);
        }
    }

    private async Task<StartupTaskSettingState> LoadStartupTaskStateAsync()
    {
        try
        {
            return await _startupTaskService.GetStateAsync();
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"WINTODO_STARTUP_TASK_READ_ERROR={exception}");
            return new StartupTaskSettingState(false, false);
        }
    }

    private async Task ApplyStartupTaskStateWhenReadyAsync(
        Task<StartupTaskSettingState> startupTask)
    {
        ApplyStartupTaskState(await startupTask);
        _areWindowsSettingsLoaded = true;
    }

    private async Task UpdateStartupTaskAsync(bool enabled)
    {
        try
        {
            StartupTaskSettingState state =
                await _startupTaskService.SetEnabledAsync(enabled);
            ApplyStartupTaskState(state);
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"WINTODO_STARTUP_TASK_WRITE_ERROR={exception}");
            ApplyStartupTaskState(await LoadStartupTaskStateAsync());
        }
    }

    private void ApplyStartupTaskState(StartupTaskSettingState state)
    {
        _isApplyingStartupTaskState = true;
        LaunchAtLogin = state.IsEnabled;
        CanChangeLaunchAtLogin = state.CanChange;
        _isApplyingStartupTaskState = false;
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
