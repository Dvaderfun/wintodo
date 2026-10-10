using System.ComponentModel;
using System.Globalization;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.Windows.ApplicationModel.Resources;
using Microsoft.Windows.Storage.Pickers;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;
using Windows.UI.ViewManagement;
using Wintodo.Controls;
using Wintodo.Core.Ical;
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
        CalDavSyncService syncService)
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
            syncService);
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
        ViewModel.SyncConflictsRequested += OnSyncConflictsRequested;
        ViewModel.CustomListIconRequested += OnCustomListIconRequested;
        ShellRoot.ActualThemeChanged += OnActualThemeChanged;

        DefaultListNavigationItem.AllowDrop = true;
        DefaultListNavigationItem.DragOver += OnNavigationDragOver;
        DefaultListNavigationItem.Drop += OnListDrop;

    }

    public ShellViewModel ViewModel { get; }

    public IList<object> KeyboardShortcuts { get; }

    internal event Action? SearchFocusRequested;

    internal event Func<bool>? SearchClearRequested;

    // The app theme has to be applied to the window content root, not this page:
    // the title bar and its search box are siblings of the frame that hosts the
    // page, so a page-level RequestedTheme would leave them on the system theme.
    internal event Action<ElementTheme>? ThemeChanged;

    internal async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        string selectedNavigation = await ViewModel.InitializeAsync(cancellationToken);
        PopulateUserListNavigationItems();
        object selectedItem = ResolveNavigationItem(selectedNavigation);
        ShellNavigation.SelectedItem = selectedItem;
        await ApplySelectionAsync(selectedItem, isSettingsSelected: false, cancellationToken);
        _isNavigationInitialized = true;
        _uiSettings.ColorValuesChanged += OnColorValuesChanged;
        ScheduleMidnightRefresh();
        ScheduleBackgroundSync();
        _ = RunBackgroundSyncAsync();
    }

    internal Task SearchAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        return ViewModel.SearchAsync(text, cancellationToken);
    }

    internal async Task RefreshForActivationAsync(
        CancellationToken cancellationToken = default)
    {
        await ViewModel.TaskList.RefreshForDateChangeAsync(cancellationToken);
        await _windowsIntegration.RefreshAsync(cancellationToken);
        if (_clock.UtcNow - _lastSyncAttempt >= TimeSpan.FromMinutes(1))
        {
            await RunBackgroundSyncAsync(cancellationToken);
        }
        ScheduleMidnightRefresh();
    }

    internal Task RefreshForExternalChangeAsync(
        CancellationToken cancellationToken = default)
    {
        return ViewModel.TaskList.ReloadAsync(cancellationToken);
    }

    internal async Task HandleJumpListArgumentAsync(
        string argument,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(
            argument,
            WindowsIntegrationService.JumpListNewTaskArgument,
            StringComparison.Ordinal))
        {
            if (!ViewModel.TaskList.CanCreateTask)
            {
                ShellListItemViewModel? fallback = ViewModel.DefaultList
                    ?? ViewModel.UserLists
                        .OrderBy(list => list.SortOrder)
                        .FirstOrDefault();
                if (fallback is not null)
                {
                    await SelectNavigationAsync(
                        ResolveNavigationItem(fallback.NavigationKey),
                        cancellationToken);
                }
            }

            TaskList.FocusComposer();
            return;
        }

        if (!argument.StartsWith(
            WindowsIntegrationService.JumpListOpenListPrefix,
            StringComparison.Ordinal))
        {
            return;
        }

        string uid = argument[WindowsIntegrationService.JumpListOpenListPrefix.Length..];
        ShellListItemViewModel? list = ViewModel.UserLists.FirstOrDefault(
            candidate => string.Equals(candidate.Uid, uid, StringComparison.Ordinal));
        if (list is not null)
        {
            await SelectNavigationAsync(
                ResolveNavigationItem(list.NavigationKey),
                cancellationToken);
        }
    }

    internal async Task OpenTaskAsync(
        string uid,
        CancellationToken cancellationToken = default)
    {
        TodoTask? task = await _taskStore.GetTaskAsync(uid, cancellationToken);
        if (task is null)
        {
            return;
        }

        ShellListItemViewModel? list = ViewModel.UserLists.FirstOrDefault(
            candidate => candidate.Id == task.ListId);
        if (list is null)
        {
            return;
        }

        await SelectNavigationAsync(
            ResolveNavigationItem(list.NavigationKey),
            cancellationToken);
        TaskItemViewModel? item = ViewModel.TaskList.FindTask(uid);
        if (item is not null)
        {
            await ViewModel.OpenDetailAsync(item, cancellationToken);
            TaskList.SelectTask(item);
        }
    }

    private async Task SelectNavigationAsync(
        object selectedItem,
        CancellationToken cancellationToken)
    {
        bool wasInitialized = _isNavigationInitialized;
        _isNavigationInitialized = false;
        ShellNavigation.SelectedItem = selectedItem;
        await ApplySelectionAsync(
            selectedItem,
            isSettingsSelected: false,
            cancellationToken);
        _isNavigationInitialized = wasInitialized;
    }

    private async void OnUserListVisited(string uid)
    {
        try
        {
            await _windowsIntegration.RecordRecentListAsync(uid);
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"WINTODO_RECENT_LIST_EXCEPTION={exception}");
        }
    }

    internal void StopMidnightRefresh()
    {
        ViewModel.UserListVisited -= OnUserListVisited;
        ViewModel.SyncConflictsRequested -= OnSyncConflictsRequested;
        ViewModel.CustomListIconRequested -= OnCustomListIconRequested;
        ViewModel.DisposeSync();
        if (_syncTimer is not null)
        {
            _syncTimer.Stop();
            _syncTimer.Tick -= OnSyncTimerTick;
            _syncTimer = null;
        }

        if (_midnightTimer is null)
        {
            return;
        }

        _midnightTimer.Stop();
        _midnightTimer.Tick -= OnMidnightTimerTick;
        _midnightTimer = null;
    }

    public static Visibility BoolToVisibility(bool value)
    {
        return value ? Visibility.Visible : Visibility.Collapsed;
    }

    public static Visibility CountToVisibility(int count)
    {
        return count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public static Visibility NewItemActionsVisibility(bool isCreatingList, bool isCreatingGroup)
    {
        return isCreatingList || isCreatingGroup ? Visibility.Collapsed : Visibility.Visible;
    }

    public static ElementTheme ThemeForIndex(int index)
    {
        return index switch
        {
            1 => ElementTheme.Light,
            2 => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
    }

    public InfoBarSeverity ToInfoBarSeverity(bool isError)
    {
        return isError ? InfoBarSeverity.Error : InfoBarSeverity.Success;
    }

    private async void OnNavigationSelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (!_isNavigationInitialized || _isRebuildingNavigation)
        {
            return;
        }

        await ApplySelectionAsync(args.SelectedItem, args.IsSettingsSelected);
    }

    private async Task ApplySelectionAsync(
        object? selectedItem,
        bool isSettingsSelected,
        CancellationToken cancellationToken = default)
    {
        if (isSettingsSelected
            || selectedItem is NavigationViewItem { Tag: "settings" })
        {
            ViewModel.SelectSettings();
            _selectedList = null;
            ApplyListAccent(list: null);
            ApplyListBackground();
            return;
        }

        if (selectedItem is not NavigationViewItem navigationItem
            || navigationItem.Tag is not string navigationKey)
        {
            return;
        }

        await ViewModel.SelectAsync(
            navigationKey,
            navigationItem.Content?.ToString() ?? string.Empty,
            ReferenceEquals(navigationItem, MyDayNavigationItem),
            cancellationToken);
        _selectedList = navigationKey.StartsWith(
                UserListNavigationPrefix,
                StringComparison.Ordinal)
            ? ViewModel.UserLists.FirstOrDefault(
                list => list.NavigationKey == navigationKey)
            : null;
        ApplyListAccent(_selectedList);
        ApplyListBackground();
    }

    // NavigationView caps its menu ScrollViewer with a MaxHeight computed from the
    // pane footer. Showing the inline name box grows the footer and shrinks that cap,
    // and the cap survives after the box unloads — a stretched element clamped by
    // MaxHeight centers itself, so the whole menu drifts down by half the difference.
    private void PinMenuItemsToPaneTop()
    {
        if (FindTemplateChild(ShellNavigation, "MenuItemsScrollViewer") is not FrameworkElement menuItems)
        {
            return;
        }

        menuItems.VerticalAlignment = VerticalAlignment.Top;
        menuItems.ClearValue(FrameworkElement.MaxHeightProperty);
    }

    private static FrameworkElement? FindTemplateChild(DependencyObject root, string name)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < count; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is FrameworkElement element
                && string.Equals(element.Name, name, StringComparison.Ordinal))
            {
                return element;
            }

            if (FindTemplateChild(child, name) is FrameworkElement match)
            {
                return match;
            }
        }

        return null;
    }

    private void PopulateUserListNavigationItems()
    {
        _navigationItems.Clear();
        ConfigureDefaultListNavigationItem();

        int firstListIndex = ShellNavigation.MenuItems.IndexOf(ListsSeparator) + 1;
        while (ShellNavigation.MenuItems.Count > firstListIndex)
        {
            ShellNavigation.MenuItems.RemoveAt(ShellNavigation.MenuItems.Count - 1);
        }

        foreach (ShellListItemViewModel list in ViewModel.UserLists
                     .Where(item => item.GroupId is null && !item.IsDefault)
                     .OrderBy(item => item.SortOrder))
        {
            ShellNavigation.MenuItems.Add(CreateListNavigationItem(list));
        }

        foreach (ShellGroupViewModel group in ViewModel.Groups.OrderBy(item => item.SortOrder))
        {
            var groupItem = new NavigationViewItem
            {
                Content = group.Name,
                SelectsOnInvoked = false,
                IsExpanded = group.IsExpanded,
                Tag = group,
                AllowDrop = true,
            };
            groupItem.Icon = new SymbolIcon(Symbol.Folder);
            AutomationProperties.SetAutomationId(groupItem, group.AutomationId);
            AutomationProperties.SetName(groupItem, group.Name);
            groupItem.DragOver += OnNavigationDragOver;
            groupItem.Drop += OnGroupDrop;

            foreach (ShellListItemViewModel list in group.Lists.OrderBy(item => item.SortOrder))
            {
                groupItem.MenuItems.Add(CreateListNavigationItem(list));
            }

            ShellNavigation.MenuItems.Add(groupItem);
        }

        UpdateFixedCategoryNavigationAccessibility();
    }

    private void ConfigureDefaultListNavigationItem()
    {
        ShellListItemViewModel? list = ViewModel.DefaultList;
        if (list is null)
        {
            DefaultListNavigationItem.Visibility = Visibility.Collapsed;
            return;
        }

        DefaultListNavigationItem.Visibility = Visibility.Visible;
        DefaultListNavigationItem.Content = BuildDefaultListContent(list);
        DefaultListNavigationItem.Tag = list.NavigationKey;
        AutomationProperties.SetName(
            DefaultListNavigationItem,
            GetListAccessibleName(list));
        _navigationItems[list.NavigationKey] = DefaultListNavigationItem;
    }

    private NavigationViewItem CreateListNavigationItem(ShellListItemViewModel list)
    {
        var navigationItem = new NavigationViewItem
        {
            Content = BuildListContent(list),
            Tag = list.NavigationKey,
            CanDrag = true,
            AllowDrop = true,
            ContextFlyout = CreateListContextFlyout(list),
        };
        AutomationProperties.SetAutomationId(navigationItem, list.AutomationId);
        AutomationProperties.SetName(navigationItem, GetListAccessibleName(list));
        navigationItem.DragStarting += OnListDragStarting;
        navigationItem.DragOver += OnNavigationDragOver;
        navigationItem.Drop += OnListDrop;
        _navigationItems[list.NavigationKey] = navigationItem;
        return navigationItem;
    }

    private Grid BuildListContent(ShellListItemViewModel list)
    {
        FrameworkElement icon;
        if (list.IconEmoji is { Length: > 0 } iconEmoji)
        {
            icon = new TextBlock
            {
                Text = iconEmoji,
                FontFamily = new FontFamily("Segoe UI Emoji"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
            };
        }
        else
        {
            icon = new Ellipse
            {
                Width = 16,
                Height = 16,
                Fill = ResolveBrush(list.AccentResourceKey),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        AutomationProperties.SetAutomationId(icon, $"Todo.Nav.Icon.{list.Uid}");
        var label = new TextBlock
        {
            Text = list.DisplayName,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var content = new Grid
        {
            ColumnSpacing = 12,
        };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(label, 1);
        InfoBadge countBadge = BuildTaskCountBadge(list);
        Grid.SetColumn(countBadge, 2);
        content.Children.Add(icon);
        content.Children.Add(label);
        content.Children.Add(countBadge);
        return content;
    }

    private Grid BuildDefaultListContent(ShellListItemViewModel list)
    {
        var label = new TextBlock
        {
            Text = list.DisplayName,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var content = new Grid
        {
            ColumnSpacing = 12,
        };
        content.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        InfoBadge countBadge = BuildTaskCountBadge(list);
        Grid.SetColumn(countBadge, 1);
        content.Children.Add(label);
        content.Children.Add(countBadge);
        return content;
    }

    private InfoBadge BuildTaskCountBadge(ShellListItemViewModel list)
    {
        var countBadge = new InfoBadge
        {
            Value = list.IncompleteTaskCount,
            Style = (Style)Application.Current.Resources["NavigationTaskCountBadgeStyle"],
            Visibility = list.IncompleteTaskCount == 0
                ? Visibility.Collapsed
                : Visibility.Visible,
        };
        AutomationProperties.SetAutomationId(
            countBadge,
            list.IsDefault
                ? "Todo.Nav.DefaultList.Count"
                : $"Todo.Nav.Count.{list.Uid}");
        AutomationProperties.SetAccessibilityView(countBadge, AccessibilityView.Content);
        AutomationProperties.SetName(
            countBadge,
            list.IncompleteTaskCount.ToString(DisplayCulture.Current));
        return countBadge;
    }

    private string GetListAccessibleName(ShellListItemViewModel list)
    {
        return list.IncompleteTaskCount == 0
            ? list.DisplayName
            : string.Format(
                DisplayCulture.Current,
                _resources.GetString("List_AccessibleNameWithCount"),
                list.DisplayName,
                list.IncompleteTaskCount);
    }

    private MenuFlyout CreateListContextFlyout(ShellListItemViewModel list)
    {
        var flyout = new MenuFlyout();
        var rename = new MenuFlyoutItem
        {
            Text = _resources.GetString("List_Rename"),
            Icon = new FontIcon { Glyph = "\uE70F" },
            Command = ViewModel.SelectListAndBeginRenameCommand,
            CommandParameter = list,
        };
        AutomationProperties.SetAutomationId(rename, $"Todo.Nav.Rename.{list.Uid}");
        flyout.Items.Add(rename);
        if (list.GroupId is not null)
        {
            var ungroup = new MenuFlyoutItem
            {
                Text = _resources.GetString("List_RemoveFromGroup"),
                Icon = new FontIcon { Glyph = "" },
                Command = ViewModel.RemoveListFromGroupCommand,
                CommandParameter = list,
            };
            AutomationProperties.SetAutomationId(ungroup, $"Todo.Nav.Ungroup.{list.Uid}");
            flyout.Items.Add(ungroup);
        }

        var delete = new MenuFlyoutItem
        {
            Text = _resources.GetString("List_Delete"),
            Icon = new FontIcon { Glyph = "\uE74D" },
            Command = ViewModel.RequestDeleteListCommand,
            CommandParameter = list,
        };
        AutomationProperties.SetAutomationId(delete, $"Todo.Nav.Delete.{list.Uid}");
        flyout.Items.Add(delete);
        return flyout;
    }

    private void OnListDragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if (sender is NavigationViewItem
            {
                Tag: string navigationKey,
            }
            && navigationKey.StartsWith(UserListNavigationPrefix, StringComparison.Ordinal))
        {
            args.Data.SetData(ListDragFormat, navigationKey[UserListNavigationPrefix.Length..]);
            args.Data.RequestedOperation = DataPackageOperation.Move;
        }
    }

    private void OnNavigationDragOver(object sender, DragEventArgs args)
    {
        if (args.DataView.Contains(ListDragFormat)
            || args.DataView.Contains(TaskListView.TaskDragFormat))
        {
            args.AcceptedOperation = DataPackageOperation.Move;
        }
    }

    private async void OnListDrop(object sender, DragEventArgs args)
    {
        if (sender is not NavigationViewItem { Tag: string targetKey }
            || !targetKey.StartsWith(UserListNavigationPrefix, StringComparison.Ordinal))
        {
            return;
        }

        string targetUid = targetKey[UserListNavigationPrefix.Length..];
        if (args.DataView.Contains(ListDragFormat))
        {
            if (ReferenceEquals(sender, DefaultListNavigationItem))
            {
                return;
            }

            object value = await args.DataView.GetDataAsync(ListDragFormat);
            if (value is string draggedUid)
            {
                await ViewModel.ReorderListAsync(draggedUid, targetUid);
            }
        }
        else if (args.DataView.Contains(TaskListView.TaskDragFormat))
        {
            object value = await args.DataView.GetDataAsync(TaskListView.TaskDragFormat);
            ShellListItemViewModel? target = ViewModel.UserLists.FirstOrDefault(
                list => list.Uid == targetUid);
            if (value is string taskUid && target is not null)
            {
                await ViewModel.TaskList.MoveTaskToListAsync(taskUid, target.Id);
            }
        }
    }

    private async void OnGroupDrop(object sender, DragEventArgs args)
    {
        if (sender is not NavigationViewItem { Tag: ShellGroupViewModel group }
            || !args.DataView.Contains(ListDragFormat))
        {
            return;
        }

        object value = await args.DataView.GetDataAsync(ListDragFormat);
        if (value is string listUid)
        {
            await ViewModel.MoveListToGroupAsync(listUid, group.Id);
        }
    }

    private void OnNavigationStructureChanged()
    {
        string? selectedKey = (ShellNavigation.SelectedItem as NavigationViewItem)?.Tag as string;
        _isRebuildingNavigation = true;
        try
        {
            PopulateUserListNavigationItems();
            if (selectedKey is not null)
            {
                ShellNavigation.SelectedItem = ResolveNavigationItem(selectedKey);
                _selectedList = ViewModel.UserLists.FirstOrDefault(
                    list => list.NavigationKey == selectedKey);
                ApplyListAccent(_selectedList);
                ApplyListBackground();
            }
        }
        finally
        {
            _isRebuildingNavigation = false;
        }
    }

    private void OnNavigationRequested(string navigationKey)
    {
        ShellNavigation.SelectedItem = ResolveNavigationItem(navigationKey);
    }

    private async void OnDeleteListRequested(ShellListItemViewModel list)
    {
        ViewModel.IsDeleteListDialogLoaded = true;
        await Task.Yield();
        DeleteListDialog.XamlRoot = XamlRoot;
        DeleteListMessage.Text = string.Format(
            DisplayCulture.Current,
            _resources.GetString("DeleteListDialog_Message"),
            list.DisplayName);
        ContentDialogResult result = await DeleteListDialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            await ViewModel.ConfirmDeleteListAsync(list);
        }
    }

    private async void OnDeleteTaskRequested(string summary)
    {
        ViewModel.IsDeleteTaskDialogLoaded = true;
        await Task.Yield();
        DeleteTaskDialog.XamlRoot = XamlRoot;
        DeleteTaskMessage.Text = string.Format(
            DisplayCulture.Current,
            _resources.GetString("DeleteTaskDialog_Message"),
            summary);
        ContentDialogResult result = await DeleteTaskDialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            await ViewModel.ConfirmDeleteTaskAsync();
        }
        else
        {
            ViewModel.CancelDeleteTask();
        }
    }

    private void OnDeleteListDialogOpened(
        ContentDialog sender,
        ContentDialogOpenedEventArgs args)
    {
        SetOpenDialogButtonAutomationId(
            sender,
            "PrimaryButton",
            "Todo.Dialog.DeleteList.Confirm");
        SetOpenDialogButtonAutomationId(
            sender,
            "CloseButton",
            "Todo.Dialog.DeleteList.Cancel");
    }

    private void OnDeleteTaskDialogOpened(
        ContentDialog sender,
        ContentDialogOpenedEventArgs args)
    {
        SetOpenDialogButtonAutomationId(
            sender,
            "PrimaryButton",
            "Todo.Dialog.DeleteTask.Confirm");
        SetOpenDialogButtonAutomationId(
            sender,
            "CloseButton",
            "Todo.Dialog.DeleteTask.Cancel");
    }

    private async void OnCustomListIconRequested()
    {
        await Task.Yield();
        ListIconDialog.XamlRoot = XamlRoot;
        ContentDialogResult result = await ListIconDialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            await ViewModel.CommitCustomListIconAsync();
        }
    }

    private void OnListIconDialogOpened(
        ContentDialog sender,
        ContentDialogOpenedEventArgs args)
    {
        SetOpenDialogButtonAutomationId(
            sender,
            "PrimaryButton",
            "Todo.Dialog.ListIcon.Save");
        SetOpenDialogButtonAutomationId(
            sender,
            "CloseButton",
            "Todo.Dialog.ListIcon.Cancel");
        ListIconInput.Focus(FocusState.Programmatic);
        ListIconInput.SelectAll();
    }

    private void OnListIconDialogClosing(
        ContentDialog sender,
        ContentDialogClosingEventArgs args)
    {
        if (args.Result == ContentDialogResult.Primary
            && !ViewModel.ValidateCustomListIconInput())
        {
            args.Cancel = true;
            ListIconInput.Focus(FocusState.Programmatic);
            ListIconInput.SelectAll();
        }
    }

    private async void OnImportIcalRequested()
    {
        ViewModel.SetTransferBusy(true);
        try
        {
            var picker = new FileOpenPicker(App.WindowId)
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            };
            picker.FileTypeFilter.Add(".ics");
            IReadOnlyList<PickFileResult> files = await picker.PickMultipleFilesAsync();
            if (files.Count == 0)
            {
                return;
            }

            var calendars = new List<IcalCalendar>(files.Count);
            var reader = new IcalReader(_clock.LocalTimeZone);
            long batchBytes = 0;
            foreach (PickFileResult file in files)
            {
                var fileInfo = new FileInfo(file.Path);
                if (fileInfo.Length > MaximumImportFileBytes)
                {
                    await ShowImportErrorAsync(_resources.GetString("Import_FileTooLarge"));
                    return;
                }

                if (fileInfo.Length > MaximumImportBatchBytes - batchBytes)
                {
                    await ShowImportErrorAsync(_resources.GetString("Import_BatchTooLarge"));
                    return;
                }

                batchBytes += fileInfo.Length;

                byte[] content = await File.ReadAllBytesAsync(file.Path);
                IcalCalendar calendar = reader.Read(content);
                calendar.Name = string.IsNullOrWhiteSpace(calendar.Name)
                    ? System.IO.Path.GetFileNameWithoutExtension(file.Path)
                    : calendar.Name.Trim();
                calendars.Add(calendar);
            }

            IcalImportPreview preview = await _taskStore.PreviewIcalImportAsync(calendars);
            ViewModel.IsImportPreviewDialogLoaded = true;
            await Task.Yield();
            ImportPreviewDialog.XamlRoot = XamlRoot;
            ImportPreviewDialog.IsPrimaryButtonEnabled = preview.TaskCount > 0;
            ImportPreviewMessage.Text = string.Format(
                DisplayCulture.Current,
                _resources.GetString("Import_PreviewMessage"),
                preview.TaskCount,
                preview.ListCount,
                preview.ConflictCount);
            ContentDialogResult decision = await ImportPreviewDialog.ShowAsync();
            if (decision != ContentDialogResult.Primary)
            {
                return;
            }

            try
            {
                IcalImportResult result = await _taskStore.ImportIcalAsync(calendars);
                await ViewModel.RefreshNavigationAsync();
                ShowTransferStatus(
                    _resources.GetString("Transfer_StatusTitleSuccess"),
                    string.Format(
                        DisplayCulture.Current,
                        _resources.GetString("Import_SuccessMessage"),
                        result.CreatedTaskCount,
                        result.UpdatedTaskCount),
                    InfoBarSeverity.Success);
            }
            catch (Exception exception)
            {
                Trace.WriteLine($"WINTODO_ICAL_IMPORT_APPLY_EXCEPTION={exception}");
                await ShowImportErrorAsync(_resources.GetString("Import_ApplyFailed"));
            }
        }
        catch (IcalFormatException exception)
        {
            Trace.WriteLine($"WINTODO_ICAL_IMPORT_FORMAT_EXCEPTION={exception}");
            string message = exception.LineNumber > 0
                ? string.Format(
                    DisplayCulture.Current,
                    _resources.GetString("Import_MalformedMessage"),
                    exception.LineNumber)
                : _resources.GetString("Import_MalformedMessageNoLine");
            await ShowImportErrorAsync(message);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Trace.WriteLine($"WINTODO_ICAL_IMPORT_READ_EXCEPTION={exception}");
            await ShowImportErrorAsync(_resources.GetString("Import_ReadFailed"));
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"WINTODO_ICAL_IMPORT_EXCEPTION={exception}");
            await ShowImportErrorAsync(_resources.GetString("Import_ApplyFailed"));
        }
        finally
        {
            ViewModel.SetTransferBusy(false);
        }
    }

    private async void OnExportListRequested(ShellListItemViewModel list)
    {
        ViewModel.SetTransferBusy(true);
        try
        {
            var picker = new FileSavePicker(App.WindowId)
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = SanitizeFileName(list.DisplayName),
            };
            picker.FileTypeChoices.Add(
                _resources.GetString("Ical_FileType"),
                new List<string> { ".ics" });
            PickFileResult? target = await picker.PickSaveFileAsync();
            if (target is null)
            {
                return;
            }

            IReadOnlyList<TodoTask> tasks = await _taskStore.GetTasksForExportAsync(list.Id);
            var model = new TodoList
            {
                Id = list.Id,
                Uid = list.Uid,
                DisplayName = list.DisplayName,
                Color = list.Color,
                IconEmoji = list.IconEmoji,
            };
            byte[] content = new IcalWriter(_clock.LocalTimeZone).WriteCalendar(
                model,
                tasks,
                _clock.UtcNow,
                ApplicationData.Current.LocalFolder.Path);
            await WriteFileAtomicallyAsync(target.Path, content);
            ShowTransferStatus(
                _resources.GetString("Transfer_StatusTitleSuccess"),
                string.Format(
                    DisplayCulture.Current,
                    _resources.GetString("ExportList_SuccessMessage"),
                    list.DisplayName),
                InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"WINTODO_ICAL_EXPORT_LIST_EXCEPTION={exception}");
            ShowTransferStatus(
                _resources.GetString("Transfer_StatusTitleError"),
                _resources.GetString("Export_Failed"),
                InfoBarSeverity.Error);
        }
        finally
        {
            ViewModel.SetTransferBusy(false);
        }
    }

    private async void OnExportAllRequested()
    {
        ViewModel.SetTransferBusy(true);
        string? temporaryPath = null;
        try
        {
            var picker = new FileSavePicker(App.WindowId)
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = _resources.GetString("ExportAll_SuggestedFileName"),
            };
            picker.FileTypeChoices.Add(
                _resources.GetString("Zip_FileType"),
                new List<string> { ".zip" });
            PickFileResult? target = await picker.PickSaveFileAsync();
            if (target is null)
            {
                return;
            }

            temporaryPath = CreateTemporarySiblingPath(target.Path);
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var writer = new IcalWriter(_clock.LocalTimeZone);
            await using (var file = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 81920,
                             useAsync: true))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: false))
            {
                foreach (ShellListItemViewModel list in ViewModel.UserLists)
                {
                    IReadOnlyList<TodoTask> tasks =
                        await _taskStore.GetTasksForExportAsync(list.Id);
                    var model = new TodoList
                    {
                        Id = list.Id,
                        Uid = list.Uid,
                        DisplayName = list.DisplayName,
                        Color = list.Color,
                        IconEmoji = list.IconEmoji,
                    };
                    byte[] content = writer.WriteCalendar(
                        model,
                        tasks,
                        _clock.UtcNow,
                        ApplicationData.Current.LocalFolder.Path);
                    string entryName = GetUniqueFileName(list.DisplayName, usedNames);
                    ZipArchiveEntry entry = archive.CreateEntry(
                        entryName,
                        CompressionLevel.Optimal);
                    await using Stream entryStream = entry.Open();
                    await entryStream.WriteAsync(content);
                }
            }

            File.Move(temporaryPath, target.Path, overwrite: true);
            temporaryPath = null;
            ShowTransferStatus(
                _resources.GetString("Transfer_StatusTitleSuccess"),
                string.Format(
                    DisplayCulture.Current,
                    _resources.GetString("ExportAll_SuccessMessage"),
                    ViewModel.UserLists.Count),
                InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"WINTODO_ICAL_EXPORT_ALL_EXCEPTION={exception}");
            ShowTransferStatus(
                _resources.GetString("Transfer_StatusTitleError"),
                _resources.GetString("Export_Failed"),
                InfoBarSeverity.Error);
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
            ViewModel.SetTransferBusy(false);
        }
    }

    private async Task ShowImportErrorAsync(string message)
    {
        ViewModel.IsImportErrorDialogLoaded = true;
        await Task.Yield();
        ImportErrorDialog.XamlRoot = XamlRoot;
        ImportErrorMessage.Text = message;
        await ImportErrorDialog.ShowAsync();
    }

    private void OnImportPreviewDialogOpened(
        ContentDialog sender,
        ContentDialogOpenedEventArgs args)
    {
        SetOpenDialogButtonAutomationId(
            sender,
            "PrimaryButton",
            "Todo.Dialog.ImportPreview.Import");
        SetOpenDialogButtonAutomationId(
            sender,
            "CloseButton",
            "Todo.Dialog.ImportPreview.Cancel");
    }

    private void OnImportErrorDialogOpened(
        ContentDialog sender,
        ContentDialogOpenedEventArgs args)
    {
        SetOpenDialogButtonAutomationId(
            sender,
            "CloseButton",
            "Todo.Dialog.ImportError.Close");
    }

    private void SetOpenDialogButtonAutomationId(
        ContentDialog dialog,
        string generatedId,
        string automationId)
    {
        if (TrySetOpenDialogButtonAutomationId(dialog, generatedId, automationId))
        {
            return;
        }

        DispatcherQueue.TryEnqueue(
            () => TrySetOpenDialogButtonAutomationId(dialog, generatedId, automationId));
    }

    private static bool TrySetOpenDialogButtonAutomationId(
        ContentDialog dialog,
        string generatedId,
        string automationId)
    {
        foreach (Microsoft.UI.Xaml.Controls.Primitives.Popup popup in
                 VisualTreeHelper.GetOpenPopupsForXamlRoot(dialog.XamlRoot))
        {
            if (TrySetDialogButtonAutomationId(popup.Child, generatedId, automationId))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TrySetDialogButtonAutomationId(
        DependencyObject root,
        string generatedId,
        string automationId)
    {
        int childCount = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < childCount; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is Button button
                && (button.Name == generatedId
                    || AutomationProperties.GetAutomationId(button) == generatedId))
            {
                AutomationProperties.SetAutomationId(button, automationId);
                return true;
            }

            if (TrySetDialogButtonAutomationId(child, generatedId, automationId))
            {
                return true;
            }
        }

        return false;
    }

    private void ShowTransferStatus(
        string title,
        string message,
        InfoBarSeverity severity)
    {
        _transferStatusCancellation?.Cancel();
        _transferStatusCancellation?.Dispose();
        _transferStatusCancellation = new CancellationTokenSource();
        ViewModel.ShowTransferStatus(
            title,
            message,
            severity == InfoBarSeverity.Error);
        _ = CloseTransferStatusAsync(_transferStatusCancellation.Token);
    }

    private async Task CloseTransferStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(6), cancellationToken);
            ViewModel.IsTransferStatusOpen = false;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task WriteFileAtomicallyAsync(string targetPath, byte[] content)
    {
        string temporaryPath = CreateTemporarySiblingPath(targetPath);
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, content);
            File.Move(temporaryPath, targetPath, overwrite: true);
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    private static void TryDeleteTemporaryFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"WINTODO_ICAL_TEMP_CLEANUP_EXCEPTION={exception}");
        }
    }

    private static string CreateTemporarySiblingPath(string targetPath)
    {
        string directory = System.IO.Path.GetDirectoryName(targetPath)
            ?? throw new IOException("The selected destination has no directory.");
        return System.IO.Path.Combine(
            directory,
            $".{System.IO.Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
    }

    private string GetUniqueFileName(string displayName, ISet<string> usedNames)
    {
        string stem = SanitizeFileName(displayName);
        string candidate = stem + ".ics";
        int suffix = 2;
        while (!usedNames.Add(candidate))
        {
            candidate = string.Format(
                DisplayCulture.Current,
                _resources.GetString("Export_DuplicateFileNameFormat"),
                stem,
                suffix++);
        }

        return candidate;
    }

    private string SanitizeFileName(string value)
    {
        char[] invalid = System.IO.Path.GetInvalidFileNameChars();
        string sanitized = new(value
            .Select(character => invalid.Contains(character) ? '_' : character)
            .ToArray());
        sanitized = sanitized.Trim().TrimEnd('.');
        if (sanitized.Length == 0)
        {
            sanitized = _resources.GetString("Export_FallbackFileName");
        }

        if (sanitized.Length <= 96)
        {
            return sanitized;
        }

        int length = char.IsHighSurrogate(sanitized[95]) ? 95 : 96;
        return sanitized[..length];
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ShellViewModel.IsCreatingList)
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
                VisualStateManager.GoToState(this, "DetailOpen", useTransitions: true);
            }
            else
            {
                if (_taskListEventsAttached)
                {
                    TaskList.ClearTaskSelection();
                }

                _ = CloseDetailVisualAsync();
            }
        }
    }

    private async void OnTaskOpenRequested(TaskItemViewModel task)
    {
        await ViewModel.OpenDetailAsync(task);
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

    private void OnNewListNameBoxKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Enter
            && ViewModel.CreateListCommand.CanExecute(null))
        {
            ViewModel.CreateListCommand.Execute(null);
            args.Handled = true;
        }
        else if (args.Key == VirtualKey.Escape)
        {
            ViewModel.CancelCreateListCommand.Execute(null);
            args.Handled = true;
        }
    }

    private void OnNewGroupNameBoxKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Enter
            && ViewModel.CreateGroupCommand.CanExecute(null))
        {
            ViewModel.CreateGroupCommand.Execute(null);
            args.Handled = true;
        }
        else if (args.Key == VirtualKey.Escape)
        {
            ViewModel.CancelCreateGroupCommand.Execute(null);
            args.Handled = true;
        }
    }

    // Clicking anywhere outside leaves the editor, the same way the inline list title
    // editor behaves. A blank name has nothing to commit, so it reverts instead.
    private void OnNewListNameBoxLostFocus(object sender, RoutedEventArgs args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!ViewModel.IsCreatingList || !HasFocusMovedInsideApp())
            {
                return;
            }

            if (ViewModel.CreateListCommand.CanExecute(null))
            {
                ViewModel.CreateListCommand.Execute(null);
                return;
            }

            ViewModel.CancelCreateListCommand.Execute(null);
        });
    }

    private void OnNewGroupNameBoxLostFocus(object sender, RoutedEventArgs args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!ViewModel.IsCreatingGroup || !HasFocusMovedInsideApp())
            {
                return;
            }

            if (ViewModel.CreateGroupCommand.CanExecute(null))
            {
                ViewModel.CreateGroupCommand.Execute(null);
                return;
            }

            ViewModel.CancelCreateGroupCommand.Execute(null);
        });
    }

    // Switching away from the window also raises LostFocus, and closing the editor
    // there would drop a half-typed name on every alt-tab. Focus lands on nothing
    // inside the app in that case, so the editor stays open until focus moves to
    // another element in the window.
    private bool HasFocusMovedInsideApp()
    {
        return XamlRoot is { } xamlRoot
            && FocusManager.GetFocusedElement(xamlRoot) is not null;
    }

    // Closing the editor takes the focused element out of the tree. Without a
    // landing spot focus flies on to the next tab stop — the footer menu — where
    // the key-up half of the committing Enter press opens Settings.
    private void ReturnFocusToPaneFooter(Control button)
    {
        if (NewItemActionsHost.Visibility != Visibility.Visible
            || button.Visibility != Visibility.Visible)
        {
            return;
        }

        button.Focus(FocusState.Programmatic);
    }

    private IList<object> CreateKeyboardShortcuts()
    {
        var shortcuts = new List<object>(19);
        for (int index = 1; index <= 19; index++)
        {
            string key = index.ToString("00", CultureInfo.InvariantCulture);
            shortcuts.Add(new ShortcutEntryViewModel(
                _resources.GetString($"Shortcut{key}_Key"),
                _resources.GetString($"Shortcut{key}_Action")));
        }

        return shortcuts;
    }

    private void AddPunctuationAccelerators()
    {
        var settings = new KeyboardAccelerator
        {
            Key = (VirtualKey)188,
            Modifiers = VirtualKeyModifiers.Control,
        };
        settings.Invoked += OnSettingsAccelerator;
        KeyboardAccelerators.Add(settings);

        var shortcuts = new KeyboardAccelerator
        {
            Key = (VirtualKey)191,
            Modifiers = VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift,
        };
        shortcuts.Invoked += OnShowShortcutsAccelerator;
        KeyboardAccelerators.Add(shortcuts);
    }

    private async void OnKeyboardShortcutsRequested()
    {
        if (_isShortcutsDialogOpen)
        {
            return;
        }

        _isShortcutsDialogOpen = true;
        try
        {
            ViewModel.IsShortcutsDialogLoaded = true;
            await Task.Yield();
            await ShortcutsDialog.ShowAsync();
        }
        catch (COMException exception)
            when (exception.HResult == unchecked((int)0x80000019))
        {
            Trace.WriteLine($"WINTODO_SHORTCUTS_DIALOG_BUSY={exception.Message}");
        }
        finally
        {
            _isShortcutsDialogOpen = false;
        }
    }

    private void OnShortcutsDialogOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        SetOpenDialogButtonAutomationId(
            sender,
            generatedId: "CloseButton",
            automationId: "Todo.Dialog.Shortcuts.Close");
    }

    private void OnNewListAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        ViewModel.BeginCreateListCommand.Execute(null);
        args.Handled = true;
    }

    private void OnNewGroupAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        ViewModel.BeginCreateGroupCommand.Execute(null);
        args.Handled = true;
    }

    private void OnFocusSearchAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        SearchFocusRequested?.Invoke();
        args.Handled = true;
    }

    private void OnSettingsAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        ShellNavigation.SelectedItem = SettingsNavigationItem;
        args.Handled = true;
    }

    private void OnShellKeyDown(object sender, KeyRoutedEventArgs args)
    {
        const int virtualKeyControl = 0x11;
        if ((GetKeyState(virtualKeyControl) & 0x8000) == 0)
        {
            return;
        }

        if (args.Key == (VirtualKey)188)
        {
            ShellNavigation.SelectedItem = SettingsNavigationItem;
            args.Handled = true;
        }
        else if (args.Key == (VirtualKey)191)
        {
            OnKeyboardShortcutsRequested();
            args.Handled = true;
        }
    }

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(
        uint action,
        uint parameter,
        ref HighContrastState state,
        uint updateFlags);

    private static bool IsSystemHighContrastEnabled()
    {
        var state = new HighContrastState
        {
            Size = checked((uint)Unsafe.SizeOf<HighContrastState>()),
        };
        return SystemParametersInfo(
                SpiGetHighContrast,
                state.Size,
                ref state,
                updateFlags: 0)
            && (state.Flags & HighContrastOn) != 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HighContrastState
    {
        public uint Size;

        public uint Flags;

        public nint DefaultScheme;
    }

    private void OnShowShortcutsAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        OnKeyboardShortcutsRequested();
        args.Handled = true;
    }

    private async void OnDueDateAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        TaskItemViewModel? selected = ViewModel.TaskList.SelectedTask;
        if (selected is null)
        {
            return;
        }

        await ViewModel.OpenDetailAsync(selected);
        DispatcherQueue.TryEnqueue(TaskDetail.OpenDuePicker);
        args.Handled = true;
    }

    private async void OnReminderAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        TaskItemViewModel? selected = ViewModel.TaskList.SelectedTask;
        if (selected is null)
        {
            return;
        }

        await ViewModel.OpenDetailAsync(selected);
        DispatcherQueue.TryEnqueue(TaskDetail.OpenReminderPicker);
        args.Handled = true;
    }

    private void OnSmartListAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        ShellNavigation.SelectedItem = sender.Key switch
        {
            VirtualKey.Number2 => ImportantNavigationItem,
            VirtualKey.Number3 => PlannedNavigationItem,
            VirtualKey.Number4 => AllNavigationItem,
            VirtualKey.Number5 => CompletedNavigationItem,
            _ => MyDayNavigationItem,
        };
        args.Handled = true;
    }

    private void OnEscapeAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewModel.IsDetailPaneOpen)
        {
            ViewModel.CloseDetail();
            args.Handled = true;
            return;
        }

        if (SearchClearRequested?.Invoke() == true)
        {
            args.Handled = true;
            return;
        }

        if (ViewModel.TaskList.IsComposerExpanded)
        {
            ViewModel.TaskList.CancelComposerCommand.Execute(null);
            args.Handled = true;
        }
    }

    private void ScheduleMidnightRefresh()
    {
        _midnightTimer ??= DispatcherQueue.CreateTimer();
        _midnightTimer.Stop();
        _midnightTimer.Tick -= OnMidnightTimerTick;
        _midnightTimer.Tick += OnMidnightTimerTick;
        _midnightTimer.IsRepeating = false;

        DateTime nextLocalMidnight = DateTime.SpecifyKind(
            _clock.Today.AddDays(1).ToDateTime(TimeOnly.MinValue),
            DateTimeKind.Unspecified);
        DateTime nextUtc =
            TimeZoneInfo.ConvertTimeToUtc(nextLocalMidnight, _clock.LocalTimeZone);
        TimeSpan interval = nextUtc - _clock.UtcNow.UtcDateTime;
        _midnightTimer.Interval = interval > TimeSpan.Zero
            ? interval
            : TimeSpan.FromSeconds(1);
        _midnightTimer.Start();
    }

    private void ScheduleBackgroundSync()
    {
        _syncTimer ??= DispatcherQueue.CreateTimer();
        _syncTimer.Stop();
        _syncTimer.Tick -= OnSyncTimerTick;
        _syncTimer.Tick += OnSyncTimerTick;
        _syncTimer.Interval = TimeSpan.FromMinutes(15);
        _syncTimer.IsRepeating = true;
        _syncTimer.Start();
    }

    private async void OnSyncTimerTick(
        Microsoft.UI.Dispatching.DispatcherQueueTimer sender,
        object args)
    {
        await RunBackgroundSyncAsync();
    }

    private async Task RunBackgroundSyncAsync(
        CancellationToken cancellationToken = default)
    {
        _lastSyncAttempt = _clock.UtcNow;
        try
        {
            await ViewModel.RunBackgroundSyncAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Trace.WriteLine($"WINTODO_BACKGROUND_SYNC_EXCEPTION={exception}");
        }
    }

    private async void OnSyncConflictsRequested(IReadOnlyList<SyncConflict> conflicts)
    {
        if (_isSyncConflictDialogOpen || conflicts.Count == 0)
        {
            return;
        }

        _isSyncConflictDialogOpen = true;
        try
        {
            foreach (SyncConflict conflict in conflicts)
            {
                ViewModel.IsSyncConflictDialogLoaded = true;
                await Task.Yield();
                SyncConflictDialog.XamlRoot = XamlRoot;
                SyncConflictMessage.Text = string.Format(
                    DisplayCulture.Current,
                    _resources.GetString(
                        conflict.RemoteDeleted
                            ? "Sync_ConflictDeletedMessage"
                            : "Sync_ConflictMessage"),
                    conflict.LocalTask.Summary,
                    conflict.ListName);
                ContentDialogResult result = await SyncConflictDialog.ShowAsync();
                SyncConflictChoice choice = result switch
                {
                    ContentDialogResult.Primary => SyncConflictChoice.KeepMine,
                    ContentDialogResult.Secondary => SyncConflictChoice.KeepTheirs,
                    _ => SyncConflictChoice.KeepBoth,
                };
                await ViewModel.ResolveSyncConflictAsync(conflict, choice);
            }

            await ViewModel.PushConflictResolutionsAsync();
        }
        catch (COMException exception)
            when (exception.HResult == unchecked((int)0x80000019))
        {
            Trace.WriteLine($"WINTODO_SYNC_DIALOG_BUSY={exception.Message}");
        }
        finally
        {
            _isSyncConflictDialogOpen = false;
        }
    }

    private void OnSyncConflictDialogOpened(
        ContentDialog sender,
        ContentDialogOpenedEventArgs args)
    {
        SetOpenDialogButtonAutomationId(
            sender,
            "PrimaryButton",
            "Todo.Dialog.SyncConflict.KeepMine");
        SetOpenDialogButtonAutomationId(
            sender,
            "SecondaryButton",
            "Todo.Dialog.SyncConflict.KeepTheirs");
        SetOpenDialogButtonAutomationId(
            sender,
            "CloseButton",
            "Todo.Dialog.SyncConflict.KeepBoth");
    }

    private async void OnMidnightTimerTick(
        Microsoft.UI.Dispatching.DispatcherQueueTimer sender,
        object args)
    {
        await ViewModel.TaskList.RefreshForDateChangeAsync();
        await ViewModel.RefreshNavigationCountsAsync();
        ScheduleMidnightRefresh();
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        string? selectedKey =
            (ShellNavigation.SelectedItem as NavigationViewItem)?.Tag as string;
        PopulateUserListNavigationItems();
        _selectedList = ViewModel.UserLists.FirstOrDefault(
            list => list.NavigationKey == selectedKey);
        ApplyListAccent(_selectedList);
        ApplyListBackground();
        ViewModel.RefreshFixedCategoryIconBrushes();
    }

    private void OnColorValuesChanged(UISettings sender, object args)
    {
        bool isHighContrast = IsSystemHighContrastEnabled();
        DispatcherQueue.TryEnqueue(() =>
        {
            _isHighContrast = isHighContrast;
            ApplyListAccent(_selectedList, isHighContrast);
            RefreshUserListAccentBrushes(isHighContrast);
            ApplyListBackground(isHighContrast);
            ViewModel.RefreshFixedCategoryIconBrushes();
        });
    }

    private void ApplyListAccent(
        ShellListItemViewModel? list,
        bool? isHighContrast = null)
    {
        string resourceKey = list?.AccentResourceKey ?? "ListAccentBrush";
        Brush brush = (isHighContrast ?? _isHighContrast)
            ? ResolveBrush("SystemColorHighlightColorBrush")
            : ResolveBrush(resourceKey);
        TaskList.Resources["ListAccentBrush"] = brush;
        DetailPane.Resources["ListAccentBrush"] = brush;
        ShellNavigation.Resources["NavigationViewSelectionIndicatorForeground"] = brush;
    }

    private void UpdateFixedCategoryNavigationAccessibility()
    {
        SetFixedCategoryNavigationAccessibleName(
            MyDayNavigationItem,
            ViewModel.MyDayTaskCount);
        SetFixedCategoryNavigationAccessibleName(
            ImportantNavigationItem,
            ViewModel.ImportantTaskCount);
        SetFixedCategoryNavigationAccessibleName(
            PlannedNavigationItem,
            ViewModel.PlannedTaskCount);
        SetFixedCategoryNavigationAccessibleName(
            AllNavigationItem,
            ViewModel.AllTaskCount);
        SetFixedCategoryNavigationAccessibleName(
            CompletedNavigationItem,
            ViewModel.CompletedTaskCount);
    }

    private void SetFixedCategoryNavigationAccessibleName(
        NavigationViewItem item,
        int count)
    {
        string title = item.Content?.ToString() ?? string.Empty;
        string accessibleName = count == 0
            ? title
            : string.Format(
                DisplayCulture.Current,
                _resources.GetString("FixedCategory_AccessibleNameWithCount"),
                title,
                count);
        AutomationProperties.SetName(item, accessibleName);
    }

    private void RefreshUserListAccentBrushes(bool isHighContrast)
    {
        Brush? highContrastBrush = isHighContrast
            ? ResolveBrush("SystemColorHighlightColorBrush")
            : null;
        foreach (ShellListItemViewModel list in ViewModel.UserLists)
        {
            if (_navigationItems.TryGetValue(list.NavigationKey, out NavigationViewItem? item)
                && item.Content is Grid { Children.Count: > 0 } content
                && content.Children[0] is Ellipse dot)
            {
                dot.Fill = highContrastBrush ?? ResolveBrush(list.AccentResourceKey);
            }
        }
    }

    private void OnAppearanceSettingsChanged()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            ThemeChanged?.Invoke(ThemeForIndex(ViewModel.AppearanceThemeIndex));
            ApplyListBackground();
        });
    }

    // The detail pane's upper drag limit depends on how much room the window has,
    // so it cannot come from a visual state the way its column and margin do.
    private void OnContentGridSizeChanged(object sender, SizeChangedEventArgs args)
    {
        const double listReserve = 320;
        const double hardMaximum = 960;
        DetailHost.MaxWidth = Math.Clamp(
            args.NewSize.Width - listReserve,
            DetailHost.MinWidth,
            hardMaximum);
    }

    private void OnListPaneSizeChanged(object sender, SizeChangedEventArgs args)
    {
        int decodeWidth = GetBackgroundDecodeWidth();
        if (ListBackgroundImage.Source is not null
            && Math.Abs(decodeWidth - _backgroundDecodeWidth) >= 64)
        {
            ApplyListBackground();
        }
    }

    private void ApplyListBackground(bool? isHighContrast = null)
    {
        ListBackgroundImage.Source = null;
        _backgroundDecodeWidth = 0;
        string? assetName = ResolveBackgroundAsset(ViewModel.TaskList.BackgroundTheme);
        if (!ViewModel.ShowListBackgrounds
            || assetName is null
            || (isHighContrast ?? _isHighContrast))
        {
            return;
        }

        int decodeWidth = GetBackgroundDecodeWidth();
        if (decodeWidth <= 0)
        {
            return;
        }

        var bitmap = new BitmapImage
        {
            DecodePixelWidth = decodeWidth,
            UriSource = new Uri($"ms-appx:///Assets/ListBackgrounds/{assetName}.jpg"),
        };
        _backgroundDecodeWidth = decodeWidth;
        ListBackgroundImage.Source = bitmap;
    }

    private int GetBackgroundDecodeWidth()
    {
        double scale = XamlRoot?.RasterizationScale ?? 1;
        return checked((int)Math.Ceiling(ListPane.ActualWidth * scale));
    }

    private static string? ResolveBackgroundAsset(string? theme)
    {
        return theme is "Coast"
            or "Forest"
            or "Mist"
            or "Sunrise"
            or "Indigo"
            or "Petals"
            or "Graphite"
            or "Field"
            ? theme
            : null;
    }

    private Brush ResolveBrush(string resourceKey)
    {
        return (Brush)Application.Current.Resources[resourceKey];
    }

    private object ResolveNavigationItem(string navigationKey)
    {
        if (_navigationItems.TryGetValue(navigationKey, out NavigationViewItem? item))
        {
            return item;
        }

        return navigationKey switch
        {
            "settings" => SettingsNavigationItem,
            "smart:important" => ImportantNavigationItem,
            "smart:planned" => PlannedNavigationItem,
            "smart:all" => AllNavigationItem,
            "smart:completed" => CompletedNavigationItem,
            _ => MyDayNavigationItem,
        };
    }
}
