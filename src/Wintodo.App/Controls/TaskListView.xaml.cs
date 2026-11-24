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
    private string? _draggedTaskUid;
    private TaskItemViewModel[] _dragSelection = [];
    private ScrollViewer? _tasksScrollViewer;
    private double _savedVerticalOffset;
    private TaskItemViewModel? _savedSelectedTask;
    private TaskItemViewModel[] _savedSelection = [];
    private TaskItemViewModel? _selectionAnchor;
    private string? _savedFocusedTaskUid;
    private int _updateVersion;
    private bool _restoringTaskFocus;
    private Microsoft.UI.Xaml.Media.Animation.TransitionCollection? _savedTransitions;
    private TaskItemViewModel? _viewportAnchor;
    private double _viewportAnchorY;

    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(TaskListViewModel),
            typeof(TaskListView),
            new PropertyMetadata(null, OnViewModelChanged));

    public TaskListView()
    {
        InitializeComponent();
        TasksList.BringIntoViewRequested += OnTaskBringIntoViewRequested;
        TasksList.PointerPressed += OnTaskPointerInteraction;
        TasksList.PointerWheelChanged += OnTaskPointerInteraction;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
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

    public static Visibility GroupHeaderVisibility(TaskListGroupKind kind) =>
        kind == TaskListGroupKind.Hidden ? Visibility.Collapsed : Visibility.Visible;

    public static TaskListGroupViewModel GroupMetadata(object items) =>
        TaskListGroupViewModel.GetMetadata(items);

    public static string GroupCompletedChevron(object items)
    {
        return CompletedChevron(GroupIsExpanded(items));
    }

    internal void FocusComposer()
    {
        Composer.FocusComposer();
    }

    internal void SetHeaderPlaceholderBrush(Brush brush)
    {
        ListIconPlaceholder.Fill = brush;
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
            ViewModel.SetSelection([]);
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
            oldViewModel.DueDatePickerRequested -= view.OnDueDatePickerRequested;
            oldViewModel.ListIconChanged -= view.OnListIconChanged;
            oldViewModel.VisibleItemsUpdating -= view.OnVisibleItemsUpdating;
            oldViewModel.VisibleItemsUpdated -= view.OnVisibleItemsUpdated;
            oldViewModel.CompletionReordering -= view.OnCompletionReordering;
        }

        if (args.NewValue is TaskListViewModel newViewModel)
        {
            view.TasksCollectionViewSource.Source = newViewModel.TaskGroups;
            newViewModel.PropertyChanged += view.OnViewModelPropertyChanged;
            newViewModel.VisibleItems.CollectionChanged +=
                view.OnVisibleItemsCollectionChanged;
            newViewModel.TaskCompletionChanged += view.OnTaskCompletionChanged;
            newViewModel.DueDatePickerRequested += view.OnDueDatePickerRequested;
            newViewModel.ListIconChanged += view.OnListIconChanged;
            newViewModel.VisibleItemsUpdating += view.OnVisibleItemsUpdating;
            newViewModel.VisibleItemsUpdated += view.OnVisibleItemsUpdated;
            newViewModel.CompletionReordering += view.OnCompletionReordering;
            newViewModel.AreAnimationsEnabled = () => view._uiSettings.AnimationsEnabled;
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
        else if (args.PropertyName == nameof(TaskListViewModel.IsUndoVisible))
        {
            ApplyUndoState();
        }
        else if (args.PropertyName == nameof(TaskListViewModel.HasSubtitle) && !ViewModel.HasSubtitle)
        {
            ViewModel.CloseSuggestions();
        }
        else if (args.PropertyName == nameof(TaskListViewModel.IsSuggestionsOpen))
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!ViewModel.HasSubtitle) return;
                (ViewModel.IsSuggestionsOpen ? CloseSuggestionsButton : SuggestionsButton)
                    .Focus(FocusState.Keyboard);
            });
        }
    }

    public static GridLength SuggestionsColumnWidth(bool open) =>
        open ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

    private bool IsSuggestionActionFocused()
    {
        if (!ViewModel.IsSuggestionsOpen || XamlRoot is null) return false;
        for (var element = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
            element is not null; element = VisualTreeHelper.GetParent(element))
        {
            if (ReferenceEquals(element, SuggestionsSidebar)) return true;
        }
        return false;
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        _uiSettings.AnimationsEnabledChanged += OnAnimationsEnabledChanged;
        ApplyListIconPickerState();
    }

    private void OnListIconPickerPointerChanged(object sender, PointerRoutedEventArgs args)
    {
        ApplyListIconPickerState();
    }

    private void OnListIconPickerFocusChanged(object sender, RoutedEventArgs args)
    {
        ApplyListIconPickerState();
    }

    private void ApplyListIconPickerState()
    {
        bool revealed = ListIconPickerButton.IsPointerOver
            || ListIconPickerButton.FocusState is FocusState.Keyboard or FocusState.Programmatic;
        VisualStateManager.GoToState(this,
            revealed ? "ListIconPickerRevealed" : "ListIconPickerIdle",
            useTransitions: false);
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        _uiSettings.AnimationsEnabledChanged -= OnAnimationsEnabledChanged;
    }

    private void OnAnimationsEnabledChanged(UISettings sender, UISettingsAnimationsEnabledChangedEventArgs args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!IsLoaded || ViewModel is null) return;
            if (!_uiSettings.AnimationsEnabled)
            {
                foreach (VisualTransition transition in UndoStates.Transitions)
                    transition.Storyboard?.Stop();
                ApplyUndoState();
                if (IsUndoLoaded)
                {
                    UndoSnackbar.Opacity = ViewModel.IsUndoVisible ? 1 : 0;
                    UndoOffset.Y = 0;
                }
            }
        });
    }

    private void OnCompletionReordering()
    {
        // UX-07 suppresses transitions for ordinary diffs. Completion needs the
        // built-in add/delete/reorder motion while retaining its viewport capture.
        // A selected task can move to a virtualized Completed container. Native
        // transitions then compete with restoring its focus and scroll anchor.
        // Keep that offscreen move instant; visible checkbox moves still animate.
        TasksList.ItemContainerTransitions = _uiSettings.AnimationsEnabled
            && _savedSelectedTask is null ? _savedTransitions : null;
    }

    public static readonly DependencyProperty IsUndoLoadedProperty = DependencyProperty.Register(
        nameof(IsUndoLoaded), typeof(bool), typeof(TaskListView), new PropertyMetadata(false));

    public bool IsUndoLoaded
    {
        get => (bool)GetValue(IsUndoLoadedProperty);
        set => SetValue(IsUndoLoadedProperty, value);
    }

    private void ApplyUndoState()
    {
        if (ViewModel.IsUndoVisible)
        {
            if (!IsUndoLoaded)
            {
                IsUndoLoaded = true;
                FindName(nameof(UndoSnackbar));
                VisualStateManager.GoToState(this, "UndoHidden", useTransitions: false);
            }
            VisualStateManager.GoToState(this, "UndoShown", useTransitions: _uiSettings.AnimationsEnabled);
        }
        else if (IsUndoLoaded)
        {
            VisualStateManager.GoToState(this, "UndoHidden", useTransitions: _uiSettings.AnimationsEnabled);
            if (!_uiSettings.AnimationsEnabled) IsUndoLoaded = false;
        }
    }

    private void OnUndoHideCompleted(object? sender, object args)
    {
        if (!ViewModel.IsUndoVisible) IsUndoLoaded = false;
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

        SynchronizeSelection();
    }

    private void SynchronizeSelection(bool updateDetail = true)
    {
        ViewModel.SetSelection(TasksList.SelectedItems.OfType<TaskItemViewModel>());
        if (updateDetail)
        {
            if (ViewModel.SelectedTask is { } selected)
                TaskOpenRequested?.Invoke(selected);
            else TaskCloseRequested?.Invoke();
        }
        AutomationPeer? peer = FrameworkElementAutomationPeer.FromElement(TasksList)
            ?? FrameworkElementAutomationPeer.CreatePeerForElement(TasksList);
        peer?.RaiseNotificationEvent(AutomationNotificationKind.Other,
            AutomationNotificationProcessing.MostRecent,
            ViewModel.SelectionLabel("Selection_Count"), "Wintodo.Selection");
    }

    internal void SelectContextRow(TaskItemViewModel row)
    {
        if (!TasksList.SelectedItems.Contains(row)) TasksList.SelectedItem = row;
    }

    internal void CaptureDragSelection(TaskItemViewModel row)
    {
        _dragSelection = ViewModel.ActionTargets(row);
    }

    private TaskItemViewModel[] DragSelection(TaskItemViewModel row) =>
        _dragSelection.Contains(row) ? _dragSelection : ViewModel.ActionTargets(row);

    private void OnVisibleItemsUpdating(bool preserveViewport)
    {
        _savedTransitions ??= TasksList.ItemContainerTransitions;
        TasksList.ItemContainerTransitions = null;
        _isSynchronizingSelection = true;
        _tasksScrollViewer ??= FindScrollViewer(TasksList);
        _restoringTaskFocus = true;
        if (_tasksScrollViewer is not null) _tasksScrollViewer.BringIntoViewOnFocusChange = false;
        _savedVerticalOffset = _tasksScrollViewer?.VerticalOffset ?? 0;
        _savedSelectedTask = ViewModel.SelectedTask;
        _savedSelection = ViewModel.SelectedTasks.ToArray();
        _viewportAnchor = null;
        foreach (TaskItemViewModel item in ViewModel.VisibleItems.OfType<TaskItemViewModel>())
        {
            if (ReferenceEquals(item, _savedSelectedTask) || TasksList.ContainerFromItem(item) is not ListViewItem row) continue;
            double y = row.TransformToVisual(TasksList).TransformPoint(new Windows.Foundation.Point()).Y;
            if (row.ActualHeight > 0 && row.ActualWidth > 0
                && y > 4 && y + row.ActualHeight < TasksList.ActualHeight)
            {
                _viewportAnchor = item;
                _viewportAnchorY = y;
                break;
            }
        }
        _savedFocusedTaskUid = null;
        DependencyObject? focused = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        while (focused is not null && !ReferenceEquals(focused, TasksList))
        {
            if (focused is ListViewItem container
                && TasksList.ItemFromContainer(container) is TaskItemViewModel item)
            {
                _savedFocusedTaskUid = item.Uid;
                break;
            }
            focused = VisualTreeHelper.GetParent(focused);
        }
        // Arrow navigation focuses the ListView itself while selecting a row.
        if (ReferenceEquals(focused, TasksList)) _savedFocusedTaskUid = _savedSelectedTask?.Uid;
        if (!preserveViewport)
        {
            _savedVerticalOffset = 0;
            _savedSelectedTask = null;
            _savedSelection = [];
            _savedFocusedTaskUid = null;
            _viewportAnchor = null;
        }
    }

    private void OnVisibleItemsUpdated()
    {
        if (TasksList.ItemsPanelRoot is ItemsStackPanel panel) UpdateStickyGroupHeaders(panel);
        int version = ++_updateVersion;
        string? focusedUid = _savedFocusedTaskUid;
        TaskItemViewModel? anchor = _viewportAnchor;
        double anchorY = _viewportAnchorY;
        TaskItemViewModel[] restored = _savedSelection.Where(ViewModel.VisibleItems.Contains).ToArray();
        foreach (TaskItemViewModel item in TasksList.SelectedItems.OfType<TaskItemViewModel>().ToArray())
            if (!restored.Contains(item)) TasksList.SelectedItems.Remove(item);
        foreach (TaskItemViewModel item in restored)
            if (!TasksList.SelectedItems.Contains(item)) TasksList.SelectedItems.Add(item);
        _isSynchronizingSelection = false;
        SynchronizeSelection(updateDetail: _savedSelection.Length > 0 && restored.Length != _savedSelection.Length);
        DispatcherQueue.TryEnqueue(() =>
        {
            if (version != _updateVersion) return;
            _isSynchronizingSelection = true;
            try
            {
                TasksList.UpdateLayout();
                TaskItemViewModel? focusItem = ViewModel.VisibleItems.OfType<TaskItemViewModel>()
                    .FirstOrDefault(item => item.Uid == focusedUid);
                if (focusItem is not null)
                {
                    _restoringTaskFocus = true;
                    if (_tasksScrollViewer is not null) _tasksScrollViewer.BringIntoViewOnFocusChange = false;
                    if (TasksList.ContainerFromItem(focusItem) is ListViewItem container)
                        container.Focus(FocusState.Programmatic);
                    else
                        TasksList.Focus(FocusState.Programmatic);
                }
                _ = RestoreViewportAfterTransitionAsync(version, anchor, anchorY);
            }
            finally { _isSynchronizingSelection = false; }
        });
    }

    private void OnTaskPanelLoaded(object sender, RoutedEventArgs args) =>
        UpdateStickyGroupHeaders((ItemsStackPanel)sender);

    private void UpdateStickyGroupHeaders(ItemsStackPanel panel)
    {
        // With a hidden active header WinUI can clip the top rows to the height
        // of the upcoming Completed header. Only pin groups that start with a heading.
        panel.AreStickyGroupHeadersEnabled = ViewModel?.TaskGroups is [var first, ..]
            && GroupMetadata(first).Kind != TaskListGroupKind.Hidden;
    }

    private async Task RestoreViewportAfterTransitionAsync(int version, TaskItemViewModel? anchor, double anchorY)
    {
        double offset = _savedVerticalOffset;
        if (_uiSettings.AnimationsEnabled && TasksList.ItemContainerTransitions is not null)
            await Task.Delay(TimeSpan.FromMilliseconds(500));
        if (version != _updateVersion) return;
        _restoringTaskFocus = true;
        if (_tasksScrollViewer is not null)
        {
            _tasksScrollViewer.BringIntoViewOnFocusChange = false;
            _tasksScrollViewer.ChangeView(null, offset, null, disableAnimation: true);
            // ChangeView realizes grouped rows and can revise the virtualizing
            // panel's estimated offsets again. Recheck after each correction;
            // a single correction can leave the anchor several rows away.
            for (int attempt = 0; attempt < 4; attempt++)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200));
                if (version != _updateVersion) return;
                TasksList.UpdateLayout();
                if (anchor is null || TasksList.ContainerFromItem(anchor) is not ListViewItem row)
                {
                    if (anchor is null || !ViewModel.VisibleItems.Contains(anchor)) break;
                    // A grouped move can recycle the anchor. Realize that item
                    // before correcting its position instead of abandoning it.
                    TasksList.ScrollIntoView(anchor, ScrollIntoViewAlignment.Leading);
                    continue;
                }
                double y = row.TransformToVisual(TasksList).TransformPoint(new Windows.Foundation.Point()).Y;
                if (Math.Abs(y - anchorY) < 1) break;
                _tasksScrollViewer.ChangeView(null, _tasksScrollViewer.VerticalOffset + y - anchorY, null, disableAnimation: true);
            }
        }
        TasksList.ItemContainerTransitions = _savedTransitions;
    }
    private void OnTaskBringIntoViewRequested(UIElement sender, BringIntoViewRequestedEventArgs args)
    {
        if (_restoringTaskFocus) args.Handled = true;
    }

    private void OnTaskPointerInteraction(object sender, PointerRoutedEventArgs args)
    {
        ++_updateVersion;
        _restoringTaskFocus = false;
        TasksList.ItemContainerTransitions = _savedTransitions ?? TasksList.ItemContainerTransitions;
        if (_tasksScrollViewer is not null) _tasksScrollViewer.BringIntoViewOnFocusChange = true;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer viewer) return viewer;
            ScrollViewer? found = FindScrollViewer(child);
            if (found is not null) return found;
        }
        return null;
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
            // The custom row template has no ListViewItemPresenter to initiate
            // an item drag. Use element initiation with our grouped drop handler.
            container.CanDrag = true;
            container.DragStarting -= OnTaskContainerDragStarting;
            container.DragStarting += OnTaskContainerDragStarting;
            container.DropCompleted -= OnTaskContainerDropCompleted;
            container.DropCompleted += OnTaskContainerDropCompleted;
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
        if (task is null)
        {
            args.Cancel = true;
            return;
        }

        TaskItemViewModel[] selection = DragSelection(task);
        _draggedTaskUid = selection.Length == 1 && !task.IsCompleted ? task.Uid : null;
        args.Data.SetData(TaskDragFormat, string.Join("\n", selection.Select(item => item.Uid)));
        args.Data.RequestedOperation = DataPackageOperation.Move;
    }

    private void OnDragItemsCompleted(
        ListViewBase sender,
        DragItemsCompletedEventArgs args)
    {
        _draggedTaskUid = null;
        _dragSelection = [];
        HideTaskInsertionIndicator();
    }

    private void OnTaskContainerDragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if (sender is not ListViewItem container
            || TasksList.ItemFromContainer(container) is not TaskItemViewModel task)
        {
            args.Cancel = true;
            return;
        }

        TaskItemViewModel[] selection = DragSelection(task);
        _draggedTaskUid = selection.Length == 1 && !task.IsCompleted ? task.Uid : null;
        args.Data.SetData(TaskDragFormat, string.Join("\n", selection.Select(item => item.Uid)));
        args.Data.RequestedOperation = DataPackageOperation.Move;
    }

    private void OnTaskContainerDropCompleted(UIElement sender, DropCompletedEventArgs args)
    {
        _draggedTaskUid = null;
        _dragSelection = [];
        HideTaskInsertionIndicator();
    }

    // WinUI's built-in reorder path does not support grouped CollectionViewSource.
    // Route the active-row boundary to the VM rather than asking WinUI to reorder.
    private void OnTaskDragOver(object sender, DragEventArgs args)
    {
        if (!TryGetTaskDropTarget(args, out _, out _, out double boundary))
        {
            args.AcceptedOperation = DataPackageOperation.None;
            HideTaskInsertionIndicator();
            args.Handled = true;
            return;
        }

        args.AcceptedOperation = DataPackageOperation.Move;
        args.Handled = true;
        TaskInsertionIndicator.Margin = new Thickness(4, Math.Max(0, boundary), 4, 0);
        VisualStateManager.GoToState(this, "TaskDropAllowed", useTransitions: false);
    }

    private void OnTaskDragLeave(object sender, DragEventArgs args)
    {
        HideTaskInsertionIndicator();
    }

    private async void OnTaskDrop(object sender, DragEventArgs args)
    {
        string? sourceUid = _draggedTaskUid;
        if (sourceUid is null
            || !TryGetTaskDropTarget(args, out TaskItemViewModel? target, out bool after, out _))
        {
            args.AcceptedOperation = DataPackageOperation.None;
            args.Handled = true;
            HideTaskInsertionIndicator();
            return;
        }

        args.AcceptedOperation = DataPackageOperation.Move;
        args.Handled = true;
        HideTaskInsertionIndicator();
        string targetUid = target!.Uid;
        // Let the routed drop finish before rebuilding its containers.
        await Task.Yield();
        await ViewModel.ReorderActiveTaskAsync(sourceUid, targetUid, after);
    }

    private bool TryGetTaskDropTarget(
        DragEventArgs args, out TaskItemViewModel? target, out bool after, out double boundary)
    {
        target = null;
        after = false;
        boundary = 0;
        if (_draggedTaskUid is null || !ViewModel.CanReorderTasks
            || !args.DataView.Contains(TaskDragFormat))
        {
            return false;
        }

        double y = args.GetPosition(TasksList).Y;
        foreach (TaskItemViewModel item in ViewModel.VisibleItems)
        {
            if (TasksList.ContainerFromItem(item) is not ListViewItem container)
            {
                continue;
            }

            double top = container.TransformToVisual(TasksList)
                .TransformPoint(new Windows.Foundation.Point()).Y;
            double bottom = top + container.ActualHeight;
            if (item.IsCompleted)
            {
                // Completed is a separate group, never a reorder destination.
                return target is not null && y < top;
            }
            target = item;
            after = y >= top + container.ActualHeight / 2;
            boundary = after ? bottom : top;
            if (y <= bottom)
            {
                return true;
            }
        }

        // Do not turn the collapsed Completed header into an active-group target.
        return target is not null && y <= boundary + 4;
    }

    private void HideTaskInsertionIndicator()
    {
        VisualStateManager.GoToState(this, "TaskDropIdle", useTransitions: false);
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

        if (IsTextInputFocused() || IsSuggestionActionFocused()) return;
        ViewModel.ToggleSelectedCompletionCommand.Execute(null);
        args.Handled = ViewModel.SelectionCount > 0;
    }

    private void OnOpenDetailAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewModel.SelectedTask is null || IsTextInputFocused() || IsSuggestionActionFocused())
        {
            return;
        }

        TaskOpenRequested?.Invoke(ViewModel.SelectedTask);
        args.Handled = true;
    }

    private void OnTasksListPreviewKeyDown(object sender, KeyRoutedEventArgs args)
    {
        ++_updateVersion;
        _restoringTaskFocus = false;
        TasksList.ItemContainerTransitions = _savedTransitions ?? TasksList.ItemContainerTransitions;
        if (_tasksScrollViewer is not null) _tasksScrollViewer.BringIntoViewOnFocusChange = true;
        if (IsTextInputFocused()) return;
        if (args.Key == VirtualKey.A && (GetKeyState(0x11) & 0x8000) != 0)
        {
            TaskItemViewModel? current = FocusedTask() ?? ViewModel.SelectedTasks.LastOrDefault();
            object? group = ViewModel.TaskGroups.FirstOrDefault(items =>
                ((System.Collections.IEnumerable)items).Cast<object>().Contains(current!));
            _isSynchronizingSelection = true;
            TasksList.SelectedItems.Clear();
            IEnumerable<TaskItemViewModel> items = group is System.Collections.IEnumerable sequence
                ? sequence.Cast<object>().OfType<TaskItemViewModel>()
                : ViewModel.VisibleItems.OfType<TaskItemViewModel>();
            foreach (TaskItemViewModel item in items) TasksList.SelectedItems.Add(item);
            _isSynchronizingSelection = false;
            SynchronizeSelection();
            args.Handled = true;
            return;
        }
        if (args.Key is VirtualKey.Up or VirtualKey.Down && (GetKeyState(0x10) & 0x8000) != 0)
        {
            TaskItemViewModel? current = FocusedTask() ?? ViewModel.SelectedTasks.LastOrDefault();
            _selectionAnchor ??= current;
            if (current is not null && _selectionAnchor is not null)
            {
                int next = Math.Clamp(TasksList.Items.IndexOf(current) + (args.Key == VirtualKey.Down ? 1 : -1),
                    0, TasksList.Items.Count - 1);
                int anchor = TasksList.Items.IndexOf(_selectionAnchor);
                _isSynchronizingSelection = true;
                TasksList.SelectedItems.Clear();
                for (int i = Math.Min(anchor, next); i <= Math.Max(anchor, next); i++)
                    TasksList.SelectedItems.Add(TasksList.Items[i]);
                _isSynchronizingSelection = false;
                SynchronizeSelection();
                TasksList.ScrollIntoView(TasksList.Items[next]);
                TasksList.UpdateLayout();
                (TasksList.ContainerFromIndex(next) as ListViewItem)?.Focus(FocusState.Keyboard);
                args.Handled = true;
                return;
            }
        }
        else _selectionAnchor = null;
        if (ViewModel.SelectedTask is not null && args.Key is VirtualKey.Up or VirtualKey.Down
            && (GetKeyState(0x12) & 0x8000) != 0)
        {
            (args.Key == VirtualKey.Up ? ViewModel.MoveSelectedUpCommand : ViewModel.MoveSelectedDownCommand).Execute(null);
            args.Handled = true;
            return;
        }
        if (args.Key != VirtualKey.Space || ViewModel.SelectionCount == 0 || IsTextInputFocused())
        {
            return;
        }

        ViewModel.ToggleSelectedCompletionCommand.Execute(null);
        args.Handled = true;
    }

    private TaskItemViewModel? FocusedTask()
    {
        DependencyObject? focused = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        while (focused is not null && !ReferenceEquals(focused, TasksList))
        {
            if (focused is ListViewItem row) return TasksList.ItemFromContainer(row) as TaskItemViewModel;
            focused = VisualTreeHelper.GetParent(focused);
        }
        return null;
    }

    private async void OnDueDatePickerRequested()
    {
        TaskItemViewModel[] selection = ViewModel.SelectedTasks.ToArray();
        await Task.Yield();
        var resources = new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader();
        var picker = new CalendarDatePicker
        {
            Header = resources.GetString("Selection_Due"),
            PlaceholderText = resources.GetString("Selection_Pick"),
            Language = Wintodo.Services.DisplayCulture.Current.Name,
            FirstDayOfWeek = (Windows.Globalization.DayOfWeek)ViewModel.FirstDayOfWeek,
        };
        AutomationProperties.SetAutomationId(picker, "Todo.List.SelectionDuePicker");
        var flyout = new Flyout { Content = picker };
        picker.DateChanged += async (_, _) =>
        {
            if (picker.Date is not { } date) return;
            flyout.Hide();
            await ViewModel.SetTasksDueDateAsync(selection, DateOnly.FromDateTime(date.DateTime));
        };
        flyout.ShowAt(TasksList);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetKeyState(int virtualKey);

    private void OnToggleImportanceAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {

        if (IsTextInputFocused() || IsSuggestionActionFocused()) return;
        ViewModel.ToggleSelectedImportanceCommand.Execute(null);
        args.Handled = ViewModel.SelectionCount > 0;
    }

    private void OnToggleMyDayAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {

        if (IsTextInputFocused() || IsSuggestionActionFocused()) return;
        ViewModel.ToggleSelectedMyDayCommand.Execute(null);
        args.Handled = ViewModel.SelectionCount > 0;
    }

    private void OnDeleteAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {

        if (IsTextInputFocused() || IsSuggestionActionFocused()) return;
        ViewModel.DeleteSelectedTaskCommand.Execute(null);
        args.Handled = ViewModel.SelectionCount > 0;
    }

    private void OnUndoAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!IsTextInputFocused() && ViewModel.UndoDeleteCommand.CanExecute(null))
        {
            ViewModel.UndoDeleteCommand.Execute(null);
            args.Handled = true;
        }
    }

    private void OnRenameAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        if (IsSuggestionActionFocused()) return;
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
        if (IsSuggestionActionFocused()) return;
        ViewModel.MoveSelectedUpCommand.Execute(null);
        args.Handled = ViewModel.SelectedTask is not null;
    }

    private void OnMoveDownAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        if (IsSuggestionActionFocused()) return;
        ViewModel.MoveSelectedDownCommand.Execute(null);
        args.Handled = ViewModel.SelectedTask is not null;
    }

    private void OnSelectPreviousAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        if ((GetKeyState(0x10) & 0x8000) == 0) args.Handled = MoveSelection(-1);
    }

    private void OnSelectNextAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        if ((GetKeyState(0x10) & 0x8000) == 0) args.Handled = MoveSelection(1);
    }

    private bool MoveSelection(int offset)
    {
        if (IsTextInputFocused() || IsSuggestionActionFocused() || TasksList.Items.Count == 0)
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
