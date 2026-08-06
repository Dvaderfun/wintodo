using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Windows.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.ViewManagement;
using Wintodo.ViewModels;

namespace Wintodo.Controls;

public sealed partial class TaskListView : UserControl
{
    internal const string TaskDragFormat = "application/x-wintodo-task-uid";
    private readonly UISettings _uiSettings = new();
    private readonly HashSet<TaskItemViewModel> _observedTaskItems = [];
    private bool _isSynchronizingSelection;

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

    internal event Action<TaskItemViewModel>? TaskOpenRequested;
    internal event Action? TaskCloseRequested;

    public static Visibility BoolToVisibility(bool value)
    {
        return value ? Visibility.Visible : Visibility.Collapsed;
    }

    public static Visibility TitleButtonVisibility(bool canEdit, bool isRenaming)
    {
        return canEdit && !isRenaming ? Visibility.Visible : Visibility.Collapsed;
    }

    public static Visibility InverseBoolToVisibility(bool value)
    {
        return value ? Visibility.Collapsed : Visibility.Visible;
    }

    public static string CompletedChevron(bool isExpanded)
    {
        return isExpanded ? "\uE70D" : "\uE76C";
    }

    public static Visibility NamedGroupVisibility(TaskListGroupKind kind)
    {
        return kind == TaskListGroupKind.Named
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public static Visibility CompletedGroupVisibility(TaskListGroupKind kind)
    {
        return kind == TaskListGroupKind.Completed
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public static double GroupHeaderMinHeight(TaskListGroupKind kind)
    {
        return kind == TaskListGroupKind.Hidden ? 0 : 32;
    }

    public static Brush GroupHeaderBrush(bool isCritical)
    {
        string resourceKey = isCritical
            ? "SystemFillColorCriticalBrush"
            : "TextFillColorPrimaryBrush";
        return (Brush)Application.Current.Resources[resourceKey];
    }

    public static double GroupHeaderMinHeightForItems(object items)
    {
        return GroupHeaderMinHeight(
            TaskListGroupViewModel.GetMetadata(items).Kind);
    }

    public static Visibility NamedGroupVisibilityForItems(object items)
    {
        return NamedGroupVisibility(
            TaskListGroupViewModel.GetMetadata(items).Kind);
    }

    public static Visibility CompletedGroupVisibilityForItems(object items)
    {
        return CompletedGroupVisibility(
            TaskListGroupViewModel.GetMetadata(items).Kind);
    }

    public static Brush GroupHeaderBrushForItems(object items)
    {
        return GroupHeaderBrush(
            TaskListGroupViewModel.GetMetadata(items).IsCritical);
    }

    public static string GroupAutomationId(object items)
    {
        return TaskListGroupViewModel.GetMetadata(items).AutomationId;
    }

    public static string GroupTitle(object items)
    {
        return TaskListGroupViewModel.GetMetadata(items).Title;
    }

    public static string GroupAccessibleName(object items)
    {
        return TaskListGroupViewModel.GetMetadata(items).AccessibleName;
    }

    public static int GroupHeaderCount(object items)
    {
        return TaskListGroupViewModel.GetMetadata(items).HeaderCount;
    }

    public static ICommand GroupToggleCommand(object items)
    {
        return TaskListGroupViewModel.GetMetadata(items).ToggleCommand;
    }

    public static bool GroupIsExpanded(object items)
    {
        return TaskListGroupViewModel.GetMetadata(items).IsExpanded;
    }

    public static string GroupCompletedChevron(object items)
    {
        return CompletedChevron(GroupIsExpanded(items));
    }

    internal void FocusComposer()
    {
        Composer.FocusComposer();
    }

    internal void SelectTask(TaskItemViewModel item)
    {
        TasksList.SelectedItem = item;
    }

    internal void FocusRenameBox()
    {
        RenameListBox.Focus(FocusState.Programmatic);
        RenameListBox.SelectAll();
    }

    internal void ClearTaskSelection()
    {
        _isSynchronizingSelection = true;
        try
        {
            foreach (TaskItemViewModel task in
                     TasksList.SelectedItems.OfType<TaskItemViewModel>())
            {
                task.IsSelected = false;
            }

            TasksList.SelectedItems.Clear();
            ViewModel.SelectedTask = null;
        }
        finally
        {
            _isSynchronizingSelection = false;
        }
    }

    private static void OnViewModelChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        var view = (TaskListView)dependencyObject;
        if (args.OldValue is TaskListViewModel oldViewModel)
        {
            view.ClearObservedTaskItems();
            oldViewModel.PropertyChanged -= view.OnViewModelPropertyChanged;
            oldViewModel.VisibleItems.CollectionChanged -=
                view.OnVisibleItemsCollectionChanged;
            oldViewModel.TaskCompletionChanged -= view.OnTaskCompletionChanged;
            oldViewModel.ListIconChanged -= view.OnListIconChanged;
        }

        if (args.NewValue is TaskListViewModel newViewModel)
        {
            view.TasksCollectionViewSource.Source = newViewModel.TaskGroups;
            newViewModel.PropertyChanged += view.OnViewModelPropertyChanged;
            newViewModel.VisibleItems.CollectionChanged +=
                view.OnVisibleItemsCollectionChanged;
            newViewModel.TaskCompletionChanged += view.OnTaskCompletionChanged;
            newViewModel.ListIconChanged += view.OnListIconChanged;
            newViewModel.AnimationsEnabled = view._uiSettings.AnimationsEnabled;
            ScrollPerformanceProbe.TryStart(newViewModel.VisibleItems.Count);
        }
        else
        {
            view.TasksCollectionViewSource.Source = null;
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
        else if (args.PropertyName == nameof(TaskListViewModel.Title))
        {
            AnimateListSwitch();
        }
        else if (args.PropertyName == nameof(TaskListViewModel.TaskGroups))
        {
            TasksCollectionViewSource.Source = null;
            TasksCollectionViewSource.Source = ViewModel.TaskGroups;
        }
    }

    private void OnTaskCompletionChanged(TaskItemViewModel item)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            FrameworkElement announcementSource =
                TasksList.ContainerFromItem(item) as FrameworkElement ?? this;
            AutomationPeer? peer = FrameworkElementAutomationPeer.FromElement(
                    announcementSource)
                ?? FrameworkElementAutomationPeer.CreatePeerForElement(
                    announcementSource);
            peer?.RaiseNotificationEvent(
                AutomationNotificationKind.ActionCompleted,
                AutomationNotificationProcessing.MostRecent,
                ViewModel.CompletionAnnouncement,
                "Wintodo.TaskCompletion");
        });
    }

    private void AnimateListSwitch()
    {
        if (!_uiSettings.AnimationsEnabled)
        {
            return;
        }

        Microsoft.UI.Composition.Visual visual =
            ElementCompositionPreview.GetElementVisual(TaskContent);
        Microsoft.UI.Composition.ScalarKeyFrameAnimation opacity =
            visual.Compositor.CreateScalarKeyFrameAnimation();
        opacity.Duration = TimeSpan.FromMilliseconds(150);
        opacity.InsertKeyFrame(0, 0);
        opacity.InsertKeyFrame(1, 1);
        visual.StartAnimation("Opacity", opacity);

        Microsoft.UI.Composition.Vector3KeyFrameAnimation translation =
            visual.Compositor.CreateVector3KeyFrameAnimation();
        translation.Target = "Translation";
        translation.Duration = TimeSpan.FromMilliseconds(150);
        translation.InsertKeyFrame(0, new System.Numerics.Vector3(0, 8, 0));
        translation.InsertKeyFrame(1, System.Numerics.Vector3.Zero);
        ElementCompositionPreview.SetIsTranslationEnabled(TaskContent, true);
        TaskContent.StartAnimation(translation);
    }

    private void OnTaskSelectionChanged(
        object sender,
        SelectionChangedEventArgs args)
    {
        if (_isSynchronizingSelection)
        {
            return;
        }

        foreach (TaskItemViewModel removed in args.RemovedItems.OfType<TaskItemViewModel>())
        {
            removed.IsSelected = false;
        }

        TaskItemViewModel? selectedTask =
            args.AddedItems.OfType<TaskItemViewModel>().LastOrDefault();
        if (selectedTask is null)
        {
            if (TasksList.SelectedItems.Count == 0)
            {
                ViewModel.SelectedTask = null;
                TaskCloseRequested?.Invoke();
            }

            return;
        }

        _isSynchronizingSelection = true;
        try
        {
            foreach (TaskItemViewModel task in
                     TasksList.SelectedItems
                         .OfType<TaskItemViewModel>()
                         .Where(task => !ReferenceEquals(task, selectedTask))
                         .ToArray())
            {
                task.IsSelected = false;
                TasksList.SelectedItems.Remove(task);
            }
        }
        finally
        {
            _isSynchronizingSelection = false;
        }

        ViewModel.SelectedTask = selectedTask;
        selectedTask.IsSelected = true;
        TaskOpenRequested?.Invoke(selectedTask);
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
            if (_observedTaskItems.Add(task))
            {
                task.PropertyChanged += OnTaskItemPropertyChanged;
            }
            container.CanDrag = true;
            container.AllowDrop = true;
        }
    }

    private void OnTaskItemPropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (sender is not TaskItemViewModel task
            || args.PropertyName != nameof(TaskItemViewModel.AccessibleName))
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            if (TasksList.ContainerFromItem(task) is not ListViewItem container)
            {
                return;
            }

            string oldName = AutomationProperties.GetName(container);
            AutomationProperties.SetName(container, task.AccessibleName);
            AutomationPeer? peer = FrameworkElementAutomationPeer.FromElement(container)
                ?? FrameworkElementAutomationPeer.CreatePeerForElement(container);
            peer?.RaisePropertyChangedEvent(
                AutomationElementIdentifiers.NameProperty,
                oldName,
                task.AccessibleName);
        });
    }

    private void ClearObservedTaskItems()
    {
        foreach (TaskItemViewModel task in _observedTaskItems)
        {
            task.PropertyChanged -= OnTaskItemPropertyChanged;
        }
        _observedTaskItems.Clear();
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

    // Clicking anywhere outside leaves the editor, the same way the inline task title
    // editor behaves. A blank name has nothing to commit, so it reverts instead.
    private void OnRenameListBoxLostFocus(object sender, RoutedEventArgs args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!ViewModel.IsRenamingList || ListIconPickerFlyout.IsOpen)
            {
                return;
            }

            if (ViewModel.CommitRenameListCommand.CanExecute(null))
            {
                ViewModel.CommitRenameListCommand.Execute(null);
                return;
            }

            ViewModel.CancelRenameListCommand.Execute(null);
        });
    }

    private void OnListIconChanged()
    {
        ListIconPickerFlyout.Hide();
        if (ViewModel.IsRenamingList)
        {
            DispatcherQueue.TryEnqueue(FocusRenameBox);
        }
    }

    private void OnListIconPickerClosed(object sender, object args)
    {
        if (ViewModel.IsRenamingList)
        {
            DispatcherQueue.TryEnqueue(FocusRenameBox);
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

    private void OnOpenDetailAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewModel.SelectedTask is null || IsTextInputFocused())
        {
            return;
        }

        TaskOpenRequested?.Invoke(ViewModel.SelectedTask);
        args.Handled = true;
    }

    private void OnTasksListPreviewKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Space || ViewModel.SelectedTask is null)
        {
            return;
        }

        ViewModel.ToggleSelectedCompletionCommand.Execute(null);
        args.Handled = true;
    }

    private void OnToggleImportanceAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        ViewModel.ToggleSelectedImportanceCommand.Execute(null);
        args.Handled = ViewModel.SelectedTask is not null;
    }

    private void OnToggleMyDayAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        ViewModel.ToggleSelectedMyDayCommand.Execute(null);
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
        if (ViewModel.SelectedTask is not null)
        {
            ViewModel.BeginEditSelectedTaskCommand.Execute(null);
            args.Handled = true;
        }
        else if (ViewModel.CanEditSelectedList)
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

    private void OnSelectPreviousAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = MoveSelection(-1);
    }

    private void OnSelectNextAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = MoveSelection(1);
    }

    private bool MoveSelection(int offset)
    {
        if (IsTextInputFocused() || TasksList.Items.Count == 0)
        {
            return false;
        }

        int current = TasksList.SelectedIndex;
        int destination = current < 0
            ? (offset > 0 ? 0 : TasksList.Items.Count - 1)
            : Math.Clamp(current + offset, 0, TasksList.Items.Count - 1);
        TasksList.SelectedIndex = destination;
        TasksList.ScrollIntoView(TasksList.SelectedItem);
        TasksList.Focus(FocusState.Keyboard);
        return true;
    }

    private bool IsTextInputFocused()
    {
        object? focused = FocusManager.GetFocusedElement(XamlRoot);
        return focused is TextBox or AutoSuggestBox or RichEditBox;
    }
}
