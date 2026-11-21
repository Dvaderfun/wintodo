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
        Task<TodoTask?> taskRequest;
        Task<IReadOnlyList<TodoTask>> stepsRequest;
        do
        {
            if (!await FlushPendingEditsAsync() || loadVersion != _loadVersion) return false;
            revision = _textSaves.Revision;
            taskRequest = _taskStore.GetTaskAsync(uid, cancellationToken);
            stepsRequest = _taskStore.GetChildrenAsync(uid, cancellationToken);
            await Task.WhenAll(taskRequest, stepsRequest);
            if (loadVersion != _loadVersion) return false;
            // The old editor was still available while SQLite read the new selection.
        } while (HasPendingEdits || (uid == _model?.Uid && revision != _textSaves.Revision));

        TodoTask task = await taskRequest
            ?? throw new KeyNotFoundException($"Task '{uid}' was not found.");
        _model = task;
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
        IsMyDay = task.MyDayDate is not null;
        CreatedText = TaskDateLabels.FormatCreated(
            task.Created.ToLocalTime(),
            _resources.GetString("Detail_Created"),
            DisplayCulture.Current);

        PendingDueDate = task.Due?.ToLocalTime();
        PendingDueTime = task.DueIsDateOnly
            ? TimeSpan.Zero
            : task.Due?.ToLocalTime().TimeOfDay ?? TimeSpan.Zero;
        TaskAlarm? reminder = task.Alarms.FirstOrDefault(alarm => !alarm.IsAcknowledged);
        PendingReminderDate = reminder?.Trigger?.ToLocalTime();
        PendingReminderTime =
            reminder?.Trigger?.ToLocalTime().TimeOfDay ?? _preferences.DefaultReminderTime;
        ConfigureRepeatEditor(task.RRule);
        UpdateActionLabels();
        _isLoading = false;
    }

    internal bool HasPendingEdits => _textSaves.HasPendingEdits;

    internal Task<bool> FlushPendingEditsAsync() => _textSaves.FlushAsync();

    private void ScheduleSave(TimeSpan delay, bool writesWholeTask = false)
    {
        if (_model is { } owner)
        {
            _textSaves.Schedule(delay, () => PersistModelAsync(owner, textOnly: !writesWholeTask), writesWholeTask);
        }
    }

    private Task<bool> PersistModelAsync(CancellationToken cancellationToken = default) =>
        _model is { } current ? PersistModelAsync(current, cancellationToken) : Task.FromResult(false);

    private async Task<bool> PersistModelAsync(
        TodoTask current, CancellationToken cancellationToken = default, bool textOnly = false)
    {
        if (string.IsNullOrWhiteSpace(current.Summary)) return false;

        await _saveGate.WaitAsync(cancellationToken);
        try
        {
            if (textOnly)
            {
                await _taskStore.SetTaskTextAsync(current.Uid, current.Summary.Trim(), current.Description, cancellationToken);
            }
            else
            {
                TodoTask snapshot = CloneTask(current);
                snapshot.Summary = snapshot.Summary.Trim();
                await _taskStore.UpdateTaskAsync(snapshot, cancellationToken, moveStarredToTop: _preferences.MoveStarredToTop);
                current.SortOrder = snapshot.SortOrder;
                current.Sequence = snapshot.Sequence;
                current.LastModified = snapshot.LastModified;
                CopyCollectionIdentities(snapshot, current);
            }
            if (!textOnly || ReferenceEquals(_model, current))
                TaskChanged?.Invoke(CloneTask(current));
            IsErrorOpen = false;
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"WINTODO_TASK_SAVE_EXCEPTION={DiagnosticException.Describe(exception)}");
            ShowError(_resources.GetString("Detail_SaveFailed"));
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
        clone.Alarms.AddRange(source.Alarms.Select(alarm => new TaskAlarm
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
        }));
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

    private static void CopyCollectionIdentities(TodoTask source, TodoTask destination)
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

        foreach (TaskAttachment destinationAttachment in destination.Attachments)
        {
            TaskAttachment? sourceAttachment = source.Attachments.FirstOrDefault(
                attachment => string.Equals(
                    attachment.RelativePath,
                    destinationAttachment.RelativePath,
                    StringComparison.Ordinal));
            if (sourceAttachment is null)
            {
                continue;
            }

            destinationAttachment.Id = sourceAttachment.Id;
            destinationAttachment.TaskId = sourceAttachment.TaskId;
            destinationAttachment.Added = sourceAttachment.Added;
        }
    }

}
