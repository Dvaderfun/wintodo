using Microsoft.Data.Sqlite;
using Wintodo.Core.Models;

namespace Wintodo.Data;

public sealed partial class SqliteTaskStore
{
    public Task<IReadOnlyList<TodoTask>> GetTaskFamilyForSyncAsync(string uid,
        CancellationToken cancellationToken = default)
    {
        return ExecuteReadAsync<IReadOnlyList<TodoTask>>(connection =>
        {
            using SqliteTransaction transaction = connection.BeginTransaction(deferred: true);
            TodoTask? root = ReadTaskByUid(connection, transaction, uid, includeDeleted: true);
            if (root is null) return [];
            LoadTaskCollections(connection, root, transaction);
            var family = new List<TodoTask> { root };
            foreach (string childUid in GetConflictStepUids(connection, transaction, uid))
            {
                TodoTask child = ReadTaskByUid(connection, transaction, childUid, includeDeleted: true)!;
                LoadTaskCollections(connection, child, transaction);
                family.Add(child);
            }
            return family;
        }, cancellationToken);
    }

    public Task<bool> ResolveRemoteConflictAsync(SyncConflict conflict, SyncConflictChoice choice,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conflict);
        return ExecuteWriteAsync((connection, transaction, context) =>
        {
            TodoTask expected = conflict.LocalTask;
            TodoTask? current = ReadTaskByUid(connection, transaction, expected.Uid, includeDeleted: true);
            if (current is null || current.Sequence != expected.Sequence || current.ListId != expected.ListId
                || current.RemoteHref != expected.RemoteHref || current.RemoteETag != expected.RemoteETag
                || current.IsDeleted != expected.IsDeleted || HasTaskMove(connection, transaction, current.Uid)) return false;
            // The caller captures the complete family before showing the chooser.
            IReadOnlyList<TodoTask> family = conflict.LocalFamily.Count == 0 ? [expected] : conflict.LocalFamily;
            var childUids = GetConflictStepUids(connection, transaction, current.Uid).ToHashSet(StringComparer.Ordinal);
            if (!childUids.SetEquals(family.Where(task => task.Uid != current.Uid).Select(task => task.Uid))) return false;
            foreach (TodoTask snapshot in family)
            {
                TodoTask? member = ReadTaskByUid(connection, transaction, snapshot.Uid, includeDeleted: true);
                if (member is null || member.Sequence != snapshot.Sequence || member.ListId != snapshot.ListId
                    || member.IsDeleted != snapshot.IsDeleted || member.RemoteHref != snapshot.RemoteHref
                    || member.RemoteETag != snapshot.RemoteETag || HasTaskMove(connection, transaction, member.Uid)) return false;
            }
            if (choice == SyncConflictChoice.KeepMine)
            {
                if (!QueueReverseMove(connection, transaction, context, current,
                    conflict.RemoteDeleted ? null : conflict.RemoteHref,
                    conflict.RemoteDeleted ? null : conflict.RemoteETag))
                {
                    current.RemoteHref = conflict.RemoteDeleted ? null : conflict.RemoteHref;
                    current.RemoteETag = conflict.RemoteDeleted ? null : conflict.RemoteETag;
                    current.IsDirty = true;
                    UpdateTaskRow(connection, transaction, current);
                    if (!HasUnsyncedEntityChange(connection, transaction, SyncEntityType.Task, current.Uid))
                        LogChange(connection, transaction, TaskEntityType, current.Uid,
                            current.IsDeleted ? ChangeOperation.Delete : ChangeOperation.Update);
                    context.AddTask(current.ListId, current.Uid);
                }
                return true;
            }
            if (choice is not (SyncConflictChoice.KeepTheirs or SyncConflictChoice.KeepBoth)) return false;
            if (!conflict.RemoteDeleted && (conflict.RemoteTask is null || conflict.RemoteTask.Uid != current.Uid)) return false;
            if (choice == SyncConflictChoice.KeepBoth)
            {
                // A deleted remote parent has no competing child UID version to retain.
                // Keep its existing steps under the fresh local parent, as session undo expects.
                IReadOnlyList<TodoTask> copied = conflict.RemoteDeleted ? [expected] : family;
                var identities = copied.ToDictionary(task => task.Uid, _ => Guid.NewGuid().ToString("D"), StringComparer.Ordinal);
                foreach (TodoTask snapshot in copied)
                {
                    TodoTask copy = CloneTask(snapshot);
                    copy.Uid = identities[snapshot.Uid];
                    if (copy.ParentUid is not null && identities.TryGetValue(copy.ParentUid, out string? parent)) copy.ParentUid = parent;
                    PrepareNewTask(connection, transaction, copy);
                    copy.Id = InsertTask(connection, transaction, copy);
                    ReplaceTaskCollections(connection, transaction, copy);
                    LogChange(connection, transaction, TaskEntityType, copy.Uid, ChangeOperation.Create);
                    context.AddTask(copy.ListId, copy.Uid);
                }
                if (conflict.RemoteDeleted)
                {
                    foreach (TodoTask snapshot in family.Where(task => task.Uid != current.Uid))
                    {
                        TodoTask step = ReadTaskByUid(connection, transaction, snapshot.Uid, includeDeleted: true)!;
                        step.ParentUid = identities[current.Uid];
                        step.Sequence++;
                        step.LastModified = _clock.UtcNow;
                        step.IsDirty = true;
                        UpdateTaskRow(connection, transaction, step);
                        LogChange(connection, transaction, TaskEntityType, step.Uid, ChangeOperation.Update);
                        context.AddTask(step.ListId, step.Uid);
                    }
                }
            }
            if (conflict.RemoteDeleted)
            {
                foreach (TodoTask snapshot in family)
                {
                    if (choice == SyncConflictChoice.KeepBoth && snapshot.Uid != current.Uid) continue;
                    TodoTask member = ReadTaskByUid(connection, transaction, snapshot.Uid, includeDeleted: true)!;
                    member.IsDeleted = true;
                    bool rootDeleted = member.Uid == current.Uid;
                    member.IsDirty = !rootDeleted;
                    if (rootDeleted) member.RemoteETag = null;
                    UpdateTaskRow(connection, transaction, member);
                    if (rootDeleted) MarkEntityChangesSynced(connection, transaction, SyncEntityType.Task, member.Uid);
                    else LogChange(connection, transaction, TaskEntityType, member.Uid, ChangeOperation.Delete);
                    context.AddTask(member.ListId, member.Uid);
                }
            }
            else
            {
                TodoTask adopted = CloneMoveTask(conflict.RemoteTask!);
                adopted.Id = current.Id;
                adopted.ListId = conflict.ListId;
                adopted.RemoteHref = conflict.RemoteHref;
                adopted.RemoteETag = conflict.RemoteETag;
                adopted.IsDirty = false;
                adopted.IsDeleted = false;
                adopted.Created = adopted.Created == default ? current.Created : adopted.Created;
                adopted.LastModified = adopted.LastModified == default ? _clock.UtcNow : adopted.LastModified;
                GetListUid(connection, transaction, adopted.ListId);
                UpdateTaskRow(connection, transaction, adopted);
                ReplaceTaskCollections(connection, transaction, adopted);
                MarkEntityChangesSynced(connection, transaction, SyncEntityType.Task, adopted.Uid);
                if (childUids.Count > 0 && current.ListId != adopted.ListId)
                    MoveTaskGroup(connection, transaction, context, childUids.ToArray(), adopted.ListId,
                        restoredParentUid: adopted.Uid, cancellationToken: cancellationToken);
                context.AddTask(current.ListId, current.Uid);
                context.AddTask(adopted.ListId, adopted.Uid);
            }
            context.IsRemoteChange = true;
            return true;
        }, cancellationToken);
    }

    private bool QueueReverseMove(SqliteConnection connection, SqliteTransaction transaction,
        WriteContext context, TodoTask task, string? href, string? etag)
    {
        if (string.IsNullOrWhiteSpace(href)) return false;
        TodoList destination = ReadListById(connection, transaction, task.ListId);
        if (Uri.TryCreate(href, UriKind.Absolute, out Uri? resource)
            && Uri.TryCreate(destination.RemoteUrl, UriKind.Absolute, out Uri? collection)
            && SameMoveOrigin(resource, collection) && resource.Query == collection.Query
            && resource.AbsolutePath[..(resource.AbsolutePath.LastIndexOf('/') + 1)]
                == collection.AbsolutePath.TrimEnd('/') + "/") return false;
        string groupId = Guid.NewGuid().ToString("N");
        if (task.ParentUid is null)
            MoveTaskGroup(connection, transaction, context, [task.Uid], task.ListId,
                sortOrder: task.SortOrder, groupId: groupId);
        TodoTask before = CloneMoveTask(task);
        before.RemoteHref = href;
        before.RemoteETag = etag;
        task.IsDirty = true;
        UpdateTaskRow(connection, transaction, task);
        LogChange(connection, transaction, TaskEntityType, task.Uid,
            task.IsDeleted ? ChangeOperation.Delete : ChangeOperation.Update);
        RecordMove(connection, transaction, task, before, groupId);
        context.AddTask(task.ListId, task.Uid);
        return true;
    }
}
