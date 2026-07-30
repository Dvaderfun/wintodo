using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Wintodo.ViewModels;

namespace Wintodo.Controls;

public sealed partial class TaskListView : UserControl
{
    internal const string TaskDragFormat = "application/x-wintodo-task-uid";

    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(TaskListViewModel),
            typeof(TaskListView),
            new PropertyMetadata(null, OnViewModelChanged));

    public TaskListView()
    {
        InitializeComponent();
    }

    public TaskListViewModel ViewModel
    {
        get => (TaskListViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static Visibility BoolToVisibility(bool value)
    {
        return value ? Visibility.Visible : Visibility.Collapsed;
    }

    public static Visibility InverseBoolToVisibility(bool value)
    {
        return value ? Visibility.Collapsed : Visibility.Visible;
    }

    public static string CompletedChevron(bool isExpanded)
    {
        return isExpanded ? "\uE70D" : "\uE76B";
    }

    internal void FocusComposer()
    {
        Composer.FocusComposer();
    }

    internal void FocusRenameBox()
    {
        RenameListBox.Focus(FocusState.Programmatic);
        RenameListBox.SelectAll();
    }

    private static void OnViewModelChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        var view = (TaskListView)dependencyObject;
        if (args.OldValue is TaskListViewModel oldViewModel)
        {
            oldViewModel.PropertyChanged -= view.OnViewModelPropertyChanged;
            oldViewModel.VisibleItems.CollectionChanged -=
                view.OnVisibleItemsCollectionChanged;
        }

        if (args.NewValue is TaskListViewModel newViewModel)
        {
            newViewModel.PropertyChanged += view.OnViewModelPropertyChanged;
            newViewModel.VisibleItems.CollectionChanged +=
                view.OnVisibleItemsCollectionChanged;
            ScrollPerformanceProbe.TryStart(newViewModel.VisibleItems.Count);
        }
    }

    private void OnVisibleItemsCollectionChanged(
        object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs args)
    {
        ScrollPerformanceProbe.TryStart(ViewModel.VisibleItems.Count);
    }

    private void OnViewModelPropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(TaskListViewModel.IsRenamingList)
            && ViewModel.IsRenamingList)
        {
            DispatcherQueue.TryEnqueue(FocusRenameBox);
        }
    }

    private void OnTaskSelectionChanged(
        object sender,
        SelectionChangedEventArgs args)
    {
        foreach (TaskItemViewModel removed in args.RemovedItems.OfType<TaskItemViewModel>())
        {
            removed.IsSelected = false;
        }

        ViewModel.SelectedTask = TasksList.SelectedItem as TaskItemViewModel;
        if (ViewModel.SelectedTask is not null)
        {
            ViewModel.SelectedTask.IsSelected = true;
        }
    }

    private void OnContainerContentChanging(
        ListViewBase sender,
        ContainerContentChangingEventArgs args)
    {
        if (args.ItemContainer is not ListViewItem container
            || args.Item is not TaskListEntryViewModel entry)
        {
            return;
        }

        AutomationProperties.SetAutomationId(container, entry.AutomationId);
        if (entry is TaskItemViewModel task)
        {
            AutomationProperties.SetName(container, task.AccessibleName);
            container.CanDrag = true;
            container.AllowDrop = true;
        }
        else if (entry is CompletedHeaderViewModel completedHeader)
        {
            AutomationProperties.SetName(container, completedHeader.AccessibleName);
            container.CanDrag = false;
            container.AllowDrop = false;
        }
    }

    private void OnDragItemsStarting(
        object sender,
        DragItemsStartingEventArgs args)
    {
        TaskItemViewModel? task = args.Items.OfType<TaskItemViewModel>().FirstOrDefault();
        if (task is null || task.IsCompleted)
        {
            args.Cancel = true;
            return;
        }

        args.Data.SetData(TaskDragFormat, task.Uid);
        args.Data.RequestedOperation = DataPackageOperation.Move;
    }

    private async void OnDragItemsCompleted(
        ListViewBase sender,
        DragItemsCompletedEventArgs args)
    {
        if (args.DropResult != DataPackageOperation.None)
        {
            await ViewModel.PersistVisibleOrderAsync();
        }
    }

    private void OnRenameListBoxKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Enter
            && ViewModel.CommitRenameListCommand.CanExecute(null))
        {
            ViewModel.CommitRenameListCommand.Execute(null);
            args.Handled = true;
        }
        else if (args.Key == VirtualKey.Escape)
        {
            ViewModel.CancelRenameListCommand.Execute(null);
            args.Handled = true;
        }
    }

    private void OnFocusComposerAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        FocusComposer();
        args.Handled = true;
    }

    private void OnToggleCompletionAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        ViewModel.ToggleSelectedCompletionCommand.Execute(null);
        args.Handled = ViewModel.SelectedTask is not null;
    }

    private void OnToggleImportanceAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        ViewModel.ToggleSelectedImportanceCommand.Execute(null);
        args.Handled = ViewModel.SelectedTask is not null;
    }

    private void OnDeleteAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        ViewModel.DeleteSelectedTaskCommand.Execute(null);
        args.Handled = ViewModel.SelectedTask is not null;
    }

    private void OnUndoAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewModel.UndoDeleteCommand.CanExecute(null))
        {
            ViewModel.UndoDeleteCommand.Execute(null);
            args.Handled = true;
        }
    }

    private void OnRenameAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewModel.IsUserListSelected)
        {
            ViewModel.BeginRenameListCommand.Execute(null);
            args.Handled = true;
        }
    }

    private void OnMoveUpAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        ViewModel.MoveSelectedUpCommand.Execute(null);
        args.Handled = ViewModel.SelectedTask is not null;
    }

    private void OnMoveDownAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        ViewModel.MoveSelectedDownCommand.Execute(null);
        args.Handled = ViewModel.SelectedTask is not null;
    }
}
