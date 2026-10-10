using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.ApplicationModel.Resources;
using Microsoft.Windows.Storage.Pickers;
using Windows.Storage;
using Windows.System;
using Wintodo.ViewModels;
using Wintodo.Services;

namespace Wintodo.Controls;

public sealed partial class TaskDetailView : UserControl
{
    private static readonly ResourceLoader StringResources = new();

    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(TaskDetailViewModel),
            typeof(TaskDetailView),
            new PropertyMetadata(null, OnViewModelChanged));

    public TaskDetailView()
    {
        InitializeComponent();
        int count = Interlocked.Increment(ref _instanceCount);
        Trace.WriteLine($"WINTODO_DETAIL_INSTANCE_COUNT={count}");
    }

    private static int _instanceCount;

    internal static int InstanceCount => Volatile.Read(ref _instanceCount);

    public TaskDetailViewModel ViewModel
    {
        get => (TaskDetailViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    internal void OpenDuePicker()
    {
        FlyoutBase.ShowAttachedFlyout(DueButton);
    }

    internal void OpenReminderPicker()
    {
        FlyoutBase.ShowAttachedFlyout(ReminderButton);
    }

    public static bool Not(bool value)
    {
        return !value;
    }

    public static Visibility BoolToVisibility(bool value)
    {
        return value ? Visibility.Visible : Visibility.Collapsed;
    }

    public static Visibility HasValue(DateTimeOffset? value)
    {
        return value is null ? Visibility.Collapsed : Visibility.Visible;
    }

    public static Visibility CustomRepeatVisibility(int selectedIndex)
    {
        return selectedIndex == 5 ? Visibility.Visible : Visibility.Collapsed;
    }

    public static Visibility WeeklyOptionsVisibility(int repeatModeIndex, int periodIndex)
    {
        return repeatModeIndex == 5 && periodIndex == 1
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public static Visibility RepeatClearVisibility(int selectedIndex)
    {
        return selectedIndex >= 0 ? Visibility.Visible : Visibility.Collapsed;
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

    public static string StepCompletionName(bool isCompleted)
    {
        return isCompleted
            ? StringResources.GetString("Detail_MarkStepNotCompleted")
            : StringResources.GetString("Detail_MarkStepCompleted");
    }

    public static string DeleteStepName(string summary)
    {
        return string.Format(
            DisplayCulture.Current,
            StringResources.GetString("Detail_DeleteStep"),
            summary);
    }

    public static string RemoveAttachmentName(string fileName)
    {
        return string.Format(
            DisplayCulture.Current,
            StringResources.GetString("Detail_RemoveAttachment"),
            fileName);
    }

    private static void OnViewModelChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        var view = (TaskDetailView)dependencyObject;
        if (args.OldValue is TaskDetailViewModel oldViewModel)
        {
            oldViewModel.ShowDuePickerRequested -= view.OnShowDuePickerRequested;
            oldViewModel.ShowReminderPickerRequested -= view.OnShowReminderPickerRequested;
            oldViewModel.AddAttachmentRequested -= view.OnAddAttachmentRequested;
        }

        if (args.NewValue is TaskDetailViewModel newViewModel)
        {
            newViewModel.ShowDuePickerRequested += view.OnShowDuePickerRequested;
            newViewModel.ShowReminderPickerRequested += view.OnShowReminderPickerRequested;
            newViewModel.AddAttachmentRequested += view.OnAddAttachmentRequested;
        }
    }

    public static Windows.Globalization.DayOfWeek ToCalendarDay(DayOfWeek day) => (Windows.Globalization.DayOfWeek)day;

    private void OnShowDuePickerRequested()
    {
        FlyoutBase.ShowAttachedFlyout(DueButton);
    }

    private void OnShowReminderPickerRequested()
    {
        FlyoutBase.ShowAttachedFlyout(ReminderButton);
    }

    private async void OnAddAttachmentRequested()
    {
        try
        {
            var picker = new FileOpenPicker(App.Window.AppWindow.Id);
            picker.FileTypeFilter.Add("*");
            PickFileResult? result = await picker.PickSingleFileAsync();
            if (result is not null)
            {
                StorageFile file =
                    await StorageFile.GetFileFromPathAsync(result.Path);
                await ViewModel.AttachFileAsync(file);
            }
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"WINTODO_ATTACHMENT_PICKER_EXCEPTION={exception}");
            ViewModel.ReportError(
                StringResources.GetString("Detail_AttachmentPickerFailed"));
        }
    }

    private void OnNewStepKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter
            || !ViewModel.AddStepCommand.CanExecute(null))
        {
            return;
        }

        ViewModel.AddStepCommand.Execute(null);
        args.Handled = true;
    }

    private void OnStepContainerContentChanging(
        ListViewBase sender,
        ContainerContentChangingEventArgs args)
    {
        if (args.ItemContainer is not ListViewItem container
            || args.Item is not StepItemViewModel step)
        {
            return;
        }

        AutomationProperties.SetAutomationId(container, step.AutomationId);
        AutomationProperties.SetName(container, step.Summary);
        // Let the ListView own drag initiation and its reorder drop target.
    }

    private void OnStepDragItemsStarting(
        object sender,
        DragItemsStartingEventArgs args)
    {
        if (!args.Items.OfType<StepItemViewModel>().Any())
        {
            args.Cancel = true;
            return;
        }

        args.Data.RequestedOperation =
            Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
    }

    private async void OnStepDragItemsCompleted(
        ListViewBase sender,
        DragItemsCompletedEventArgs args)
    {
        if (args.DropResult != Windows.ApplicationModel.DataTransfer.DataPackageOperation.None)
        {
            await ViewModel.PersistStepOrderAsync();
        }
    }

    private void OnRepeatFlyoutOpened(object sender, object args)
    {
        if (sender is not Flyout flyout)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() => AssignRepeatAutomationIds(flyout));
    }

    private static void AssignRepeatAutomationIds(Flyout flyout)
    {
        if (flyout.Content is not DependencyObject content)
        {
            return;
        }

        string[] automationIds =
        [
            "Todo.Detail.RepeatOption.Daily",
            "Todo.Detail.RepeatOption.Weekdays",
            "Todo.Detail.RepeatOption.Weekly",
            "Todo.Detail.RepeatOption.Monthly",
            "Todo.Detail.RepeatOption.Yearly",
            "Todo.Detail.RepeatOption.Custom",
        ];
        IReadOnlyList<RadioButton> buttons = FindDescendants<RadioButton>(content);
        for (int index = 0; index < Math.Min(buttons.Count, automationIds.Length); index++)
        {
            AutomationProperties.SetAutomationId(buttons[index], automationIds[index]);
        }
    }

    private void OnCustomPeriodDropDownOpened(object sender, object args)
    {
        if (sender is not ComboBox comboBox)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() => AssignCustomPeriodAutomationIds(comboBox));
    }

    private static void AssignCustomPeriodAutomationIds(ComboBox comboBox)
    {
        for (int index = 0; index < comboBox.Items.Count; index++)
        {
            if (comboBox.ContainerFromIndex(index) is ComboBoxItem item)
            {
                AutomationProperties.SetAutomationId(
                    item,
                    $"Todo.Detail.CustomPeriod.{index}");
            }
        }
    }

    private static IReadOnlyList<T> FindDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        var matches = new List<T>();
        AddDescendants(root, matches);
        return matches;
    }

    private static void AddDescendants<T>(DependencyObject root, List<T> matches)
        where T : DependencyObject
    {
        int childCount = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < childCount; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                matches.Add(match);
            }

            AddDescendants(child, matches);
        }
    }
}
