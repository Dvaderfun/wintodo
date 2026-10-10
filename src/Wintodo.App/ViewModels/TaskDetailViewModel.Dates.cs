using CommunityToolkit.Mvvm.Input;
using Wintodo.Core.Models;
using Wintodo.Core.Presentation;
using Wintodo.Services;

namespace Wintodo.ViewModels;

public sealed partial class TaskDetailViewModel
{
    [RelayCommand]
    private async Task SetDueQuickAsync(string option)
    {
        if (_model is null)
        {
            return;
        }

        DateOnly? date = option switch
        {
            "Today" => _clock.Today,
            "Tomorrow" => _clock.Today.AddDays(1),
            "NextWeek" => _clock.Today.AddDays(7),
            _ => null,
        };
        if (date is null)
        {
            ShowDuePickerRequested?.Invoke();
            return;
        }

        _model.Due = StartOfLocalDay(date.Value);
        _model.DueIsDateOnly = true;
        PendingDueDate = _model.Due;
        PendingDueTime = TimeSpan.Zero;
        UpdateActionLabels();
        await PersistModelAsync();
    }

    [RelayCommand]
    private async Task ApplyCustomDueAsync()
    {
        if (_model is null || PendingDueDate is null)
        {
            return;
        }

        _model.Due = CombineLocal(PendingDueDate.Value, PendingDueTime);
        _model.DueIsDateOnly = PendingDueTime == TimeSpan.Zero;
        UpdateActionLabels();
        await PersistModelAsync();
    }

    [RelayCommand]
    private async Task ClearDueAsync()
    {
        if (_model is null)
        {
            return;
        }

        if (_model.Due is null) return;
        var states = await CaptureUndoAsync(Core.Services.TaskUndoFields.Due);
        _model.Due = null;
        _model.DueIsDateOnly = false;
        _model.DueTzid = null;
        PendingDueDate = null;
        PendingDueTime = TimeSpan.Zero;
        UpdateActionLabels();
        if (await PersistModelAsync()) RecordUndo(states, "Undo_DueCleared");
    }

    [RelayCommand]
    private async Task SetReminderQuickAsync(string option)
    {
        if (_model is null)
        {
            return;
        }

        DateTimeOffset localNow = _clock.UtcNow.ToLocalTime();
        DateTimeOffset? reminder = option switch
        {
            "Today" => localNow.AddHours(3),
            "Tomorrow" => CombineLocal(
                StartOfLocalDay(_clock.Today.AddDays(1)),
                _preferences.DefaultReminderTime),
            "NextWeek" => CombineLocal(
                StartOfLocalDay(_clock.Today.AddDays(7)),
                _preferences.DefaultReminderTime),
            _ => null,
        };
        if (reminder is null)
        {
            ShowReminderPickerRequested?.Invoke();
            return;
        }

        SetReminder(reminder.Value);
        UpdateActionLabels();
        await PersistModelAsync();
    }

    [RelayCommand]
    private async Task ApplyCustomReminderAsync()
    {
        if (_model is null || PendingReminderDate is null)
        {
            return;
        }

        SetReminder(CombineLocal(PendingReminderDate.Value, PendingReminderTime));
        UpdateActionLabels();
        await PersistModelAsync();
    }

    [RelayCommand]
    private async Task ClearReminderAsync()
    {
        if (_model is null)
        {
            return;
        }

        if (_model.Alarms.Count == 0) return;
        var states = await CaptureUndoAsync(Core.Services.TaskUndoFields.Reminder);
        _model.Alarms.Clear();
        PendingReminderDate = null;
        PendingReminderTime = _preferences.DefaultReminderTime;
        UpdateActionLabels();
        if (await PersistModelAsync()) RecordUndo(states, "Undo_ReminderCleared");
    }

    private void UpdateActionLabels()
    {
        if (_model is null)
        {
            return;
        }

        MyDayLabel = _model.MyDayDate is null
            ? _resources.GetString("Detail_AddToMyDay")
            : _resources.GetString("Detail_AddedToMyDay");
        DueLabel = _model.Due is null
            ? _resources.GetString("Detail_AddDueDate")
            : string.Format(
                DisplayCulture.Current,
                _resources.GetString("Detail_Due"),
                FormatDate(_model.Due.Value));

        TaskAlarm? reminder = _model.Alarms.FirstOrDefault(alarm => !alarm.IsAcknowledged);
        ReminderLabel = reminder?.Trigger is null
            ? _resources.GetString("Detail_RemindMe")
            : string.Format(
                DisplayCulture.Current,
                _resources.GetString("Detail_Reminder"),
                reminder.Trigger.Value.ToLocalTime());
        RepeatLabel = FormatRepeat(_model.RRule);
    }

    private string FormatDate(DateTimeOffset value)
    {
        DateOnly date = DateOnly.FromDateTime(value.LocalDateTime);
        return TaskDateLabels.FormatDetailDue(
            date,
            _clock,
            DisplayCulture.Current,
            _resources.GetString("Task_Metadata_Today"),
            _resources.GetString("Task_Metadata_Tomorrow"),
            _resources.GetString("Detail_NextWeek"));
    }

    private void SetReminder(DateTimeOffset value)
    {
        _model!.Alarms.Clear();
        _model.Alarms.Add(new TaskAlarm
        {
            Action = AlarmAction.Display,
            Trigger = value,
            Description = _model.Summary,
        });
        PendingReminderDate = value;
        PendingReminderTime = value.TimeOfDay;
    }

    private static DateTimeOffset StartOfLocalDay(DateOnly date)
    {
        DateTime local = date.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    private static DateTimeOffset CombineLocal(DateTimeOffset date, TimeSpan time)
    {
        DateTime local = date.LocalDateTime.Date + time;
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

}
