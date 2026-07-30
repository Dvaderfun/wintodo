using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Wintodo.Core.Services;
using Wintodo.Services;
using Wintodo.ViewModels;

namespace Wintodo.Pages;

public sealed partial class ShellPage : Page
{
    private const string UserListNavigationPrefix = "list:";

    internal ShellPage(
        ITaskStore taskStore,
        IClock clock,
        ShellStateService stateService)
    {
        ViewModel = new ShellViewModel(taskStore, clock, stateService);
        InitializeComponent();

        if (ShellNavigation.SettingsItem is NavigationViewItem settingsItem)
        {
            AutomationProperties.SetAutomationId(settingsItem, "Todo.Nav.Settings");
        }
    }

    public ShellViewModel ViewModel { get; }

    public ObservableCollection<NavigationViewItem> UserListNavigationItems { get; } = [];

    internal async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        string selectedNavigation = await ViewModel.InitializeAsync(cancellationToken);
        PopulateUserListNavigationItems();
        object selectedItem = ResolveNavigationItem(selectedNavigation);
        ShellNavigation.SelectedItem = selectedItem;
        await ApplySelectionAsync(selectedItem, isSettingsSelected: false, cancellationToken);
    }

    public static Visibility BoolToVisibility(bool value)
    {
        return value ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnNavigationSelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
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
            return;
        }

        switch (selectedItem)
        {
            case NavigationViewItem navigationItem
                when navigationItem.Tag is string navigationKey:
                await ViewModel.SelectAsync(
                    navigationKey,
                    navigationItem.Content?.ToString() ?? string.Empty,
                    ReferenceEquals(navigationItem, MyDayNavigationItem),
                    cancellationToken);
                break;
        }
    }

    private void PopulateUserListNavigationItems()
    {
        UserListNavigationItems.Clear();
        foreach (ShellListItemViewModel listItem in ViewModel.UserLists)
        {
            var navigationItem = new NavigationViewItem
            {
                Content = listItem.DisplayName,
                Tag = UserListNavigationPrefix + listItem.Uid,
            };
            AutomationProperties.SetAutomationId(
                navigationItem,
                $"Todo.Nav.UserList.{listItem.Uid}");
            AutomationProperties.SetName(navigationItem, listItem.DisplayName);
            UserListNavigationItems.Add(navigationItem);
        }
    }

    private object ResolveNavigationItem(string navigationKey)
    {
        if (navigationKey.StartsWith(UserListNavigationPrefix, StringComparison.Ordinal))
        {
            NavigationViewItem? item = UserListNavigationItems.FirstOrDefault(
                candidate => string.Equals(
                    candidate.Tag?.ToString(),
                    navigationKey,
                    StringComparison.Ordinal));
            if (item is not null)
            {
                return item;
            }
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
