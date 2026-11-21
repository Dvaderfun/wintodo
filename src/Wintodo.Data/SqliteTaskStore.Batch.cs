using Wintodo.Core.Models;
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
        bool moveStarredToTop = false)
    {
        ArgumentNullException.ThrowIfNull(uids);
        if (!Enum.IsDefined(action)) throw new ArgumentOutOfRangeException(nameof(action));
        if (action == TaskBatchAction.SetDueDate && date is null)
            throw new ArgumentException("A due date is required.", nameof(date));
        if (action == TaskBatchAction.Move && destinationListId is null)
            throw new ArgumentException("A destination list is required.", nameof(destinationListId));
        string[] selection = uids.Distinct(StringComparer.Ordinal).ToArray();
        foreach (string uid in selection) ArgumentException.ThrowIfNullOrWhiteSpace(uid);

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
                long sourceListId = task.ListId;
                bool wasDeleted = task.IsDeleted;
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
                        changed = task.MyDayDate != _clock.Today || task.MyDayExcludedDate is not null;
                        task.MyDayDate = _clock.Today;
                        task.MyDayExcludedDate = null;
                        break;
                    case TaskBatchAction.RemoveFromMyDay:
                        DateOnly? exclusion = Wintodo.Core.Presentation.TaskDateLabels.IsDueToday(task, _clock) ? _clock.Today : null;
                        changed = task.MyDayDate is not null || task.MyDayExcludedDate != exclusion;
                        task.MyDayDate = null;
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
                task.LastModified = _clock.UtcNow;
                task.Sequence++;
                task.IsDirty = true;
                UpdateTaskRow(connection, transaction, task);
                LogChange(connection, transaction, TaskEntityType, uid,
                    action == TaskBatchAction.Delete ? ChangeOperation.Delete : ChangeOperation.Update);
                if (action == TaskBatchAction.Restore && wasDeleted)
                    QueueRestoredSteps(connection, transaction, context, uid);
                context.AddTask(sourceListId, uid);
                context.AddTask(task.ListId, uid);
            }
        }, cancellationToken);
    }
}
