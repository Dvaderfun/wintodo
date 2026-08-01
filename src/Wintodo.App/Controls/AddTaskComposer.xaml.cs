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
        TargetListFlyout.Items.Clear();
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
            TargetListFlyout.Items.Add(item);
        }
    }
}
