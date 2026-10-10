using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Wintodo.ViewModels;

namespace Wintodo.Controls;

public sealed partial class AddTaskComposer : UserControl
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(TaskListViewModel),
            typeof(AddTaskComposer),
            new PropertyMetadata(null));

    public AddTaskComposer()
    {
        InitializeComponent();
        SizeChanged += OnComposerSizeChanged;
    }

    public TaskListViewModel ViewModel
    {
        get => (TaskListViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    internal void FocusComposer()
    {
        ViewModel.BeginComposerCommand.Execute(null);
        TaskTitleBox.Focus(FocusState.Programmatic);
    }

    public static bool Not(bool value)
    {
        return !value;
    }

    public static Visibility BoolToVisibility(bool value)
    {
        return value ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnComposerSizeChanged(object sender, SizeChangedEventArgs args)
    {
        UpdateActionLayout(args.NewSize.Width);
    }

    private void OnComposerActionsLoaded(object sender, RoutedEventArgs args)
    {
        UpdateActionLayout(ActualWidth);
    }

    private void UpdateActionLayout(double width)
    {
        // The action subtree is x:Load-deferred while the composer is collapsed.
        // A startup or detail-pane resize must not resolve setters into it yet.
        if (InlineOptions is null || CompactOptionsButton is null)
        {
            return;
        }

        // The detail pane can shrink this control while the window stays wide.
        VisualStateManager.GoToState(
            this,
            width < 880 ? "CompactActions" : "InlineActions",
            useTransitions: false);
    }

    private void OnTaskTitleBoxGotFocus(object sender, RoutedEventArgs args)
    {
        ViewModel.BeginComposerCommand.Execute(null);
    }

    private void OnTaskTitleBoxKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Enter && ViewModel.AddTaskCommand.CanExecute(null))
        {
            ViewModel.AddTaskCommand.Execute(null);
            args.Handled = true;
        }
        else if (args.Key == VirtualKey.Escape)
        {
            ViewModel.CancelComposerCommand.Execute(null);
            args.Handled = true;
        }
    }

    private void OnTargetListFlyoutOpening(object? sender, object args)
    {
        PopulateTargetLists(TargetListFlyout.Items);
    }

    private void OnCompactOptionsOpening(object? sender, object args)
    {
        PopulateTargetLists(CompactTargetList.Items);
    }

    private void PopulateTargetLists(IList<MenuFlyoutItemBase> items)
    {
        items.Clear();
        foreach (MoveTargetViewModel target in ViewModel.MoveTargets)
        {
            var item = new ToggleMenuFlyoutItem
            {
                Text = target.DisplayName,
                IsChecked = ViewModel.ComposerTargetListId == target.ListId,
                Command = ViewModel.SetComposerListCommand,
                CommandParameter = target.ListId,
            };
            AutomationProperties.SetAutomationId(item, target.ComposerAutomationId);
            items.Add(item);
        }
    }
}
