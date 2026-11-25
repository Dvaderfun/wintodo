using Microsoft.Data.Sqlite;
using Wintodo.Core.Models;
using Wintodo.Core.Services;

namespace Wintodo.Data;

public sealed partial class SqliteTaskStore
{
    public Task<TodoTask> UpdateTaskDetailAsync(
        TodoTask edits, TaskDetailFields fields, bool moveStarredToTop = false,
        long? removedAttachmentId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edits);
        ArgumentException.ThrowIfNullOrWhiteSpace(edits.Uid);
        const TaskDetailFields supported = TaskDetailFields.Summary | TaskDetailFields.Description | TaskDetailFields.Due |
            TaskDetailFields.Reminder | TaskDetailFields.Repeat | TaskDetailFields.Importance | TaskDetailFields.RemoveAttachment;
        if ((fields & ~supported) != 0) throw new ArgumentOutOfRangeException(nameof(fields));
        if (((fields & TaskDetailFields.RemoveAttachment) != 0) != removedAttachmentId.HasValue)
            throw new ArgumentException("Removing an attachment requires its id.", nameof(removedAttachmentId));

        return ExecuteWriteAsync((connection, transaction, context) =>
        {
            TodoTask current = ReadTaskByUid(connection, transaction, edits.Uid, includeDeleted: false)
                ?? throw new KeyNotFoundException($"Task '{edits.Uid}' was not found.");
            if ((fields & TaskDetailFields.Summary) != 0) current.Summary = edits.Summary;
            if ((fields & TaskDetailFields.Description) != 0) current.Description = edits.Description;
            if ((fields & TaskDetailFields.Due) != 0)
            {
                current.Due = edits.Due;
                current.DueIsDateOnly = edits.DueIsDateOnly;
                current.DueTzid = edits.DueTzid;
            }
            if ((fields & TaskDetailFields.Repeat) != 0)
            {
                current.RRule = edits.RRule;
                if (!string.IsNullOrWhiteSpace(current.RRule) && current.Due is null)
                {
                    current.Due = new DateTimeOffset(_clock.Today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
                    current.DueIsDateOnly = true;
                    current.DueTzid = null;
                }
            }
            if ((fields & TaskDetailFields.Importance) != 0)
                SetPriority(connection, transaction, current, edits.Priority, moveStarredToTop);
            if ((fields & TaskDetailFields.Reminder) != 0)
                ReplaceTaskAlarms(connection, transaction, current.Id, edits.Alarms);
            if (removedAttachmentId is { } id)
            {
                using SqliteCommand remove = connection.CreateCommand();
                remove.Transaction = transaction;
                remove.CommandText = "DELETE FROM attachments WHERE task_id = @taskId AND id = @id;";
                remove.Parameters.AddWithValue("@taskId", current.Id);
                remove.Parameters.AddWithValue("@id", id);
                if (remove.ExecuteNonQuery() != 1)
                    throw new KeyNotFoundException("The attachment no longer belongs to this task.");
            }

            if (fields != TaskDetailFields.None)
            {
                ValidateTask(current);
                CommitTaskEdit(connection, transaction, context, current);
                // Read back canonical values (including trimmed titles and normalized dates) under the same lock.
                current = ReadTaskByUid(connection, transaction, edits.Uid, includeDeleted: false)!;
            }
            LoadTaskCollections(connection, current, transaction);
            return current;
        }, cancellationToken);
    }

    private static void ReplaceTaskAlarms(
        SqliteConnection connection, SqliteTransaction transaction, long taskId, List<TaskAlarm> alarms)
    {
        using SqliteCommand remove = connection.CreateCommand();
        remove.Transaction = transaction;
        remove.CommandText = "DELETE FROM alarms WHERE task_id = @taskId;";
        remove.Parameters.AddWithValue("@taskId", taskId);
        remove.ExecuteNonQuery();
        InsertTaskAlarms(connection, transaction, taskId, alarms);
    }
}
