using Microsoft.Data.Sqlite;
using Wintodo.Core.Ical;
using Wintodo.Core.Models;
using Wintodo.Data.Mapping;

namespace Wintodo.Data;

public sealed partial class SqliteTaskStore
{
    public Task<IReadOnlyList<TaskMoveIntent>> GetPendingTaskMovesAsync(
        long? accountId = null, CancellationToken cancellationToken = default)
    {
        return ExecuteReadAsync<IReadOnlyList<TaskMoveIntent>>(connection =>
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = $"SELECT {MoveColumns} FROM task_move_outbox WHERE @account IS NULL OR source_account_id = @account OR destination_account_id = @account ORDER BY id;";
            AddParameter(command, "@account", accountId);
            using SqliteDataReader reader = command.ExecuteReader();
            var moves = new List<TaskMoveIntent>();
            while (reader.Read()) moves.Add(ReadMove(reader));
            return moves;
        }, cancellationToken);
    }

    public Task<TaskMoveIntent?> GetTaskMoveAsync(long id, CancellationToken cancellationToken = default)
        => ExecuteReadAsync(connection => ReadMoveById(connection, null, id), cancellationToken);

    public Task<TaskMoveIntent?> PrepareTaskMoveAttemptAsync(long id, int expectedSequence,
        string destinationHref, byte[] payload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return PrepareSingleMoveAsync(id, expectedSequence, destinationHref, payload, cancellationToken);
    }

    private async Task<TaskMoveIntent?> PrepareSingleMoveAsync(long id, int expectedSequence,
        string destinationHref, byte[] payload, CancellationToken cancellationToken)
    {
        IReadOnlyList<TaskMoveIntent>? prepared = await PrepareTaskMoveAttemptsAsync(
            [new TaskMoveAttempt { MoveId = id, ExpectedSequence = expectedSequence,
                DestinationHref = destinationHref, Payload = payload }], cancellationToken).ConfigureAwait(false);
        return prepared?.Single();
    }

    public Task<IReadOnlyList<TaskMoveIntent>?> PrepareTaskMoveAttemptsAsync(
        IReadOnlyList<TaskMoveAttempt> attempts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attempts);
        return ExecuteWriteAsync<IReadOnlyList<TaskMoveIntent>?>((connection, transaction, _) =>
        {
            if (attempts.Count == 0 || attempts.Select(attempt => attempt.MoveId).Distinct().Count() != attempts.Count) return null;
            var moves = new List<TaskMoveIntent>();
            var tasks = new List<TodoTask>();
            var suppressed = new List<bool>();
            var needsPreparation = new List<bool>();
            foreach (TaskMoveAttempt attempt in attempts)
            {
                TaskMoveIntent? move = ReadMoveById(connection, transaction, attempt.MoveId);
                if (move is null || move.PredecessorId is not null) return null;
                if (!move.SourceBindingVerified) return null;
                TodoTask? task = ReadTaskByUid(connection, transaction, move.TaskUid, includeDeleted: true);
                if (task is null || task.Sequence != attempt.ExpectedSequence || task.ListId != move.DestinationListId) return null;
                bool suppressDestination = task.IsDeleted || (task.ParentUid is not null
                    && ReadTaskByUid(connection, transaction, task.ParentUid, includeDeleted: true)?.IsDeleted == true);
                bool planned = move.Phase == TaskMovePhase.Planned;
                bool skipped = !planned && move.AttemptedPayload is { Length: 0 }
                    && move.DestinationAccountId is not null && !suppressDestination
                    && move.ResolutionChoice != SyncConflictChoice.KeepTheirs
                    && move.DestinationHref == attempt.DestinationHref;
                bool immutableAttempt = !planned && move.AttemptedPayload is { Length: > 0 };
                bool unchangedSkip = !planned && move.AttemptedPayload is { Length: 0 }
                    && (suppressDestination || move.DestinationAccountId is null);
                if (!planned && !skipped && !immutableAttempt && !unchangedSkip) return null;
                if ((planned || skipped) && move.DestinationAccountId is not null && !suppressDestination
                    && (attempt.Payload.Length == 0 || string.IsNullOrWhiteSpace(attempt.DestinationHref)))
                    throw new ArgumentException("A synced destination requires a resource and payload.", nameof(attempts));
                moves.Add(move);
                tasks.Add(task);
                suppressed.Add(suppressDestination);
                needsPreparation.Add(planned || skipped);
            }
            var selectedIds = attempts.Select(attempt => attempt.MoveId).ToHashSet();
            if (moves.Select(move => move.GroupId).Distinct().Any(group =>
                ReadMoveGroup(connection, transaction, group).Any(move => !selectedIds.Contains(move.Id)))) return null;
            for (int index = 0; index < moves.Count; index++)
            {
                TaskMoveIntent move = moves[index];
                if (!needsPreparation[index]) continue;
                TodoTask task = tasks[index];
                TaskMoveAttempt attempt = attempts[index];
                move.AttemptedPayload = suppressed[index] ? [] : attempt.Payload.ToArray();
                move.AttemptedSequence = task.Sequence;
                move.AttemptedChangeId = LatestTaskChangeId(connection, transaction, task.Uid);
                move.LocalSequence = task.Sequence;
                move.ChangeId = move.AttemptedChangeId.Value;
                move.DestinationHref = move.DestinationAccountId is null ? null : attempt.DestinationHref;
                move.DestinationETag = null;
                move.DestinationRemoved = false;
                move.Phase = TaskMovePhase.DestinationAttempted;
                SaveMove(connection, transaction, move);
            }
            return moves;
        }, cancellationToken);
    }

    public Task<bool> UpdateTaskMoveAsync(TaskMoveIntent intent, int expectedVersion,
        CancellationToken cancellationToken = default, int? expectedTaskSequence = null)
    {
        ArgumentNullException.ThrowIfNull(intent);
        return UpdateTaskMoveGroupAsync([new TaskMoveUpdate { Intent = intent,
            ExpectedVersion = expectedVersion, ExpectedTaskSequence = expectedTaskSequence }], cancellationToken);
    }

    public Task<bool> UpdateTaskMoveGroupAsync(IReadOnlyList<TaskMoveUpdate> updates,
        CancellationToken cancellationToken = default)
        => SaveTaskMoveGroupAsync(updates, false, cancellationToken);

    public Task<bool> ReauthorizeTaskMoveResolutionAsync(IReadOnlyList<TaskMoveUpdate> updates,
        CancellationToken cancellationToken = default)
        => SaveTaskMoveGroupAsync(updates, true, cancellationToken);

    private Task<bool> SaveTaskMoveGroupAsync(IReadOnlyList<TaskMoveUpdate> updates, bool reauthorize,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(updates);
        return ExecuteWriteAsync((connection, transaction, _) =>
        {
            if (updates.Count == 0 || updates.Select(update => update.Intent.Id).Distinct().Count() != updates.Count) return false;
            var newlyResolvedGroups = new HashSet<string>(StringComparer.Ordinal);
            foreach (TaskMoveUpdate update in updates)
            {
                TaskMoveIntent intent = update.Intent;
                TaskMoveIntent? stored = ReadMoveById(connection, transaction, intent.Id);
                if (stored is null || stored.Version != update.ExpectedVersion || !SameMoveIdentity(stored, intent)) return false;
                if (intent.Phase < stored.Phase || intent.Phase > stored.Phase + 1) return false;
                if (reauthorize && (intent.Phase != stored.Phase || intent.ResolutionChoice is null
                    || update.ExpectedTaskSequence is null || intent.ResolutionListId is null)) return false;
                if (!SameAttempt(stored, intent) || (!reauthorize && !SameResolution(stored, intent))) return false;
                if ((!stored.SourceRestored && intent.SourceRestored)
                    || stored.RestoredSourceETag != intent.RestoredSourceETag) return false;
                if (reauthorize && stored.ResolutionCopyPayload is not null && !stored.ResolutionCopyConfirmed
                    && (stored.ResolutionCopyUid != intent.ResolutionCopyUid
                        || stored.ResolutionCopyHref != intent.ResolutionCopyHref
                        || !SameBytes(stored.ResolutionCopyPayload, intent.ResolutionCopyPayload))) return false;
                TodoTask? current = ReadTaskByUid(connection, transaction, intent.TaskUid, includeDeleted: true);
                bool guardedTheirs = stored.ResolutionChoice == SyncConflictChoice.KeepTheirs;
                int? guardedSequence = update.ExpectedTaskSequence ?? (guardedTheirs ? stored.ResolutionSequence : null);
                long guardedList = intent.ResolutionListId ?? stored.ResolutionListId ?? stored.DestinationListId;
                if (guardedSequence is not null && (current is null || current.Sequence != guardedSequence
                    || current.ListId != guardedList)) return false;
                if (intent.ResolutionChoice is not null && (reauthorize || stored.ResolutionChoice is null || guardedTheirs)
                    && (intent.ResolutionSequence is null
                    || current is null || current.Sequence != intent.ResolutionSequence
                    || current.ListId != guardedList)) return false;
                if (stored.Phase == TaskMovePhase.Planned && intent.Phase != TaskMovePhase.Planned) return false;
                if (intent.Phase >= TaskMovePhase.DestinationConfirmed && intent.DestinationAccountId is not null
                    && intent.AttemptedPayload is { Length: > 0 } && !intent.DestinationRemoved
                    && string.IsNullOrWhiteSpace(intent.DestinationETag)) return false;
                if (!reauthorize && stored.ResolutionCopyConfirmed && !intent.ResolutionCopyConfirmed) return false;
                if (stored.ResolutionDestinationRemoved && !intent.ResolutionDestinationRemoved) return false;
                if (stored.DestinationRemoved && !intent.DestinationRemoved) return false;
                if (reauthorize || (stored.ResolutionChoice is null && intent.ResolutionChoice is not null))
                    newlyResolvedGroups.Add(stored.GroupId);
                intent.Version = stored.Version;
            }
            var selectedIds = updates.Select(update => update.Intent.Id).ToHashSet();
            if (newlyResolvedGroups.Any(group => ReadMoveGroup(connection, transaction, group)
                .Any(move => !selectedIds.Contains(move.Id)))) return false;
            foreach (TaskMoveUpdate update in updates) SaveMove(connection, transaction, update.Intent);
            return true;
        }, cancellationToken);
    }

    public Task<bool> CompleteTaskMoveAsync(long id, int expectedVersion, CancellationToken cancellationToken = default)
        => CompleteTaskMoveGroupAsync([new TaskMoveUpdate { Intent = new TaskMoveIntent { Id = id },
            ExpectedVersion = expectedVersion }], cancellationToken);

    public Task<bool> CompleteTaskMoveGroupAsync(IReadOnlyList<TaskMoveUpdate> updates,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updates);
        return ExecuteWriteAsync((connection, transaction, context) =>
        {
            if (updates.Count == 0 || updates.Select(update => update.Intent.Id).Distinct().Count() != updates.Count) return false;
            var moves = new List<TaskMoveIntent>();
            foreach (TaskMoveUpdate update in updates)
            {
                TaskMoveIntent? move = ReadMoveById(connection, transaction, update.Intent.Id);
                if (move is null) continue;
                if (move.Version != update.ExpectedVersion || move.Phase != TaskMovePhase.SourceRemoved
                    || move.SourceRestored) return false;
                moves.Add(move);
            }
            if (moves.Count == 0) return true;
            if (moves.Count != updates.Count) return false;
            var ids = moves.Select(move => move.Id).ToHashSet();
            if (moves.Select(move => move.GroupId).Distinct().Any(group =>
                ReadMoveGroup(connection, transaction, group).Any(move => !ids.Contains(move.Id)))) return false;
            foreach (TaskMoveIntent move in moves)
                if (!CompleteMove(connection, transaction, context, move.Id, move.Version))
                    throw new InvalidOperationException("Move group completion changed inside its transaction.");
            return true;
        }, cancellationToken);
    }

    public Task<bool> ConfirmTaskMoveDestinationRestorationAsync(long id, int expectedVersion,
        string destinationETag, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationETag);
        return ExecuteWriteAsync((connection, transaction, _) =>
        {
            TaskMoveIntent? move = ReadMoveById(connection, transaction, id);
            if (move is null || move.Version != expectedVersion || move.DestinationAccountId is null
                || move.Phase < TaskMovePhase.DestinationConfirmed || move.AttemptedPayload is not { Length: > 0 }
                || move.ResolutionChoice == SyncConflictChoice.KeepTheirs
                || !destinationETag.StartsWith('"') || !destinationETag.EndsWith('"')) return false;
            move.DestinationETag = destinationETag;
            move.DestinationRemoved = false;
            move.ResolutionDestinationRemoved = false;
            SaveMove(connection, transaction, move);
            return true;
        }, cancellationToken);
    }

    public Task<bool> ConfirmTaskMoveSourceRestorationAsync(long id, int expectedVersion,
        string sourceETag, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceETag);
        return ExecuteWriteAsync((connection, transaction, _) =>
        {
            TaskMoveIntent? move = ReadMoveById(connection, transaction, id);
            if (move is null || move.Version != expectedVersion || move.SourceAccountId is null
                || !move.SourceBindingVerified || move.Phase < TaskMovePhase.DestinationAttempted
                || string.IsNullOrWhiteSpace(move.SourceHref) || move.ResolutionChoice is null
                || (move.ResolutionSourcePayload is not { Length: > 0 } && move.SourcePayload is not { Length: > 0 })
                || !sourceETag.StartsWith('"') || !sourceETag.EndsWith('"')) return false;
            move.SourceRestored = true;
            move.RestoredSourceETag = sourceETag;
            SaveMove(connection, transaction, move);
            return true;
        }, cancellationToken);
    }

    private bool CompleteMove(SqliteConnection connection, SqliteTransaction transaction,
        WriteContext context, long id, int expectedVersion)
    {
            TaskMoveIntent? move = ReadMoveById(connection, transaction, id);
            if (move is null) return true;
            if (move.Version != expectedVersion || move.Phase != TaskMovePhase.SourceRemoved || move.SourceRestored) return false;
            if (ReadMoveGroup(connection, transaction, move.GroupId)
                .Any(member => member.Phase != TaskMovePhase.SourceRemoved || member.SourceRestored)) return false;
            TodoTask? task = ReadTaskByUid(connection, transaction, move.TaskUid, includeDeleted: true);
            TaskMoveIntent? successor = ReadNextMove(connection, transaction, move.TaskUid, move.Id);
            if (move.AttemptedChangeId is not null)
                MarkEntityChangesSyncedThrough(connection, transaction, SyncEntityType.Task, move.TaskUid, move.AttemptedChangeId.Value);
            if (successor is not null)
            {
                bool noSource = move.DestinationAccountId is null || move.DestinationRemoved || move.AttemptedPayload is { Length: 0 };
                successor.PredecessorId = null;
                successor.SourceAccountId = noSource ? null : move.DestinationAccountId;
                successor.SourceHref = noSource ? null : move.DestinationHref;
                successor.SourceETag = noSource ? null : move.DestinationETag;
                SaveMove(connection, transaction, successor);
            }
            if (task is not null && task.ListId == move.DestinationListId && successor is null)
            {
                // Binding metadata follows current membership; newer content/deletes remain queued.
                bool noDestination = move.DestinationAccountId is null || move.DestinationRemoved || move.AttemptedPayload is { Length: 0 };
                task.RemoteHref = noDestination ? null : move.DestinationHref;
                task.RemoteETag = noDestination ? null : move.DestinationETag;
                task.IsDirty = HasUnsyncedEntityChange(connection, transaction, SyncEntityType.Task, task.Uid);
                UpdateTaskRow(connection, transaction, task);
                context.AddTask(task.ListId, task.Uid);
            }
            DeleteMoveRow(connection, transaction, id);
            return true;
    }

    public Task<bool> RestoreTaskMoveSourceAsync(long id, int expectedVersion, TodoTask sourceTask,
        string sourceHref, string sourceETag, int expectedSequence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceTask);
        return RestoreTaskMoveSourcesAsync([new TaskMoveSourceRestore { MoveId = id,
            ExpectedVersion = expectedVersion, SourceTask = sourceTask, SourceHref = sourceHref,
            SourceETag = sourceETag, ExpectedSequence = expectedSequence }], cancellationToken);
    }

    public Task<bool> RestoreTaskMoveSourcesAsync(IReadOnlyList<TaskMoveSourceRestore> restores,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(restores);
        return ExecuteWriteAsync((connection, transaction, context) =>
        {
            if (restores.Count == 0 || restores.Select(restore => restore.MoveId).Distinct().Count() != restores.Count) return false;
            var moves = new List<TaskMoveIntent>();
            var currentTasks = new List<TodoTask>();
            foreach (TaskMoveSourceRestore restore in restores)
            {
                TaskMoveIntent? move = ReadMoveById(connection, transaction, restore.MoveId);
                TodoTask? current = move is null ? null : ReadTaskByUid(connection, transaction, move.TaskUid, includeDeleted: true);
                if (move is null || move.Version != restore.ExpectedVersion || current is null
                    || restore.SourceTask.Uid != move.TaskUid
                    || current.Sequence != restore.ExpectedSequence || current.ListId != (move.ResolutionListId ?? move.DestinationListId)
                    || move.ResolutionSequence != restore.ExpectedSequence
                    || move.ResolutionChoice != SyncConflictChoice.KeepTheirs
                    || (move.DestinationAccountId is not null && move.AttemptedPayload is { Length: > 0 }
                        && !move.ResolutionDestinationRemoved)
                    || ReadSuccessorMoves(connection, transaction, move.TaskUid, move.Id)
                        .Any(successor => successor.Phase != TaskMovePhase.Planned)) return false;
                GetListUid(connection, transaction, move.SourceListId);
                moves.Add(move);
                currentTasks.Add(current);
            }
            var selectedIds = restores.Select(restore => restore.MoveId).ToHashSet();
            if (moves.Select(move => move.GroupId).Distinct().Any(group =>
                ReadMoveGroup(connection, transaction, group).Any(move => !selectedIds.Contains(move.Id)))) return false;
            for (int index = 0; index < moves.Count; index++)
            {
                TaskMoveIntent move = moves[index];
                TodoTask restored = CloneMoveTask(restores[index].SourceTask);
                restored.Id = currentTasks[index].Id;
                restored.Uid = move.TaskUid;
                restored.ListId = move.SourceListId;
                restored.RemoteHref = move.SourceAccountId is null ? null : restores[index].SourceHref;
                restored.RemoteETag = move.SourceAccountId is null ? null : restores[index].SourceETag;
                restored.IsDirty = false;
                restored.IsDeleted = false;
                if (move.SourceAccountId is null)
                {
                    LoadTaskCollections(connection, currentTasks[index], transaction);
                    restored.Attachments.Clear();
                    restored.Attachments.AddRange(currentTasks[index].Attachments);
                }
                UpdateTaskRow(connection, transaction, restored);
                ReplaceTaskCollections(connection, transaction, restored);
                MarkEntityChangesSyncedThrough(connection, transaction, SyncEntityType.Task, restored.Uid,
                    LatestTaskChangeId(connection, transaction, restored.Uid));
                DeleteMoveRow(connection, transaction, move.Id);
                foreach (TaskMoveIntent successor in ReadSuccessorMoves(connection, transaction, move.TaskUid, move.Id))
                    DeleteMoveRow(connection, transaction, successor.Id);
                context.IsRemoteChange = true;
                context.AddTask(move.SourceListId, restored.Uid);
                context.AddTask(move.DestinationListId, restored.Uid);
                context.AddTask(currentTasks[index].ListId, restored.Uid);
            }
            return true;
        }, cancellationToken);
    }

    public Task<RemotePullApplyResult> ApplyRemoteTaskMoveAsync(long destinationListId,
        RemoteTaskUpdate target, RemoteTaskState expectedLocal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(expectedLocal);
        ArgumentException.ThrowIfNullOrWhiteSpace(target.Href);
        return ExecuteWriteAsync((connection, transaction, context) =>
        {
            var result = new RemotePullApplyResult();
            TodoTask? current = ReadTaskByUid(connection, transaction, target.Task.Uid, includeDeleted: true);
            if (current is null || current.Uid != expectedLocal.Uid || current.Sequence != expectedLocal.Sequence
                || current.IsDeleted != expectedLocal.IsDeleted || current.IsDirty || expectedLocal.IsDirty
                || current.RemoteHref != expectedLocal.RemoteHref || current.RemoteETag != expectedLocal.RemoteETag
                || HasUnsyncedEntityChange(connection, transaction, SyncEntityType.Task, current.Uid)
                || HasTaskMove(connection, transaction, current.Uid)
                || target.Task.Sequence < current.Sequence)
            {
                result.HasDeferredChanges = true;
                if (current is not null)
                {
                    if (target.Task.Sequence < current.Sequence)
                        HoldKnownMoveVersion(connection, transaction, context, current);
                    AddPullConflict(connection, transaction, result, current, target.Task, target.Href, target.ETag);
                }
                return result;
            }
            if (target.Task.Sequence == current.Sequence)
            {
                LoadTaskCollections(connection, current, transaction);
                if (!SameMoveContent(current, target.Task))
                {
                    result.HasDeferredChanges = true;
                    HoldKnownMoveVersion(connection, transaction, context, current);
                    AddPullConflict(connection, transaction, result, current, target.Task, target.Href, target.ETag);
                    return result;
                }
            }
            GetListUid(connection, transaction, destinationListId);
            ValidateTask(target.Task);
            TodoTask moved = CloneMoveTask(target.Task);
            moved.Id = current.Id;
            moved.ListId = destinationListId;
            moved.RemoteHref = target.Href;
            moved.RemoteETag = target.ETag;
            moved.IsDirty = false;
            moved.IsDeleted = false;
            moved.Created = moved.Created == default ? current.Created : moved.Created;
            moved.LastModified = moved.LastModified == default ? _clock.UtcNow : moved.LastModified;
            UpdateTaskRow(connection, transaction, moved);
            ReplaceTaskCollections(connection, transaction, moved);
            context.IsRemoteChange = true;
            context.AddTask(current.ListId, moved.Uid);
            context.AddTask(destinationListId, moved.Uid);
            result.AppliedCount = 1;
            return result;
        }, cancellationToken);
    }

    private void MoveTaskGroup(SqliteConnection connection, SqliteTransaction transaction, WriteContext context,
        IReadOnlyList<string> selected, long destinationListId, double? sortOrder = null,
        string? restoredParentUid = null, string? groupId = null, CancellationToken cancellationToken = default)
    {
        GetListUid(connection, transaction, destinationListId);
        var tasks = new Dictionary<string, TodoTask>(StringComparer.Ordinal);
        var roots = new HashSet<string>(StringComparer.Ordinal);
        foreach (string uid in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TodoTask task = ReadTaskByUid(connection, transaction, uid, includeDeleted: true)
                ?? throw new KeyNotFoundException($"Task '{uid}' was not found.");
            tasks.TryAdd(uid, task);
            if (task.ParentUid is not null) continue;
            roots.Add(task.Uid);
            using SqliteCommand children = connection.CreateCommand();
            children.Transaction = transaction;
            children.CommandText = $"SELECT {SqliteMapper.TaskColumns} FROM tasks t WHERE t.parent_uid = @uid AND t.is_deleted = 0;";
            children.Parameters.AddWithValue("@uid", uid);
            using SqliteDataReader reader = children.ExecuteReader();
            while (reader.Read())
            {
                TodoTask step = SqliteMapper.ReadTask(reader);
                tasks.TryAdd(step.Uid, step);
            }
        }
        var moveGroups = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (TodoTask task in tasks.Values.OrderBy(task => task.ParentUid is null ? 0 : 1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            long sourceListId = task.ListId;
            string? originalParent = task.ParentUid;
            bool remainsStep = originalParent is not null && roots.Contains(originalParent);
            string? parent = remainsStep ? originalParent : restoredParentUid;
            double newSort = remainsStep || restoredParentUid is not null ? task.SortOrder : sortOrder ?? GetNextSortOrder(connection, transaction,
                "tasks", "list_id = @listId AND parent_uid IS NULL", "@listId", destinationListId);
            if (sourceListId == destinationListId && task.ParentUid == parent && task.SortOrder == newSort) continue;
            TodoTask before = CloneMoveTask(task);
            task.ListId = destinationListId;
            task.ParentUid = parent;
            task.SortOrder = newSort;
            task.LastModified = _clock.UtcNow;
            task.Sequence++;
            task.IsDirty = true;
            UpdateTaskRow(connection, transaction, task);
            LogChange(connection, transaction, TaskEntityType, task.Uid, ChangeOperation.Update);
            if (sourceListId != destinationListId)
            {
                string groupRoot = remainsStep ? originalParent! : task.Uid;
                if (!moveGroups.TryGetValue(groupRoot, out string? taskGroup))
                    moveGroups[groupRoot] = taskGroup = groupId ?? Guid.NewGuid().ToString("N");
                RecordMove(connection, transaction, task, before, taskGroup);
            }
            context.AddTask(sourceListId, task.Uid);
            context.AddTask(destinationListId, task.Uid);
        }
    }

    private void RecordMove(SqliteConnection connection, SqliteTransaction transaction,
        TodoTask task, TodoTask before, string groupId)
    {
        TodoList source = ReadListById(connection, transaction, before.ListId);
        TodoList destination = ReadListById(connection, transaction, task.ListId);
        TaskMoveIntent? last = ReadLastMove(connection, transaction, task.Uid);
        if (last is null && string.IsNullOrWhiteSpace(before.RemoteHref)
            && (!destination.IsSyncEnabled || destination.AccountId is null)) return;
        TaskMoveIntent move;
        if (last is { Phase: TaskMovePhase.Planned })
        {
            move = last;
            move.GroupId = groupId;
            if (!move.SourceBindingVerified && move.PredecessorId is null)
                VerifySourceBinding(connection, transaction, move);
        }
        else
        {
            move = new TaskMoveIntent
            {
                TaskUid = task.Uid, GroupId = groupId, Created = _clock.UtcNow,
                PredecessorId = last?.Id,
                SourceListId = last?.DestinationListId ?? before.ListId,
                SourceListUid = last?.DestinationListUid ?? source.Uid,
                SourceAccountId = last is not null ? last.DestinationAccountId
                    : string.IsNullOrWhiteSpace(before.RemoteHref) ? null : source.AccountId,
                SourceHref = last?.DestinationHref ?? before.RemoteHref,
                SourceETag = last?.DestinationETag ?? before.RemoteETag,
            };
            if (last is null) VerifySourceBinding(connection, transaction, move);
            if (move.SourceAccountId is null && move.SourceBindingVerified)
            {
                LoadTaskCollections(connection, before, transaction);
                move.SourcePayload = new IcalWriter(_clock.LocalTimeZone)
                    .WriteTaskMoveCalendar(source, before, _clock.UtcNow);
            }
        }
        move.DestinationListId = task.ListId;
        move.DestinationListUid = destination.Uid;
        move.DestinationAccountId = destination.IsSyncEnabled ? destination.AccountId : null;
        move.DestinationHref = null;
        move.LocalSequence = task.Sequence;
        move.ChangeId = LatestTaskChangeId(connection, transaction, task.Uid);
        bool cancelledBeforeAttempt = move.PredecessorId is null && move.SourceListId == task.ListId
            && move.SourceBindingVerified;
        if (cancelledBeforeAttempt || (move.PredecessorId is null && move.SourceAccountId is null
            && move.DestinationAccountId is null && string.IsNullOrWhiteSpace(move.SourceHref)))
        {
            if (move.Id != 0) DeleteMoveRow(connection, transaction, move.Id);
            task.RemoteHref = cancelledBeforeAttempt ? move.SourceHref : null;
            task.RemoteETag = cancelledBeforeAttempt ? move.SourceETag : null;
        }
        else
        {
            SaveMove(connection, transaction, move);
            task.RemoteHref = null;
            task.RemoteETag = null;
        }
        UpdateTaskRow(connection, transaction, task);
    }

    private static int ReadRemoteDeleteState(SqliteConnection connection, SqliteTransaction transaction, string uid)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT remote_delete_state FROM tasks WHERE uid = @uid;";
        command.Parameters.AddWithValue("@uid", uid);
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void HoldKnownMoveVersion(SqliteConnection connection, SqliteTransaction transaction,
        WriteContext context, TodoTask task)
    {
        // Old-schema and explicit-user tombstones carry no proof permitting revival.
        int state = ReadRemoteDeleteState(connection, transaction, task.Uid);
        if (task.IsDeleted && state != 1) return;
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE tasks SET is_deleted = 0, remote_delete_state = 2 WHERE uid = @uid;";
        command.Parameters.AddWithValue("@uid", task.Uid);
        command.ExecuteNonQuery();
        task.IsDeleted = false;
        context.IsRemoteChange = true;
        context.AddTask(task.ListId, task.Uid);
    }

    private static bool SameMoveContent(TodoTask a, TodoTask b)
    {
        if (a.Uid != b.Uid || a.ParentUid != b.ParentUid || a.Summary != b.Summary
            || a.Description != b.Description || a.Status != b.Status || a.PercentComplete != b.PercentComplete
            || a.Priority != b.Priority || a.Due != b.Due || a.DueIsDateOnly != b.DueIsDateOnly
            || a.DueTzid != b.DueTzid || a.Start != b.Start || a.StartIsDateOnly != b.StartIsDateOnly
            || a.CompletedAt != b.CompletedAt || a.Created != b.Created || a.LastModified != b.LastModified
            || a.Sequence != b.Sequence || a.RRule != b.RRule || a.MyDayDate != b.MyDayDate
            || a.SortOrder != b.SortOrder || a.Location != b.Location || a.Url != b.Url
            || a.Class != b.Class || a.IcalUnknown != b.IcalUnknown
            || !a.Categories.Order(StringComparer.Ordinal).SequenceEqual(b.Categories.Order(StringComparer.Ordinal))
            || a.Alarms.Count != b.Alarms.Count || a.Attachments.Count != b.Attachments.Count) return false;
        for (int index = 0; index < a.Alarms.Count; index++)
        {
            TaskAlarm x = a.Alarms[index];
            TaskAlarm y = b.Alarms[index];
            if (x.Uid != y.Uid || x.Action != y.Action || x.Trigger != y.Trigger
                || x.TriggerOffsetSeconds != y.TriggerOffsetSeconds || x.TriggerRelated != y.TriggerRelated
                || x.Description != y.Description || x.SnoozedUntil != y.SnoozedUntil
                || x.IsAcknowledged != y.IsAcknowledged) return false;
        }
        for (int index = 0; index < a.Attachments.Count; index++)
        {
            TaskAttachment x = a.Attachments[index];
            TaskAttachment y = b.Attachments[index];
            if (x.FileName != y.FileName || x.RelativePath != y.RelativePath || x.SizeBytes != y.SizeBytes
                || x.MimeType != y.MimeType || x.Added != y.Added) return false;
        }
        return true;
    }

    private static void VerifySourceBinding(SqliteConnection connection, SqliteTransaction transaction,
        TaskMoveIntent move)
    {
        if (string.IsNullOrWhiteSpace(move.SourceHref)) return;
        move.SourceBindingVerified = false;
        if (!Uri.TryCreate(move.SourceHref, UriKind.Absolute, out Uri? resource)
            || resource.Scheme is not ("http" or "https") || resource.UserInfo.Length != 0
            || resource.Fragment.Length != 0) return;
        int separator = resource.AbsolutePath.LastIndexOf('/');
        if (separator < 0 || separator == resource.AbsolutePath.Length - 1) return;
        string parentPath = resource.AbsolutePath[..(separator + 1)];
        var possible = new List<TodoList>();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = $"SELECT {ListColumns} FROM lists WHERE account_id IS NOT NULL AND remote_url IS NOT NULL AND is_deleted = 0;";
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                TodoList list = SqliteMapper.ReadList(reader);
                if (Uri.TryCreate(list.RemoteUrl, UriKind.Absolute, out Uri? collection)
                    && SameMoveOrigin(resource, collection) && collection.Query == resource.Query
                    && collection.AbsolutePath.TrimEnd('/') + "/" == parentPath) possible.Add(list);
            }
        }
        var approved = new List<TodoList>();
        foreach (TodoList list in possible)
        {
            using SqliteCommand account = connection.CreateCommand();
            account.Transaction = transaction;
            account.CommandText = "SELECT server_url FROM accounts WHERE id = @id;";
            AddParameter(account, "@id", list.AccountId);
            if (Uri.TryCreate(account.ExecuteScalar() as string, UriKind.Absolute, out Uri? origin)
                && SameMoveOrigin(resource, origin)) approved.Add(list);
        }
        if (approved.Count != 1) return;
        TodoList source = approved[0];
        move.SourceAccountId = source.AccountId;
        move.SourceListId = source.Id;
        move.SourceListUid = source.Uid;
        move.SourceBindingVerified = true;
    }

    private static bool SameMoveOrigin(Uri a, Uri b) => b.UserInfo.Length == 0 && b.Fragment.Length == 0
        && a.Scheme.Equals(b.Scheme, StringComparison.OrdinalIgnoreCase)
        && a.IdnHost.Equals(b.IdnHost, StringComparison.OrdinalIgnoreCase) && a.Port == b.Port;

    private static TodoTask CloneMoveTask(TodoTask source)
    {
        // Conflict-copy cloning deliberately drops identity/binding; a move must keep them.
        TodoTask clone = CloneTask(source);
        clone.Id = source.Id;
        clone.Uid = source.Uid;
        clone.Created = source.Created;
        clone.LastModified = source.LastModified;
        clone.SortOrder = source.SortOrder;
        clone.RemoteHref = source.RemoteHref;
        clone.RemoteETag = source.RemoteETag;
        clone.IsDirty = source.IsDirty;
        clone.IsDeleted = source.IsDeleted;
        return clone;
    }

    private static bool HasTaskMove(SqliteConnection connection, SqliteTransaction? transaction, string uid)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM task_move_outbox WHERE task_uid = @uid);";
        command.Parameters.AddWithValue("@uid", uid);
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) != 0;
    }

    private void QueueRestoredSteps(SqliteConnection connection, SqliteTransaction transaction,
        WriteContext context, string parentUid)
    {
        using SqliteCommand children = connection.CreateCommand();
        children.Transaction = transaction;
        children.CommandText = $"SELECT {SqliteMapper.TaskColumns} FROM tasks t WHERE parent_uid = @parent AND is_deleted = 0 AND remote_href IS NULL;";
        children.Parameters.AddWithValue("@parent", parentUid);
        var tasks = new List<TodoTask>();
        using (SqliteDataReader reader = children.ExecuteReader())
            while (reader.Read()) tasks.Add(SqliteMapper.ReadTask(reader));
        foreach (TodoTask child in tasks)
        {
            TaskMoveIntent? pending = ReadLastMove(connection, transaction, child.Uid);
            bool frozenChangeOnly = pending?.AttemptedChangeId is long boundary
                && LatestTaskChangeId(connection, transaction, child.Uid) <= boundary;
            if (!frozenChangeOnly && HasUnsyncedEntityChange(connection, transaction, SyncEntityType.Task, child.Uid)) continue;
            child.Sequence++;
            child.LastModified = _clock.UtcNow;
            child.IsDirty = true;
            UpdateTaskRow(connection, transaction, child);
            LogChange(connection, transaction, TaskEntityType, child.Uid, ChangeOperation.Update);
            context.AddTask(child.ListId, child.Uid);
        }
    }

    private static void GuardMoveObligations(SqliteConnection connection, SqliteTransaction transaction,
        long? accountId = null, long? listId = null)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT EXISTS(SELECT 1 FROM task_move_outbox
                WHERE source_account_id = @account OR destination_account_id = @account
                   OR source_list_id = @list OR destination_list_id = @list);
            """;
        AddParameter(command, "@account", accountId);
        AddParameter(command, "@list", listId);
        if (Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) != 0)
            throw new PendingTaskMoveException();
    }

    private static TodoList ReadListById(SqliteConnection connection, SqliteTransaction transaction, long id)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT {ListColumns} FROM lists l WHERE l.id = @id;";
        command.Parameters.AddWithValue("@id", id);
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read()) throw new KeyNotFoundException("List was not found.");
        TodoList list = SqliteMapper.ReadList(reader);
        reader.Dispose();
        // A configured target can still be waiting for its first collection create.
        using SqliteCommand enabled = connection.CreateCommand();
        enabled.Transaction = transaction;
        enabled.CommandText = "SELECT NOT EXISTS(SELECT 1 FROM settings WHERE key = @key AND value = 'true');";
        enabled.Parameters.AddWithValue("@key", GetListSyncDisabledKey(list.Uid));
        list.IsSyncEnabled = list.AccountId is not null
            && Convert.ToInt32(enabled.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) == 1;
        return list;
    }

    private static long LatestTaskChangeId(SqliteConnection connection, SqliteTransaction transaction, string uid)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COALESCE(MAX(id), 0) FROM change_log WHERE entity_type = 0 AND entity_uid = @uid;";
        command.Parameters.AddWithValue("@uid", uid);
        return (long)command.ExecuteScalar()!;
    }
}
