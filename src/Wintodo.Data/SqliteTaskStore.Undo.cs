using Microsoft.Data.Sqlite;
using Wintodo.Core.Models;
using Wintodo.Core.Services;
using Wintodo.Data.Mapping;

namespace Wintodo.Data;

public sealed partial class SqliteTaskStore
{
    public Task<IReadOnlyList<TaskUndoState>> CaptureTaskUndoAsync(
        IReadOnlyList<string> uids, TaskUndoFields fields, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uids);
        return ExecuteReadAsync<IReadOnlyList<TaskUndoState>>(connection =>
        {
            using SqliteTransaction transaction = connection.BeginTransaction(deferred: true);
            var states = new List<TaskUndoState>();
            foreach (string uid in uids.Distinct(StringComparer.Ordinal))
            {
                TodoTask task = ReadTaskByUid(connection, transaction, uid, includeDeleted: true)
                    ?? throw new KeyNotFoundException($"Task '{uid}' was not found.");
                LoadTaskCollections(connection, task);
                states.Add(new TaskUndoState(task, fields, GetCompletionCount(connection, transaction, uid)));
            }
            return states;
        }, cancellationToken);
    }

    public Task RestoreTaskFieldsAsync(IReadOnlyList<TaskUndoState> states, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(states);
        return ExecuteWriteAsync((connection, transaction, context) =>
        {
            foreach (TaskUndoState state in states)
            {
                cancellationToken.ThrowIfCancellationRequested();
                TodoTask before = state.Task;
                TodoTask task = ReadTaskByUid(connection, transaction, before.Uid, includeDeleted: true)
                    ?? throw new KeyNotFoundException($"Task '{before.Uid}' was not found.");
                LoadTaskCollections(connection, task);
                long source = task.ListId;
                TodoTask membershipBefore = CloneMoveTask(task);
                string moveGroup = Guid.NewGuid().ToString("N");
                TaskUndoFields fields = state.Fields;
                if ((fields & TaskUndoFields.List) != 0 && task.ListId != before.ListId)
                {
                    GetListUid(connection, transaction, before.ListId);
                    if (task.ParentUid is null)
                    {
                        var children = new List<string>();
                        using (SqliteCommand select = connection.CreateCommand())
                        {
                            select.Transaction = transaction;
                            select.CommandText = "SELECT uid FROM tasks WHERE parent_uid = @parent AND is_deleted = 0;";
                            select.Parameters.AddWithValue("@parent", task.Uid);
                            using SqliteDataReader reader = select.ExecuteReader();
                            while (reader.Read()) children.Add(reader.GetString(0));
                        }
                        if (children.Count > 0)
                            MoveTaskGroup(connection, transaction, context, children, before.ListId,
                                restoredParentUid: before.Uid, groupId: moveGroup, cancellationToken: cancellationToken);
                    }
                    task.ListId = before.ListId;
                    task.ParentUid = before.ParentUid;
                    task.SortOrder = before.SortOrder;
                }
                else if ((fields & TaskUndoFields.List) != 0) task.SortOrder = before.SortOrder;
                GetListUid(connection, transaction, task.ListId);
                if ((fields & TaskUndoFields.Deleted) != 0) task.IsDeleted = before.IsDeleted;
                if ((fields & TaskUndoFields.Due) != 0)
                {
                    task.Due = before.Due;
                    task.DueIsDateOnly = before.DueIsDateOnly;
                    task.DueTzid = before.DueTzid;
                }
                if ((fields & TaskUndoFields.Repeat) != 0) task.RRule = before.RRule;
                if ((fields & TaskUndoFields.Importance) != 0)
                {
                    task.Priority = before.Priority;
                    task.SortOrder = before.SortOrder;
                }
                if ((fields & TaskUndoFields.MyDay) != 0)
                {
                    task.MyDayDate = before.MyDayDate;
                    task.MyDayExcludedDate = before.MyDayExcludedDate;
                }
                if ((fields & TaskUndoFields.Completion) != 0)
                {
                    task.Status = before.Status;
                    task.PercentComplete = before.PercentComplete;
                    task.CompletedAt = before.CompletedAt;
                    if (!string.IsNullOrWhiteSpace(before.RRule))
                    {
                        task.Due = before.Due;
                        task.Start = before.Start;
                    }
                    using SqliteCommand history = connection.CreateCommand();
                    history.Transaction = transaction;
                    history.CommandText = """
                        DELETE FROM completion_history WHERE task_uid = @uid AND id NOT IN (
                            SELECT id FROM completion_history WHERE task_uid = @uid ORDER BY id LIMIT @count);
                        """;
                    history.Parameters.AddWithValue("@uid", task.Uid);
                    history.Parameters.AddWithValue("@count", state.CompletionCount);
                    history.ExecuteNonQuery();
                }
                if ((fields & TaskUndoFields.Reminder) != 0 ||
                    ((fields & TaskUndoFields.Completion) != 0 && !string.IsNullOrWhiteSpace(before.RRule)))
                {
                    task.Alarms.Clear();
                    task.Alarms.AddRange(before.Alarms);
                    ReplaceTaskCollections(connection, transaction, task);
                }
                task.LastModified = _clock.UtcNow;
                task.Sequence++;
                task.IsDirty = true;
                UpdateTaskRow(connection, transaction, task);
                LogChange(connection, transaction, TaskEntityType, task.Uid,
                    task.IsDeleted ? ChangeOperation.Delete : ChangeOperation.Update);
                if (membershipBefore.ListId != task.ListId)
                    RecordMove(connection, transaction, task, membershipBefore, moveGroup);
                if (membershipBefore.IsDeleted && !task.IsDeleted)
                    QueueRestoredSteps(connection, transaction, context, task.Uid);
                context.AddTask(source, task.Uid);
                context.AddTask(task.ListId, task.Uid);
            }
        }, cancellationToken);
    }

    public Task RestoreListAsync(long listId, CancellationToken cancellationToken = default)
    {
        return ExecuteWriteAsync((connection, transaction, context) =>
        {
            string uid = GetListUid(connection, transaction, listId, includeDeleted: true);
            using SqliteCommand check = connection.CreateCommand();
            check.Transaction = transaction;
            check.CommandText = "SELECT is_deleted, account_id, remote_url FROM lists WHERE id = @id;";
            check.Parameters.AddWithValue("@id", listId);
            bool recreate;
            using (SqliteDataReader reader = check.ExecuteReader())
            {
                reader.Read();
                if (!reader.GetBoolean(0)) return;
                recreate = !reader.IsDBNull(1) && reader.IsDBNull(2);
            }
            var restored = new List<string>();
            using (SqliteCommand tasks = connection.CreateCommand())
            {
                tasks.Transaction = transaction;
                tasks.CommandText = "SELECT task_uid FROM list_deleted_tasks WHERE list_id = @id;";
                tasks.Parameters.AddWithValue("@id", listId);
                using SqliteDataReader reader = tasks.ExecuteReader();
                while (reader.Read()) restored.Add(reader.GetString(0));
            }
            using SqliteCommand update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE lists SET is_deleted = 0, modified_utc = @now,
                    remote_ctag = CASE WHEN @recreate THEN NULL ELSE remote_ctag END,
                    sync_token = CASE WHEN @recreate THEN NULL ELSE sync_token END WHERE id = @id;
                UPDATE tasks SET is_deleted = 0, is_dirty = 1, remote_delete_state = 0, modified_utc = @now, sequence = sequence + 1,
                    remote_href = CASE WHEN @recreate THEN NULL ELSE remote_href END,
                    remote_etag = CASE WHEN @recreate THEN NULL ELSE remote_etag END
                WHERE uid IN (SELECT task_uid FROM list_deleted_tasks WHERE list_id = @id);
                DELETE FROM list_deleted_tasks WHERE list_id = @id;
                """;
            update.Parameters.AddWithValue("@id", listId);
            update.Parameters.AddWithValue("@now", SqliteMapper.FormatInstant(_clock.UtcNow));
            update.Parameters.AddWithValue("@recreate", recreate);
            update.ExecuteNonQuery();
            LogChange(connection, transaction, ListEntityType, uid, recreate ? ChangeOperation.Create : ChangeOperation.Update);
            foreach (string taskUid in restored)
            {
                LogChange(connection, transaction, TaskEntityType, taskUid, ChangeOperation.Update);
                context.AddTask(listId, taskUid);
            }
            context.AddList(listId);
        }, cancellationToken);
    }
}
