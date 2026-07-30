using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.Windows.ApplicationModel.Resources;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.ViewManagement;
using Wintodo.Controls;
using Wintodo.Core.Services;
using Wintodo.Services;
using Wintodo.ViewModels;

namespace Wintodo.Pages;

public sealed partial class ShellPage : Page
{
    private const string UserListNavigationPrefix = "list:";
    private const string ListDragFormat = "application/x-wintodo-list-uid";
    private readonly ResourceLoader _resources = new();
    private readonly Dictionary<string, NavigationViewItem> _navigationItems =
        new(StringComparer.Ordinal);
    private readonly IClock _clock;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _midnightTimer;
    private bool _isNavigationInitialized;
    private bool _taskListEventsAttached;

    internal ShellPage(
        ITaskStore taskStore,
        IClock clock,
        ShellStateService stateService)
    {
        _clock = clock;
        ViewModel = new ShellViewModel(taskStore, clock, stateService);
        InitializeComponent();

        ViewModel.NavigationStructureChanged += OnNavigationStructureChanged;
        ViewModel.NavigationRequested += OnNavigationRequested;
        ViewModel.DeleteListRequested += OnDeleteListRequested;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ShellRoot.ActualThemeChanged += OnActualThemeChanged;

        UserListsNavigationItem.AllowDrop = true;
        UserListsNavigationItem.DragOver += OnNavigationDragOver;
        UserListsNavigationItem.Drop += OnUngroupedDrop;

        if (ShellNavigation.SettingsItem is NavigationViewItem settingsItem)
        {
            AutomationProperties.SetAutomationId(settingsItem, "Todo.Nav.Settings");
        }
    }

    public ShellViewModel ViewModel { get; }

    public ObservableCollection<NavigationViewItem> UserListNavigationItems { get; } = [];

    internal event Action? SearchFocusRequested;

    internal event Func<bool>? SearchClearRequested;

    internal async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ViewModel.IsTaskListLoaded = true;
        string selectedNavigation = await ViewModel.InitializeAsync(cancellationToken);
        PopulateUserListNavigationItems();
        object selectedItem = ResolveNavigationItem(selectedNavigation);
        ShellNavigation.SelectedItem = selectedItem;
        await ApplySelectionAsync(selectedItem, isSettingsSelected: false, cancellationToken);
        _isNavigationInitialized = true;
        ScheduleMidnightRefresh();
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
        ScheduleMidnightRefresh();
    }

    internal void StopMidnightRefresh()
    {
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

    private async void OnNavigationSelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (!_isNavigationInitialized)
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
        if (isSettingsSelected)
        {
            ViewModel.SelectSettings();
            ApplyListAccent(list: null);
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
        ApplyListAccent(
            navigationKey.StartsWith(UserListNavigationPrefix, StringComparison.Ordinal)
                ? ViewModel.UserLists.FirstOrDefault(
                    list => list.NavigationKey == navigationKey)
                : null);
    }

    private void PopulateUserListNavigationItems()
    {
        UserListNavigationItems.Clear();
        _navigationItems.Clear();

        foreach (ShellListItemViewModel list in ViewModel.UserLists
                     .Where(item => item.GroupId is null)
                     .OrderBy(item => item.SortOrder))
        {
            UserListNavigationItems.Add(CreateListNavigationItem(list));
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

            UserListNavigationItems.Add(groupItem);
        }
    }

    private NavigationViewItem CreateListNavigationItem(ShellListItemViewModel list)
    {
        var dot = new Ellipse
        {
            Width = 16,
            Height = 16,
            Fill = ResolveBrush(list.AccentResourceKey),
            VerticalAlignment = VerticalAlignment.Center,
        };
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
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(label, 1);
        content.Children.Add(dot);
        content.Children.Add(label);

        var navigationItem = new NavigationViewItem
        {
            Content = content,
            Tag = list.NavigationKey,
            CanDrag = true,
            AllowDrop = true,
            ContextFlyout = CreateListContextFlyout(list),
        };
        AutomationProperties.SetAutomationId(navigationItem, list.AutomationId);
        AutomationProperties.SetName(navigationItem, list.DisplayName);
        navigationItem.DragStarting += OnListDragStarting;
        navigationItem.DragOver += OnNavigationDragOver;
        navigationItem.Drop += OnListDrop;
        _navigationItems[list.NavigationKey] = navigationItem;
        return navigationItem;
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

    private async void OnUngroupedDrop(object sender, DragEventArgs args)
    {
        if (!args.DataView.Contains(ListDragFormat))
        {
            return;
        }

        object value = await args.DataView.GetDataAsync(ListDragFormat);
        if (value is string listUid)
        {
            await ViewModel.MoveListToGroupAsync(listUid, groupId: null);
        }
    }

    private void OnNavigationStructureChanged()
    {
        string? selectedKey = (ShellNavigation.SelectedItem as NavigationViewItem)?.Tag as string;
        PopulateUserListNavigationItems();
        if (selectedKey is not null)
        {
            ShellNavigation.SelectedItem = ResolveNavigationItem(selectedKey);
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
            CultureInfo.CurrentCulture,
            _resources.GetString("DeleteListDialog_Message"),
            list.DisplayName);
        ContentDialogResult result = await DeleteListDialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            await ViewModel.ConfirmDeleteListAsync(list);
        }
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
        else if (args.PropertyName == nameof(ShellViewModel.IsDetailPaneOpen))
        {
            if (ViewModel.IsDetailPaneOpen)
            {
                VisualStateManager.GoToState(this, "DetailOpen", useTransitions: true);
            }
            else
            {
                _ = CloseDetailVisualAsync();
            }
        }
    }

    private async void OnTaskOpenRequested(TaskItemViewModel task)
    {
        await ViewModel.OpenDetailAsync(task);
    }

    private void OnTaskListLoaded(object sender, RoutedEventArgs args)
    {
        if (_taskListEventsAttached)
        {
            return;
        }

        TaskList.TaskOpenRequested += OnTaskOpenRequested;
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

    private async void OnMidnightTimerTick(
        Microsoft.UI.Dispatching.DispatcherQueueTimer sender,
        object args)
    {
        await ViewModel.TaskList.RefreshForDateChangeAsync();
        ScheduleMidnightRefresh();
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        string? selectedKey =
            (ShellNavigation.SelectedItem as NavigationViewItem)?.Tag as string;
        PopulateUserListNavigationItems();
        ApplyListAccent(
            ViewModel.UserLists.FirstOrDefault(
                list => list.NavigationKey == selectedKey));
    }

    private void ApplyListAccent(ShellListItemViewModel? list)
    {
        string resourceKey = list?.AccentResourceKey ?? "ListAccentBrush";
        TaskList.Resources["ListAccentBrush"] = ResolveBrush(resourceKey);
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
            "smart:important" => ImportantNavigationItem,
            "smart:planned" => PlannedNavigationItem,
            "smart:all" => AllNavigationItem,
            "smart:completed" => CompletedNavigationItem,
            _ => MyDayNavigationItem,
        };
    }
}
