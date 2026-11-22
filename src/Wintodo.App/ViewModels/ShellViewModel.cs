using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml;
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
        WindowsIntegrationService windowsIntegration,
        ImportEnvironment importEnvironment)
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
        Undo = new UndoService();
        TaskList = new TaskListViewModel(taskStore, clock, Preferences, Undo);
        TaskList.NavigationDataChanged += OnNavigationDataChanged;
        TaskList.DeleteListRequested += OnDeleteListRequested;
        TaskList.ExportListRequested += OnExportListRequested;
        TaskList.ShortcutsRequested += OnShortcutsRequested;
        TaskList.DeleteTaskRequested += OnRowDeleteRequested;
        TaskList.DeleteTasksRequested += OnRowsDeleteRequested;
        TaskList.CustomListIconRequested += OnCustomListIconRequested;
        TaskList.BackgroundThemeChanged += OnBackgroundThemeChanged;
        Detail = new TaskDetailViewModel(taskStore, clock, Preferences, Undo);
        TaskList.UndoCompleted += RefreshAfterUndoAsync;
        TaskList.UndoPreparing += PrepareUndoAsync;
        _taskStore.StoreChanged += OnUndoStoreChanged;
        Detail.CloseRequested += CloseDetail;
        Detail.TaskChanged += TaskList.ApplyDetailTaskUpdate;
        TaskList.MyDayMembershipChanged += Detail.RefreshMyDayAsync;
        Detail.TaskDeleted += TaskList.ApplyDetailTaskDeleted;
        Detail.DeleteRequested += OnDetailDeleteRequested;
        InitializeImport(taskStore, clock, importEnvironment);
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

    internal UndoService Undo { get; }

    [ObservableProperty]
    public partial bool IsTaskPreferencesErrorOpen { get; set; }

    [ObservableProperty]
    public partial string TaskPreferencesErrorMessage { get; set; } = string.Empty;

    private void OnPreferencesSaveFailed(Exception exception)
    {
        Trace.WriteLine($"WINTODO_TASK_PREFERENCES_ERROR={DiagnosticException.Describe(exception)}");
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

        await InitializeOnboardingAsync(cancellationToken);
        await InitializeSyncAsync(cancellationToken);

        return await selectedNavigationTask ?? MyDayNavigationKey;
    }
}
