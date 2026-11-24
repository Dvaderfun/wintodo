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
    internal const double MaximumDetailPaneWidth = 960;
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
    private double _queuedDetailPaneWidth = DefaultDetailPaneWidth;
    private Task _detailPaneWidthWrite = Task.CompletedTask;
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
        TaskList.PrepareSuggestionsAsync = PrepareSuggestionsAsync;
        Detail.TaskChanged += TaskList.ApplyDetailTaskUpdate;
        Detail.MyDayMembershipChanged += TaskList.ApplyDetailMyDayUpdateAsync;
        TaskList.TaskDatesChanged += Detail.RefreshTaskDatesAsync;
        TaskList.PrepareRowCompletionAsync = uid => uid == Detail.Uid
            ? Detail.FlushPendingEditsAsync() : Task.FromResult(true);
        TaskList.RowCompletionSaved += Detail.ApplyRowCompletion;
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
        Task preferences = Preferences.LoadAsync(cancellationToken);
        Task<IReadOnlyDictionary<string, string>> appearance = _taskStore.GetSettingsAsync(
            [AppearanceThemeSetting, DensitySetting, ShowListBackgroundsSetting, TintFixedCategoryIconsSetting,
             GetFixedBackgroundSettingKey(MyDayNavigationKey), GetFixedBackgroundSettingKey(ImportantNavigationKey),
             GetFixedBackgroundSettingKey(PlannedNavigationKey), GetFixedBackgroundSettingKey(AllNavigationKey),
             GetFixedBackgroundSettingKey(CompletedNavigationKey), DetailPaneWidthSetting,
             ShellStateService.SelectedNavigationKey, OnboardingCompletedSetting], cancellationToken);
        await _taskStore.EnsureDefaultListAsync(_resources.GetString("DefaultList_Name"), cancellationToken);
        Task<IReadOnlyList<TodoList>> listsTask = _taskStore.GetListsAsync(cancellationToken);
        Task<IReadOnlyList<ListGroup>> groupsTask = _taskStore.GetGroupsAsync(cancellationToken);
        Task<IReadOnlyDictionary<ListQueryKind, int>> smartCountsTask =
            _taskStore.GetSmartListTaskCountsAsync(cancellationToken);
        await Task.WhenAll(preferences, appearance, listsTask, groupsTask, smartCountsTask);
        var settings = await appearance;
        LoadNavigationData(await listsTask, await groupsTask);
        ApplySmartListTaskCounts(await smartCountsTask);
        LoadSyncListOptions(await listsTask);

        foreach (string key in new[] { MyDayNavigationKey, ImportantNavigationKey, PlannedNavigationKey,
            AllNavigationKey, CompletedNavigationKey })
        {
            _fixedCategoryBackgrounds[key] = NormalizeBackgroundTheme(settings.GetValueOrDefault(GetFixedBackgroundSettingKey(key)));
        }
        AppearanceThemeIndex = ParseChoice(settings.GetValueOrDefault(AppearanceThemeSetting), maximum: 2);
        DensityIndex = ParseChoice(settings.GetValueOrDefault(DensitySetting), maximum: 1);
        ShowListBackgrounds = bool.TryParse(settings.GetValueOrDefault(ShowListBackgroundsSetting), out bool show) && show;
        TintFixedCategoryIcons = !bool.TryParse(settings.GetValueOrDefault(TintFixedCategoryIconsSetting), out bool tint) || tint;
        _queuedDetailPaneWidth = DetailPaneWidth = ParseDetailPaneWidth(settings.GetValueOrDefault(DetailPaneWidthSetting));
        TaskList.IsCompactDensity = DensityIndex == 1;
        _areAppearanceSettingsLoaded = true;
        AppearanceSettingsChanged?.Invoke();

        _ = ApplyStartupTaskStateWhenReadyAsync(LoadStartupTaskStateAsync());

        if (!settings.ContainsKey(OnboardingCompletedSetting)) await InitializeOnboardingAsync(cancellationToken);

        return settings.GetValueOrDefault(ShellStateService.SelectedNavigationKey) ?? MyDayNavigationKey;
    }
}
