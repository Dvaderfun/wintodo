using CommunityToolkit.Mvvm.Input;
using Wintodo.Core.Models;
using Wintodo.Core.Queries;
using Wintodo.Services;

namespace Wintodo.ViewModels;

public sealed partial class TaskListViewModel
{
    [RelayCommand]
    private void BeginComposer()
    {
        if (CanCreateTask)
        {
            IsComposerExpanded = true;
        }
    }

    [RelayCommand]
    private void CancelComposer()
    {
        ResetComposer();
    }

    private bool CanAddTask()
    {
        return CanCreateTask && !string.IsNullOrWhiteSpace(NewTaskTitle);
    }

    [RelayCommand(CanExecute = nameof(CanAddTask))]
    private async Task AddTaskAsync()
    {
        await Task.Yield();
        long? listId = ComposerTargetListId;
        if (listId is null)
        {
            return;
        }

        DateTimeOffset? reminder = _composerReminder;
        var task = new TodoTask
        {
            ListId = listId.Value,
            Summary = NewTaskTitle.Trim(),
            Due = _composerDue,
            DueIsDateOnly = _composerDue is not null,
            RRule = _composerRecurrence,
            Priority = _composerImportant
                ? (int)TaskPriority.High
                : (int)TaskPriority.Undefined,
            MyDayDate = _composerMyDay ? _clock.Today : null,
        };
        TodoTask created = await _taskStore.CreateTaskAsync(task, addToTop: _preferences.AddNewToTop);
        if (reminder is not null)
        {
            await _taskStore.SetAlarmAsync(
                created.Uid,
                new TaskAlarm
                {
                    Action = AlarmAction.Display,
                    Trigger = reminder,
                    Description = created.Summary,
                });
        }

        ResetComposer(collapse: false);
        TaskItemViewModel item = GetOrCreateItem(created);
        UpdateItemDisplay(item);
        if (_currentQuery?.SortMode == SortMode.Manual)
        {
            InsertManualItem(_activeTasks, item);
        }
        else
        {
            _activeTasks.Add(item);
        }
        RebuildVisibleItems();
    }

    [RelayCommand]
    private void SetComposerList(long listId)
    {
        ShellListItemViewModel? target =
            _lists.FirstOrDefault(list => list.Id == listId);
        if (target is null)
        {
            return;
        }

        _composerListOverrideId = target.Id;
        ComposerTargetListId = target.Id;
        ComposerTargetLabel = target.DisplayName;
        CanCreateTask = IsComposerVisible;
    }

    [RelayCommand]
    private void SetComposerDue(string value)
    {
        _composerDue = value switch
        {
            "Today" => StartOfLocalDay(_clock.Today),
            "Tomorrow" => StartOfLocalDay(_clock.Today.AddDays(1)),
            _ => null,
        };
        UpdateComposerLabels();
    }

    [RelayCommand]
    private void SetComposerReminder(string value)
    {
        DateTimeOffset now = _clock.UtcNow.ToLocalTime();
        _composerReminder = value switch
        {
            "LaterToday" => now.AddHours(3),
            "TomorrowMorning" => new DateTimeOffset(
                _clock.Today.AddDays(1).ToDateTime(TimeOnly.FromTimeSpan(_preferences.DefaultReminderTime)),
                TimeZoneInfo.Local.GetUtcOffset(
                    _clock.Today.AddDays(1).ToDateTime(TimeOnly.FromTimeSpan(_preferences.DefaultReminderTime)))),
            _ => null,
        };
        UpdateComposerLabels();
    }

    [RelayCommand]
    private void SetComposerRepeat(string value)
    {
        _composerRecurrence = value switch
        {
            "Daily" => "FREQ=DAILY",
            "Weekly" => "FREQ=WEEKLY",
            _ => null,
        };
        if (_composerRecurrence is not null) _composerDue ??= StartOfLocalDay(_clock.Today);
        UpdateComposerLabels();
    }

    private void ResetComposer(bool collapse = true)
    {
        NewTaskTitle = string.Empty;
        _composerReminder = null;
        _composerRecurrence = null;
        _composerDue = _composerKind == ListQueryKind.Planned
            ? StartOfLocalDay(_clock.Today)
            : null;
        _composerImportant = _composerKind == ListQueryKind.Important;
        _composerMyDay = _composerKind == ListQueryKind.MyDay;
        _composerListOverrideId = null;
        if (collapse)
        {
            IsComposerExpanded = false;
        }

        UpdateComposerLabels();
        ResolveComposerTarget();
    }

    private void ResolveComposerTarget()
    {
        if (!IsComposerVisible)
        {
            IsComposerTargetVisible = false;
            CanCreateTask = false;
            return;
        }

        if (_composerKind == ListQueryKind.UserList)
        {
            ComposerTargetListId = _selectedList?.Id;
            ComposerTargetLabel = _selectedList?.DisplayName ?? string.Empty;
            IsComposerTargetVisible = false;
        }
        else
        {
            ShellListItemViewModel? target =
                _lists.FirstOrDefault(list => list.Id == _composerListOverrideId)
                ?? _lists.FirstOrDefault(list => list.IsDefault)
                ?? _lists.FirstOrDefault();
            ComposerTargetListId = target?.Id;
            ComposerTargetLabel = target?.DisplayName ?? string.Empty;
            IsComposerTargetVisible = target is not null;
        }

        CanCreateTask = ComposerTargetListId is not null;
    }

    private void UpdateComposerLabels()
    {
        HasComposerDue = _composerDue is not null;
        HasComposerReminder = _composerReminder is not null;
        HasComposerRepeat = _composerRecurrence is not null;
        ComposerDueLabel = _composerDue is null
            ? _resources.GetString("Composer_DueDate")
            : FormatDueDate(DateOnly.FromDateTime(_composerDue.Value.DateTime));
        ComposerReminderLabel = _composerReminder is null
            ? _resources.GetString("Composer_RemindMe")
            : FormatReminder(_composerReminder.Value);
        ComposerRepeatLabel = _composerRecurrence switch
        {
            "FREQ=DAILY" => _resources.GetString("Composer_Daily"),
            "FREQ=WEEKLY" => _resources.GetString("Composer_Weekly"),
            _ => _resources.GetString("Composer_Repeat"),
        };
    }

    private string FormatReminder(DateTimeOffset reminder)
    {
        DateTime local =
            TimeZoneInfo.ConvertTime(reminder, _clock.LocalTimeZone).DateTime;
        string time = local.ToString("t", DisplayCulture.Current);
        DateOnly reminderDate = DateOnly.FromDateTime(local);
        return reminderDate == _clock.Today
            ? time
            : $"{FormatDueDate(reminderDate)} {time}";
    }

}
