using System.Diagnostics;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;
using Wintodo.Controls;
using Wintodo.Core.Models;
using Wintodo.Services;
using Wintodo.ViewModels;

namespace Wintodo.Pages;

public sealed partial class ShellPage
{
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
        navigationItem.RegisterPropertyChangedCallback(
            NavigationViewItem.IsSelectedProperty,
            OnUserListSelectionChanged);
        RefreshUserListAccentBrush(navigationItem, list, _isHighContrast);
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

    private void OnSmartListDragOver(object sender, DragEventArgs args)
    {
        args.Handled = true;
        if (!args.DataView.Contains(TaskListView.TaskDragFormat))
        {
            args.AcceptedOperation = DataPackageOperation.None;
            args.DragUIOverride.Clear();
            return;
        }

        args.AcceptedOperation = DataPackageOperation.Move;
        args.DragUIOverride.Caption = _resources.GetString(
            ReferenceEquals(sender, MyDayNavigationItem) ? "Drop_AddToMyDay" : "Drop_MarkImportant");
        args.DragUIOverride.IsCaptionVisible = true;
    }

    private void OnSmartListDragLeave(object sender, DragEventArgs args)
    {
        args.DragUIOverride.Clear();
    }

    private async void OnSmartListDrop(object sender, DragEventArgs args)
    {
        if (!args.DataView.Contains(TaskListView.TaskDragFormat)) return;
        args.Handled = true;
        args.AcceptedOperation = DataPackageOperation.Move;
        var deferral = args.GetDeferral();
        try
        {
            object value = await args.DataView.GetDataAsync(TaskListView.TaskDragFormat);
            if (value is string uids)
            {
                await ViewModel.TaskList.AddDroppedTasksAsync(
                    uids.Split('\n', StringSplitOptions.RemoveEmptyEntries),
                    ReferenceEquals(sender, MyDayNavigationItem)
                        ? Wintodo.Core.Queries.ListQueryKind.MyDay
                        : Wintodo.Core.Queries.ListQueryKind.Important);
            }
        }
        finally { deferral.Complete(); }
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
                await ViewModel.TaskList.MoveTasksToListAsync(taskUid.Split('\n', StringSplitOptions.RemoveEmptyEntries), target.Id);
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

    private async void OnNavigationRequested(string navigationKey)
    {
        await SelectNavigationAsync(ResolveNavigationItem(navigationKey), CancellationToken.None);
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
