using System.ComponentModel;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml;
using Microsoft.Windows.ApplicationModel.Resources;
using Windows.UI.ViewManagement;
using Wintodo.Controls;
using Wintodo.Core.Models;
using Wintodo.Core.Services;
using Wintodo.Services;
using Wintodo.Sync;
using Wintodo.ViewModels;

namespace Wintodo.Pages;

public sealed partial class ShellPage : Page
{
    private const string UserListNavigationPrefix = "list:";
    private const string ListDragFormat = "application/x-wintodo-list-uid";
    private const long MaximumImportFileBytes = 32 * 1024 * 1024;
    private const long MaximumImportBatchBytes = 128 * 1024 * 1024;
    private const uint SpiGetHighContrast = 0x0042;
    private const uint HighContrastOn = 0x0001;
    private readonly ResourceLoader _resources = new();
    private readonly UISettings _uiSettings = new();
    private readonly Dictionary<string, NavigationViewItem> _navigationItems =
        new(StringComparer.Ordinal);
    private readonly IClock _clock;
    private readonly ITaskStore _taskStore;
    private readonly WindowsIntegrationService _windowsIntegration;
    private CancellationTokenSource? _transferStatusCancellation;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _midnightTimer;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _syncTimer;
    private DateTimeOffset _lastSyncAttempt = DateTimeOffset.MinValue;
    private bool _isSyncConflictDialogOpen;
    private bool _isNavigationInitialized;
    private bool _isRebuildingNavigation;
    private bool _isHighContrast;
    private bool _isShortcutsDialogOpen;
    private bool _taskListEventsAttached;
    private ShellListItemViewModel? _selectedList;
    private int _backgroundDecodeWidth;

    internal ShellPage(
        ITaskStore taskStore,
        IClock clock,
        ShellStateService stateService,
        WindowsIntegrationService windowsIntegration,
        IStartupTaskService startupTaskService,
        CalDavSyncService syncService,
        ImportEnvironment importEnvironment)
    {
        _clock = clock;
        _taskStore = taskStore;
        _windowsIntegration = windowsIntegration;
        _isHighContrast = IsSystemHighContrastEnabled();
        ViewModel = new ShellViewModel(
            taskStore,
            clock,
            stateService,
            startupTaskService,
            syncService,
            windowsIntegration,
            importEnvironment);
        KeyboardShortcuts = CreateKeyboardShortcuts();
        InitializeComponent();
        AddPunctuationAccelerators();
        AddHandler(KeyDownEvent, new KeyEventHandler(OnShellKeyDown), handledEventsToo: true);

        ViewModel.NavigationStructureChanged += OnNavigationStructureChanged;
        ViewModel.NavigationRequested += OnNavigationRequested;
        ViewModel.DeleteListRequested += OnDeleteListRequested;
        ViewModel.DeleteTaskRequested += OnDeleteTaskRequested;
        ViewModel.ImportIcalRequested += OnImportIcalRequested;
        ViewModel.ExportAllRequested += OnExportAllRequested;
        ViewModel.ExportListRequested += OnExportListRequested;
        ViewModel.KeyboardShortcutsRequested += OnKeyboardShortcutsRequested;
        ViewModel.AppearanceSettingsChanged += OnAppearanceSettingsChanged;
        ViewModel.UserListVisited += OnUserListVisited;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.RestoreDetailSelectionRequested += task => TaskList.SelectTask(task);
        ViewModel.SyncConflictsRequested += OnSyncConflictsRequested;
        ViewModel.CustomListIconRequested += OnCustomListIconRequested;
        ShellRoot.ActualThemeChanged += OnActualThemeChanged;

        DefaultListNavigationItem.AllowDrop = true;
        DefaultListNavigationItem.DragOver += OnNavigationDragOver;
        DefaultListNavigationItem.Drop += OnListDrop;

        foreach (NavigationViewItem item in new[] { MyDayNavigationItem, ImportantNavigationItem })
        {
            item.AllowDrop = true;
            item.DragOver += OnSmartListDragOver;
            item.DragLeave += OnSmartListDragLeave;
            item.Drop += OnSmartListDrop;
        }

    }

    public ShellViewModel ViewModel { get; }

    public IList<object> KeyboardShortcuts { get; }

    internal event Action? SearchFocusRequested;

    internal event Func<bool>? SearchClearRequested;

    // The app theme has to be applied to the window content root, not this page:
    // the title bar and its search box are siblings of the frame that hosts the
    // page, so a page-level RequestedTheme would leave them on the system theme.
    internal event Action<ElementTheme>? ThemeChanged;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ShellViewModel.IsSyncConnected) && _backgroundSyncStarted)
        {
            // Connect performs its own initial sync. Only arm the recurring timer here.
            if (ViewModel.IsSyncConnected) _lastSyncAttempt = _clock.UtcNow;
            UpdateBackgroundSyncTimer();
            TraceTimerState("account");
        }
        else if (args.PropertyName == nameof(ShellViewModel.IsCreatingList)
            && ViewModel.IsCreatingList)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                NewListNameBox.Focus(FocusState.Programmatic);
                NewListNameBox.SelectAll();
            });
        }
        else if (args.PropertyName == nameof(ShellViewModel.IsCreatingGroup)
                 && ViewModel.IsCreatingGroup)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                NewGroupNameBox.Focus(FocusState.Programmatic);
                NewGroupNameBox.SelectAll();
            });
        }
        else if (args.PropertyName is nameof(ShellViewModel.IsCreatingList)
                 or nameof(ShellViewModel.IsCreatingGroup))
        {
            bool wasCreatingList = args.PropertyName == nameof(ShellViewModel.IsCreatingList);
            DispatcherQueue.TryEnqueue(() =>
            {
                PinMenuItemsToPaneTop();
                ReturnFocusToPaneFooter(wasCreatingList ? NewListButton : NewGroupButton);
            });
        }
        else if (args.PropertyName == nameof(ShellViewModel.IsDetailPaneOpen))
        {
            if (ViewModel.IsDetailPaneOpen)
            {
                VisualStateManager.GoToState(this, "DetailOpen", useTransitions: false);
            }
            else
            {
                if (ViewModel.IsDetailPaneLoaded) TaskDetail?.DismissTaskPickers();
                if (_taskListEventsAttached && ViewModel.TaskList.SelectionCount <= 1)
                {
                    TaskList.ClearTaskSelection();
                }

                _ = CloseDetailVisualAsync();
            }
        }
        else if (args.PropertyName == nameof(ShellViewModel.IsDetailEditorReady) && !ViewModel.IsDetailEditorReady)
        {
            TaskDetail?.DismissTaskPickers();
        }
    }

    private async void OnTaskOpenRequested(TaskItemViewModel task, bool explicitlyActivated)
    {
        await ViewModel.OpenDetailAsync(task, refresh: explicitlyActivated && ViewModel.IsEditingSelectedTask(task.Uid));
    }

    private void OnTaskCloseRequested()
    {
        ViewModel.CloseDetail();
    }

    private void OnTaskListLoaded(object sender, RoutedEventArgs args)
    {
        if (_taskListEventsAttached)
        {
            return;
        }

        TaskList.TaskOpenRequested += OnTaskOpenRequested;
        TaskList.TaskCloseRequested += OnTaskCloseRequested;
        _taskListEventsAttached = true;
    }

    private void OnTaskDetailLoaded(object sender, RoutedEventArgs args)
    {
        ViewModel.DetailInstanceCount = TaskDetailView.InstanceCount;
    }

    private async Task CloseDetailVisualAsync()
    {
        VisualStateManager.GoToState(this, "DetailClosing", useTransitions: true);
        if (new UISettings().AnimationsEnabled)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(150));
        }

        if (!ViewModel.IsDetailPaneOpen)
        {
            VisualStateManager.GoToState(this, "DetailClosed", useTransitions: false);
        }
    }
}
