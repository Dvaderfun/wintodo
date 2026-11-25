using System.Collections.ObjectModel;
using System.Diagnostics;
using Wintodo.Core.Models;
using Wintodo.Core.Presentation;
using Wintodo.Services;
using Wintodo.Core.Services;

namespace Wintodo.ViewModels;

public sealed partial class TaskDetailViewModel
{
    internal async Task<bool> LoadAsync(string uid, CancellationToken cancellationToken = default)
    {
        int loadVersion = ++_loadVersion;
        int revision;
        int attachmentRevision;
        int stepRevision;
        Task<TodoTask?> taskRequest;
        Task<IReadOnlyList<TodoTask>> stepsRequest;
        do
        {
            if (!await FlushPendingEditsAsync() || loadVersion != _loadVersion) return false;
            cancellationToken.ThrowIfCancellationRequested();
            revision = _textSaves.Revision;
            attachmentRevision = _attachmentRevision;
            stepRevision = _stepRevision;
            taskRequest = _taskStore.GetTaskAsync(uid, cancellationToken);
            stepsRequest = _taskStore.GetChildrenAsync(uid, cancellationToken);
            await Task.WhenAll(taskRequest, stepsRequest);
            cancellationToken.ThrowIfCancellationRequested();
            if (loadVersion != _loadVersion) return false;
            // The old editor was still available while SQLite read the new selection.
        } while (HasPendingEdits || attachmentRevision != _attachmentRevision || stepRevision != _stepRevision
            || (uid == _model?.Uid && revision != _textSaves.Revision));

        TodoTask task = await taskRequest
            ?? throw new KeyNotFoundException($"Task '{uid}' was not found.");
        if (_model?.Uid != task.Uid) IsErrorOpen = false;
        _model = task;
        RestoreStepDraft(task.Uid);
        ApplyModelToEditor(task);

        var steps = new ObservableCollection<object>();
        foreach (TodoTask step in await stepsRequest)
        {
            steps.Add(CreateStepViewModel(step));
        }
        Steps = steps;

        RebuildAttachments();
        OnPropertyChanged(nameof(Uid));
        return true;
    }

    private void ApplyModelToEditor(TodoTask task)
    {
        _isLoading = true;
        Summary = task.Summary;
        Note = task.Description ?? string.Empty;
        IsCompleted = task.Status == Wintodo.Core.Models.TaskStatus.Completed;
        IsImportant = task.Priority is >= 1 and <= 4;
        CreatedText = TaskDateLabels.FormatCreated(
            task.Created.ToLocalTime(),
            _resources.GetString("Detail_Created"),
            DisplayCulture.Current);

        RefreshPendingDueDate(task);
        RefreshPendingReminder(task);
        ConfigureRepeatEditor(task.RRule);
        UpdateActionLabels();
        _isLoading = false;
    }

    private void RefreshPendingReminder(TodoTask task)
    {
        TaskAlarm? reminder = task.Alarms.FirstOrDefault(alarm => !alarm.IsAcknowledged);
        PendingReminderDate = reminder?.Trigger?.ToLocalTime();
        PendingReminderTime = PendingReminderDate?.TimeOfDay ?? _preferences.DefaultReminderTime;
    }

    internal bool HasPendingEdits => _textSaves.HasPendingEdits;

    internal Task<bool> FlushPendingEditsAsync() => _textSaves.FlushAsync();

    private void ScheduleSave(TimeSpan delay, TaskDetailFields fields)
    {
        if (_model is { } owner)
        {
            _textSaves.Schedule(delay, fields, pending => PersistFieldsAsync(owner, pending));
        }
    }

    private async Task<bool> PersistFieldsAsync(TodoTask current, TaskDetailFields fields, long? removedAttachmentId = null)
    {
        var edits = new TodoTask
        {
            Uid = current.Uid, Summary = current.Summary, Description = current.Description,
            Due = current.Due, DueIsDateOnly = current.DueIsDateOnly, DueTzid = current.DueTzid,
            RRule = current.RRule, Priority = current.Priority,
        };
        if (fields.HasFlag(TaskDetailFields.Reminder)) edits.Alarms.AddRange(current.Alarms.Select(CloneAlarm));
        var pendingDue = (PendingDueDate, PendingDueTime);
        await _saveGate.WaitAsync();
        try
        {
            TodoTask saved = await _taskStore.UpdateTaskDetailAsync(edits, fields, _preferences.MoveStarredToTop, removedAttachmentId);
            if (ReferenceEquals(_model, current))
            {
                // The user may already be typing the next edit; never replace the editor with this snapshot.
                current.Sequence = saved.Sequence;
                current.LastModified = saved.LastModified;
                if (fields.HasFlag(TaskDetailFields.Importance)) current.SortOrder = saved.SortOrder;
                if (fields.HasFlag(TaskDetailFields.Reminder)) CopyAlarmIdentities(saved, current);
                var submittedDue = (edits.Due, edits.DueIsDateOnly, edits.DueTzid);
                if (fields.HasFlag(TaskDetailFields.Repeat)
                    && (current.Due, current.DueIsDateOnly, current.DueTzid) == submittedDue
                    && (saved.Due, saved.DueIsDateOnly, saved.DueTzid) != submittedDue)
                {
                    current.Due = saved.Due;
                    current.DueIsDateOnly = saved.DueIsDateOnly;
                    current.DueTzid = saved.DueTzid;
                    if ((PendingDueDate, PendingDueTime) == pendingDue) RefreshPendingDueDate(current);
                    UpdateActionLabels();
                }
                IsErrorOpen = false;
            }
            TaskChanged?.Invoke(saved);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"WINTODO_TASK_SAVE_EXCEPTION={DiagnosticException.Describe(exception)}");
            if (ReferenceEquals(_model, current)) ShowError(_resources.GetString("Detail_SaveFailed"));
            return false;
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private void ShowError(string message)
    {
        ErrorMessage = message;
        IsErrorOpen = true;
    }

    private static TodoTask CloneTask(TodoTask source)
    {
        var clone = new TodoTask
        {
            Id = source.Id,
            Uid = source.Uid,
            ListId = source.ListId,
            ParentUid = source.ParentUid,
            Summary = source.Summary,
            Description = source.Description,
            Status = source.Status,
            PercentComplete = source.PercentComplete,
            Priority = source.Priority,
            Due = source.Due,
            DueIsDateOnly = source.DueIsDateOnly,
            DueTzid = source.DueTzid,
            Start = source.Start,
            StartIsDateOnly = source.StartIsDateOnly,
            CompletedAt = source.CompletedAt,
            Created = source.Created,
            LastModified = source.LastModified,
            Sequence = source.Sequence,
            RRule = source.RRule,
            MyDayDate = source.MyDayDate,
            MyDayExcludedDate = source.MyDayExcludedDate,
            SortOrder = source.SortOrder,
            Location = source.Location,
            Url = source.Url,
            Class = source.Class,
            IcalUnknown = source.IcalUnknown,
            RemoteHref = source.RemoteHref,
            RemoteETag = source.RemoteETag,
            IsDirty = source.IsDirty,
            IsDeleted = source.IsDeleted,
        };
        clone.Categories.AddRange(source.Categories);
        clone.Alarms.AddRange(source.Alarms.Select(CloneAlarm));
        clone.Attachments.AddRange(source.Attachments.Select(attachment => new TaskAttachment
        {
            Id = attachment.Id,
            TaskId = attachment.TaskId,
            FileName = attachment.FileName,
            RelativePath = attachment.RelativePath,
            SizeBytes = attachment.SizeBytes,
            MimeType = attachment.MimeType,
            Added = attachment.Added,
        }));
        return clone;
    }

    private static TaskAlarm CloneAlarm(TaskAlarm alarm) => new()
    {
        Id = alarm.Id,
        TaskId = alarm.TaskId,
        Uid = alarm.Uid,
        Action = alarm.Action,
        Trigger = alarm.Trigger,
        TriggerOffsetSeconds = alarm.TriggerOffsetSeconds,
        TriggerRelated = alarm.TriggerRelated,
        Description = alarm.Description,
        SnoozedUntil = alarm.SnoozedUntil,
        IsAcknowledged = alarm.IsAcknowledged,
        ScheduledToastTag = alarm.ScheduledToastTag,
    };

    private static void CopyAlarmIdentities(TodoTask source, TodoTask destination)
    {
        foreach (TaskAlarm destinationAlarm in destination.Alarms)
        {
            TaskAlarm? sourceAlarm = string.IsNullOrWhiteSpace(destinationAlarm.Uid)
                ? source.Alarms.FirstOrDefault(alarm =>
                    alarm.Trigger == destinationAlarm.Trigger
                    && alarm.TriggerOffsetSeconds == destinationAlarm.TriggerOffsetSeconds)
                : source.Alarms.FirstOrDefault(alarm => string.Equals(
                    alarm.Uid,
                    destinationAlarm.Uid,
                    StringComparison.Ordinal));
            if (sourceAlarm is null)
            {
                continue;
            }

            destinationAlarm.Id = sourceAlarm.Id;
            destinationAlarm.TaskId = sourceAlarm.TaskId;
            destinationAlarm.Uid = sourceAlarm.Uid;
        }

    }

}
