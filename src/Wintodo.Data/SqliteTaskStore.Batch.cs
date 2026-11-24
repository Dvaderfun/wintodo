using Wintodo.Core.Models;
using Wintodo.Core.Presentation;
using Wintodo.Core.Services;

namespace Wintodo.Data;

public sealed partial class SqliteTaskStore
{
    public Task ApplyTaskBatchAsync(
        IReadOnlyList<string> uids,
        TaskBatchAction action,
        CancellationToken cancellationToken = default,
        DateOnly? date = null,
        long? destinationListId = null,
        bool moveStarredToTop = false,
        TodoTask? expectedSuggestion = null)
    {
        ArgumentNullException.ThrowIfNull(uids);
        if (!Enum.IsDefined(action)) throw new ArgumentOutOfRangeException(nameof(action));
        if (action == TaskBatchAction.SetDueDate && date is null)
            throw new ArgumentException("A due date is required.", nameof(date));
        if (action == TaskBatchAction.Move && destinationListId is null)
            throw new ArgumentException("A destination list is required.", nameof(destinationListId));
        string[] selection = uids.Distinct(StringComparer.Ordinal).ToArray();
        foreach (string uid in selection) ArgumentException.ThrowIfNullOrWhiteSpace(uid);
        if (expectedSuggestion is not null && (selection.Length != 1
            || selection[0] != expectedSuggestion.Uid
            || action is not (TaskBatchAction.AddToMyDay or TaskBatchAction.Complete)))
            throw new ArgumentException("A suggestion action requires one matching task.", nameof(expectedSuggestion));

        return ExecuteWriteAsync((connection, transaction, context) =>
        {
            if (action == TaskBatchAction.Move)
            {
                foreach (string uid in selection) _ = GetTaskIdentity(connection, transaction, uid);
                GetListUid(connection, transaction, destinationListId!.Value);
                string[] moving = selection.Where(uid => ReadTaskByUid(connection, transaction, uid,
                    includeDeleted: false)!.ListId != destinationListId.Value).ToArray();
                MoveTaskGroup(connection, transaction, context, moving, destinationListId.Value,
                    cancellationToken: cancellationToken);
                return;
            }
            foreach (string uid in selection)
            {
                cancellationToken.ThrowIfCancellationRequested();
                TodoTask task = ReadTaskByUid(connection, transaction, uid,
                    includeDeleted: action == TaskBatchAction.Restore)
                    ?? throw new KeyNotFoundException($"Task '{uid}' was not found.");
                DateOnly today = _clock.Today;
                if (expectedSuggestion is not null && (task.Sequence != expectedSuggestion.Sequence
                    || task.MyDayExcludedDate != expectedSuggestion.MyDayExcludedDate
                    || MyDaySuggestions.Classify(task, today, _clock) is null))
                    throw new InvalidOperationException("The task suggestion has changed.");
                long sourceListId = task.ListId;
                bool wasDeleted = task.IsDeleted;
                DateOnly? previousPin = task.MyDayDate;
                bool changed = true;
                switch (action)
                {
                    case TaskBatchAction.Complete:
                        if (task.Status == Core.Models.TaskStatus.Completed) { changed = false; break; }
                        if (string.IsNullOrWhiteSpace(task.RRule))
                        {
                            task.Status = Core.Models.TaskStatus.Completed;
                            task.PercentComplete = 100;
                            task.CompletedAt = _clock.UtcNow;
                        }
                        else AdvanceRecurringTask(connection, transaction, task, _clock.UtcNow);
                        break;
                    case TaskBatchAction.Uncomplete:
                        if (task.Status != Core.Models.TaskStatus.Completed) { changed = false; break; }
                        task.Status = Core.Models.TaskStatus.NeedsAction;
                        task.PercentComplete = 0;
                        task.CompletedAt = null;
                        break;
                    case TaskBatchAction.MarkImportant:
                        if (task.Priority is >= 1 and <= 4) { changed = false; break; }
                        task.Priority = (int)TaskPriority.High;
                        if (moveStarredToTop && task.ParentUid is null
                            && IsManualList(connection, transaction, task.ListId))
                            task.SortOrder = GetFirstTaskSortOrder(connection, transaction, task.ListId, uid);
                        break;
                    case TaskBatchAction.RemoveImportance:
                        if (task.Priority is not (>= 1 and <= 4)) { changed = false; break; }
                        task.Priority = (int)TaskPriority.Undefined;
                        break;
                    case TaskBatchAction.AddToMyDay:
                        changed = task.MyDayDate != today || task.MyDayExcludedDate is not null;
                        task.MyDayDate = today;
                        task.MyDayExcludedDate = null;
                        break;
                    case TaskBatchAction.RemoveFromMyDay:
                        DateOnly? exclusion = Wintodo.Core.Presentation.TaskDateLabels.IsDueToday(task, _clock) ? _clock.Today : null;
                        if (task.MyDayDate == _clock.Today) task.MyDayDate = null;
                        changed = task.MyDayDate != previousPin || task.MyDayExcludedDate != exclusion;
                        task.MyDayExcludedDate = exclusion;
                        break;
                    case TaskBatchAction.SetDueDate:
                        // Date-only storage preserves the calendar date, independent of the UTC offset.
                        task.Due = new DateTimeOffset(date!.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
                        task.DueIsDateOnly = true;
                        task.DueTzid = null;
                        break;
                    case TaskBatchAction.Delete:
                        task.IsDeleted = true;
                        break;
                    case TaskBatchAction.Restore:
                        GetListUid(connection, transaction, task.ListId);
                        changed = task.IsDeleted;
                        task.IsDeleted = false;
                        break;
                }
                if (!changed) continue;
                bool syncChange = action is not (TaskBatchAction.AddToMyDay or TaskBatchAction.RemoveFromMyDay)
                    || task.MyDayDate != previousPin;
                if (syncChange)
                {
                    task.LastModified = _clock.UtcNow;
                    task.Sequence++;
                    task.IsDirty = true;
                    LogChange(connection, transaction, TaskEntityType, uid,
                        action == TaskBatchAction.Delete ? ChangeOperation.Delete : ChangeOperation.Update);
                }
                UpdateTaskRow(connection, transaction, task);
                if (action == TaskBatchAction.Restore && wasDeleted)
                    QueueRestoredSteps(connection, transaction, context, uid);
                context.AddTask(sourceListId, uid);
                context.AddTask(task.ListId, uid);
            }
        }, cancellationToken);
    }
}
