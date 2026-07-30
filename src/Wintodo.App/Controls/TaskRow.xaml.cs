using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Windows.ApplicationModel.Resources;
using Wintodo.ViewModels;

namespace Wintodo.Controls;

public sealed partial class TaskRow : UserControl
{
    private static readonly ResourceLoader StringResources = new();

    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(TaskItemViewModel),
            typeof(TaskRow),
            new PropertyMetadata(null));

    public TaskRow()
    {
        InitializeComponent();
        RowContextFlyout.Opening += OnContextFlyoutOpening;
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

    public static Visibility BoolToVisibility(bool value)
    {
        return value ? Visibility.Visible : Visibility.Collapsed;
    }

    public static double StarOpacity(bool isImportant)
    {
        return isImportant ? 1 : 0;
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

    private void OnPointerEntered(object sender, PointerRoutedEventArgs args)
    {
        VisualStateManager.GoToState(this, "PointerOver", useTransitions: true);
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs args)
    {
        VisualStateManager.GoToState(this, "PointerNormal", useTransitions: true);
    }

    private void OnContextFlyoutOpening(object? sender, object args)
    {
        RowContextFlyout.Items.Clear();
        var moveSubItem = new MenuFlyoutSubItem
        {
            Text = StringResources.GetString("Task_MoveTo"),
            Icon = new FontIcon { Glyph = "\uE8DE" },
        };
        foreach (MoveTargetViewModel target in FindTaskListViewModel().MoveTargets)
        {
            if (target.ListId == ViewModel.ListId)
            {
                continue;
            }

            var item = new MenuFlyoutItem
            {
                Text = target.DisplayName,
                Command = ViewModel.MoveCommand,
                CommandParameter = target.ListId,
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(
                item,
                target.AutomationId);
            moveSubItem.Items.Add(item);
        }

        RowContextFlyout.Items.Add(moveSubItem);
        RowContextFlyout.Items.Add(new MenuFlyoutSeparator());
        var deleteItem = new MenuFlyoutItem
        {
            Text = StringResources.GetString("Task_Delete"),
            Icon = new FontIcon { Glyph = "\uE74D" },
            Command = ViewModel.DeleteCommand,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(
            deleteItem,
            $"Todo.Row.Delete.{ViewModel.Uid}");
        RowContextFlyout.Items.Add(deleteItem);
    }

    private TaskListViewModel FindTaskListViewModel()
    {
        DependencyObject? current = this;
        while (current is not null)
        {
            if (current is TaskListView taskList)
            {
                return taskList.ViewModel;
            }

            current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current);
        }

        throw new InvalidOperationException("TaskRow is not hosted by TaskListView.");
    }

}
