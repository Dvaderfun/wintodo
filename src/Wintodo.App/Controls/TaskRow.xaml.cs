using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.Windows.ApplicationModel.Resources;
using Windows.System;
using Windows.UI.ViewManagement;
using Wintodo.ViewModels;

namespace Wintodo.Controls;

public sealed partial class TaskRow : UserControl
{
    private static readonly ResourceLoader StringResources = new();
    private readonly UISettings _uiSettings = new();
    private bool _isInitializingMotion;

    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(TaskItemViewModel),
            typeof(TaskRow),
            new PropertyMetadata(null, OnViewModelChanged));

    public TaskRow()
    {
        InitializeComponent();
        RowContextFlyout.Opening += OnContextFlyoutOpening;
        PointerPressed += (_, _) => FindTaskListView().CaptureDragSelection(ViewModel);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public TaskItemViewModel ViewModel
    {
        get => (TaskItemViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public MenuFlyout RowContextFlyout { get; } = new();

    public static bool Not(bool value)
    {
        return !value;
    }

    public static Visibility HasText(string value)
    {
        return string.IsNullOrEmpty(value)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    public static Visibility SecondLineVisibility(string metadata, bool isCompleted)
    {
        return isCompleted ? Visibility.Collapsed : HasText(metadata);
    }

    public static Visibility CompletionLineVisibility(
        string completionDateText,
        bool isCompleted)
    {
        return isCompleted ? HasText(completionDateText) : Visibility.Collapsed;
    }

    public static Visibility InverseBoolToVisibility(bool value)
    {
        return value ? Visibility.Collapsed : Visibility.Visible;
    }

    public static double StarOpacity(bool isImportant, bool isSelected)
    {
        return isImportant || isSelected ? 1 : 0;
    }

    public static double BoolToOpacity(bool value)
    {
        return value ? 1 : 0;
    }

    public static double HoverMarkOpacity(bool isCompleted, bool isPointerOver)
    {
        return !isCompleted && isPointerOver ? 0.4 : 0;
    }

    public static double RowMinHeight(bool isCompact)
    {
        return isCompact ? 36 : 44;
    }

    public static Thickness RowPadding(bool isCompact)
    {
        return isCompact ? new Thickness(8, 4, 8, 4) : new Thickness(12, 8, 12, 8);
    }

    public static string ImportanceGlyph(bool isImportant)
    {
        return isImportant ? "\uE735" : "\uE734";
    }

    public static string ImportanceName(bool isImportant)
    {
        return isImportant
            ? StringResources.GetString("Task_RemoveImportance")
            : StringResources.GetString("Task_MarkImportant");
    }

    public static string CompletionName(bool isCompleted)
    {
        return isCompleted
            ? StringResources.GetString("Task_MarkNotCompleted")
            : StringResources.GetString("Task_MarkCompleted");
    }

    private static void OnViewModelChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        var row = (TaskRow)dependencyObject;
        if (args.OldValue is TaskItemViewModel oldViewModel)
        {
            oldViewModel.PropertyChanged -= row.OnViewModelPropertyChanged;
        }

        if (args.NewValue is TaskItemViewModel newViewModel)
        {
            newViewModel.PropertyChanged += row.OnViewModelPropertyChanged;
            row.ApplySearchHighlight();
            row.ApplyMotionStates(useTransitions: false);
        }
    }

    private void OnViewModelPropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(TaskItemViewModel.Summary)
            or nameof(TaskItemViewModel.HighlightText))
        {
            DispatcherQueue.TryEnqueue(ApplySearchHighlight);
        }
        else if (args.PropertyName == nameof(TaskItemViewModel.IsEditingTitle)
                 && sender is TaskItemViewModel { IsEditingTitle: true } editingTask)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!IsLoaded || !ReferenceEquals(ViewModel, editingTask) || !editingTask.IsEditingTitle) return;
                if (FindName(nameof(TaskTitleEditBox)) is TextBox editor)
                {
                    editor.Focus(FocusState.Programmatic);
                    editor.SelectAll();
                }
            });
        }
        else if (args.PropertyName == nameof(TaskItemViewModel.IsCompleted))
        {
            VisualStateManager.GoToState(this, ViewModel.IsCompleted ? "Completed" : "Active",
                useTransitions: IsLoaded && _uiSettings.AnimationsEnabled);
        }
        else if (args.PropertyName == nameof(TaskItemViewModel.IsImportant))
        {
            VisualStateManager.GoToState(this, ViewModel.IsImportant ? "Important" : "NotImportant",
                useTransitions: IsLoaded && _uiSettings.AnimationsEnabled);
        }
    }

    private void ApplySearchHighlight()
    {
        TaskTitle.TextHighlighters.Clear();
        if (string.IsNullOrWhiteSpace(ViewModel?.HighlightText)
            || string.IsNullOrEmpty(ViewModel.Summary))
        {
            return;
        }

        var highlighter = new TextHighlighter
        {
            Background = (Brush)Application.Current.Resources[
                "AccentFillColorSecondaryBrush"],
        };
        var ranges = new HashSet<(int Start, int Length)>();
        foreach (string token in ViewModel.HighlightText.Split(
                     (char[]?)null,
                     StringSplitOptions.RemoveEmptyEntries
                         | StringSplitOptions.TrimEntries))
        {
            int startIndex = 0;
            while (startIndex < ViewModel.Summary.Length)
            {
                int matchIndex = ViewModel.Summary.IndexOf(
                    token,
                    startIndex,
                    StringComparison.CurrentCultureIgnoreCase);
                if (matchIndex < 0)
                {
                    break;
                }

                ranges.Add((matchIndex, token.Length));
                startIndex = matchIndex + token.Length;
            }
        }

        foreach ((int start, int length) in ranges.OrderBy(range => range.Start))
        {
            highlighter.Ranges.Add(new TextRange
            {
                StartIndex = start,
                Length = length,
            });
        }

        if (highlighter.Ranges.Count > 0)
        {
            TaskTitle.TextHighlighters.Add(highlighter);
        }
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs args)
    {
        VisualStateManager.GoToState(this, "PointerOver", useTransitions: true);
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs args)
    {
        VisualStateManager.GoToState(this, "PointerNormal", useTransitions: true);
    }

    private void OnTaskTitleEditBoxKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Enter
            && ViewModel.CommitTitleEditCommand.CanExecute(null))
        {
            ViewModel.CommitTitleEditCommand.Execute(null);
            args.Handled = true;
        }
        else if (args.Key == VirtualKey.Escape)
        {
            ViewModel.CancelTitleEditCommand.Execute(null);
            args.Handled = true;
        }
    }

    private void OnTaskTitleEditBoxLostFocus(object sender, RoutedEventArgs args)
    {
        // An unloaded editor can lose focus after the row has started another edit.
        if (ReferenceEquals(sender, TaskTitleEditBox) && ViewModel.IsEditingTitle
            && ViewModel.CommitTitleEditCommand.CanExecute(null))
        {
            ViewModel.CommitTitleEditCommand.Execute(null);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        _uiSettings.AnimationsEnabledChanged += OnAnimationsEnabledChanged;
        ApplyMotionStates(useTransitions: false);
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        _uiSettings.AnimationsEnabledChanged -= OnAnimationsEnabledChanged;
        ResetMotion();
    }

    private void OnAnimationsEnabledChanged(UISettings sender, UISettingsAnimationsEnabledChangedEventArgs args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!IsLoaded) return;
            ResetMotion();
            ApplyMotionStates(useTransitions: false);
        });
    }

    private void ApplyMotionStates(bool useTransitions)
    {
        if (ViewModel is null) return;
        _isInitializingMotion = !useTransitions;
        VisualStateManager.GoToState(this, ViewModel.IsCompleted ? "Completed" : "Active", useTransitions);
        VisualStateManager.GoToState(this, ViewModel.IsImportant ? "Important" : "NotImportant", useTransitions);
        _isInitializingMotion = false;
        // Recycling must not carry a running spring into another task.
        if (!useTransitions) ResetMotion();
        TaskTitle.TextDecorations = ViewModel.IsCompleted
            ? Windows.UI.Text.TextDecorations.Strikethrough : Windows.UI.Text.TextDecorations.None;
    }

    private void ResetMotion()
    {
        foreach (VisualTransition transition in CompletionStates.Transitions)
            transition.Storyboard?.Stop();
        if (ViewModel is not null)
        {
            TaskContent.Opacity = ViewModel.IsCompleted ? 0.6 : 1;
            CompletionGlyph.Opacity = ViewModel.IsCompleted ? 1 : 0;
        }
        foreach (UIElement element in new UIElement[] { CompletionCircle, ImportanceButton })
        {
            Visual visual = ElementCompositionPreview.GetElementVisual(element);
            visual.StopAnimation("Scale");
            visual.Scale = Vector3.One;
        }
    }

    private void OnCompletionStateChanging(object sender, VisualStateChangedEventArgs args)
    {
        // State setters can wait for the transition; strike the title immediately.
        TaskTitle.TextDecorations = ViewModel.IsCompleted
            ? Windows.UI.Text.TextDecorations.Strikethrough : Windows.UI.Text.TextDecorations.None;
        if (_isInitializingMotion || !IsLoaded || args.OldState is null || !_uiSettings.AnimationsEnabled)
        {
            return;
        }

        // Animate only the decoration; the checkbox retains its 32x32 hit target.
        Visual visual = ElementCompositionPreview.GetElementVisual(CompletionCircle);
        visual.CenterPoint = new Vector3(10, 10, 0);
        SpringVector3NaturalMotionAnimation animation = visual.Compositor.CreateSpringVector3Animation();
        animation.InitialValue = new Vector3(0.8f, 0.8f, 1);
        animation.FinalValue = Vector3.One;
        animation.DampingRatio = 0.6f;
        animation.Period = TimeSpan.FromMilliseconds(40);
        visual.StartAnimation("Scale", animation);
    }

    private void OnImportanceStateChanging(object sender, VisualStateChangedEventArgs args)
    {
        if (_isInitializingMotion || !IsLoaded || args.OldState is null || !_uiSettings.AnimationsEnabled)
        {
            return;
        }

        Visual visual = ElementCompositionPreview.GetElementVisual(ImportanceButton);
        visual.CenterPoint = new Vector3(16, 16, 0);
        Vector3KeyFrameAnimation animation = visual.Compositor.CreateVector3KeyFrameAnimation();
        animation.Duration = TimeSpan.FromMilliseconds(200);
        animation.InsertKeyFrame(0, Vector3.One);
        animation.InsertKeyFrame(0.5f, new Vector3(1.25f, 1.25f, 1));
        animation.InsertKeyFrame(1, Vector3.One);
        visual.StartAnimation("Scale", animation);
    }

    private void OnContextFlyoutOpening(object? sender, object args)
    {
        TaskListView host = FindTaskListView();
        host.SelectContextRow(ViewModel);
        TaskListViewModel list = host.ViewModel;
        RowContextFlyout.Items.Clear();
        MenuFlyoutItem Action(string key, string id, System.Windows.Input.ICommand command, object? parameter = null)
        {
            var item = new MenuFlyoutItem
            {
                Text = list.SelectionLabel(key),
                Command = command,
                CommandParameter = parameter,
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(item, id);
            return item;
        }

        RowContextFlyout.Items.Add(Action(list.SelectedTasks.All(item => item.IsCompleted)
            ? "Selection_Uncomplete" : "Selection_Complete", "Todo.Row.Selection.Complete",
            list.ToggleSelectedCompletionCommand));
        RowContextFlyout.Items.Add(Action(list.SelectedTasks.All(item => item.IsImportant)
            ? "Selection_Unimportant" : "Selection_Important", "Todo.Row.Selection.Important",
            list.ToggleSelectedImportanceCommand));
        RowContextFlyout.Items.Add(Action(list.SelectionIsInMyDay
            ? "Selection_RemoveMyDay" : "Selection_MyDay", "Todo.Row.Selection.MyDay",
            list.ToggleSelectedMyDayCommand));
        var dueSubItem = new MenuFlyoutSubItem { Text = list.SelectionLabel("Selection_Due") };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(dueSubItem, "Todo.Row.Selection.Due");
        foreach (string choice in new[] { "Today", "Tomorrow", "Pick" })
            dueSubItem.Items.Add(Action("Selection_" + choice, "Todo.Row.Selection.Due." + choice,
                list.SetSelectionDueCommand, choice));
        RowContextFlyout.Items.Add(dueSubItem);
        var moveSubItem = new MenuFlyoutSubItem
        {
            Text = list.SelectionLabel("Selection_Move"),
            Icon = new FontIcon { Glyph = "\uE8DE" },
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(moveSubItem, "Todo.Row.Selection.Move");
        foreach (MoveTargetViewModel target in list.MoveTargets)
        {
            if (list.SelectedTasks.All(item => item.ListId == target.ListId)) continue;
            var item = new MenuFlyoutItem
            {
                Text = target.DisplayName,
                Command = list.MoveSelectionCommand,
                CommandParameter = target.ListId,
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(item, target.AutomationId);
            moveSubItem.Items.Add(item);
        }
        RowContextFlyout.Items.Add(moveSubItem);
        RowContextFlyout.Items.Add(new MenuFlyoutSeparator());
        var deleteItem = Action("Selection_Delete", $"Todo.Row.Delete.{ViewModel.Uid}", list.DeleteSelectedTaskCommand);
        deleteItem.Icon = new FontIcon { Glyph = "\uE74D" };
        RowContextFlyout.Items.Add(deleteItem);
    }

    private TaskListView FindTaskListView()
    {
        DependencyObject? current = this;
        while (current is not null)
        {
            if (current is TaskListView taskList) return taskList;
            current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current);
        }
        throw new InvalidOperationException("TaskRow is not hosted by TaskListView.");
    }

}
