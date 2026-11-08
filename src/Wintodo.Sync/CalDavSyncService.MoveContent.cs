using Wintodo.Core.Ical;
using Wintodo.Core.Models;

namespace Wintodo.Sync;

public sealed partial class CalDavSyncService
{
    private byte[] WriteMovePayload(TodoList list, TodoTask task)
        => _writer.WriteTaskMoveCalendar(list, task, _clock.UtcNow);

    private TodoTask ReadSingleMoveTask(byte[] payload)
    {
        IcalCalendar calendar = _reader.Read(payload);
        if (calendar.Tasks.Count != 1)
            throw new IcalFormatException("A task move resource must contain exactly one VTODO.");
        return calendar.Tasks[0];
    }

    private TodoTask? ReadMoveTaskOrNull(byte[] payload)
    {
        try { return ReadSingleMoveTask(payload); }
        catch (IcalFormatException) { return null; }
    }

    private bool MovePayloadMatches(byte[] expected, byte[] actual)
    {
        try
        {
            TodoTask expectedTask = ReadSingleMoveTask(expected);
            TodoTask actualTask = ReadSingleMoveTask(actual);
            if (expectedTask.Uid != actualTask.Uid || expectedTask.Sequence != actualTask.Sequence
                || expectedTask.ParentUid != actualTask.ParentUid || expectedTask.SortOrder != actualTask.SortOrder
                || expectedTask.Summary != actualTask.Summary || expectedTask.Description != actualTask.Description)
                return false;
            DateTimeOffset timestamp = _clock.UtcNow;
            var calendar = new TodoList();
            // Normalize framing/DTSTAMP while comparing every mapped field,
            // owned collection and preserved unknown iCalendar property.
            return _writer.WriteCalendar(calendar, [expectedTask], timestamp).AsSpan()
                .SequenceEqual(_writer.WriteCalendar(calendar, [actualTask], timestamp));
        }
        catch (IcalFormatException) { return false; }
    }

    private async Task AddMoveConflictAsync(TaskMoveIntent move, MoveEndpoint endpoint,
        CalDavContent? content, bool isDestination, SyncRunResult result,
        HashSet<string> conflictedUids, CancellationToken cancellationToken)
    {
        result.Status = SyncRunStatus.AttentionRequired;
        TodoTask? local = await _store.GetTaskForSyncAsync(move.TaskUid, cancellationToken)
            .ConfigureAwait(false);
        if (local is null || !conflictedUids.Add(local.Uid)) return;
        TodoTask? remote = null;
        try { if (content is not null) remote = ReadSingleMoveTask(content.Content); }
        catch (IcalFormatException) { }
        var conflict = new SyncConflict
        {
            TaskMoveId = move.Id, IsMoveDestinationConflict = isDestination,
            AccountId = endpoint.Account.Id, ListId = endpoint.List.Id,
            ListName = endpoint.List.DisplayName, LocalTask = local, RemoteTask = remote,
            RemoteHref = isDestination ? move.DestinationHref! : move.SourceHref!,
            RemoteETag = content?.ETag, RemoteDeleted = content is null,
        };
        foreach (TaskMoveIntent member in (await _store.GetPendingTaskMovesAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false)).Where(value => value.GroupId == move.GroupId))
        {
            TodoTask? memberTask = await _store.GetTaskForSyncAsync(member.TaskUid, cancellationToken)
                .ConfigureAwait(false);
            if (memberTask is null) continue;
            if (member.Id == move.Id) conflict.LocalTask = memberTask;
            CalDavContent? memberContent = member.Id == move.Id ? content : null;
            long? memberAccount = isDestination ? member.DestinationAccountId : member.SourceAccountId;
            long memberList = isDestination ? member.DestinationListId : member.SourceListId;
            string? memberHref = isDestination ? member.DestinationHref : member.SourceHref;
            if (member.Id != move.Id && memberAccount is not null && !string.IsNullOrWhiteSpace(memberHref))
            {
                MoveEndpoint? memberEndpoint = await GetMoveEndpointAsync(memberAccount.Value, memberList,
                    cancellationToken).ConfigureAwait(false);
                if (memberEndpoint is not null)
                    memberContent = await ReadMoveResourceAsync(memberEndpoint,
                        new Uri(memberHref, UriKind.Absolute), cancellationToken).ConfigureAwait(false);
            }
            conflict.MoveGroupSnapshots.Add(new TaskMoveConflictSnapshot
            {
                MoveId = member.Id, LocalSequence = memberTask.Sequence, LocalListId = memberTask.ListId,
                RemotePayload = memberContent?.Content ?? (isDestination ? null : member.SourcePayload),
                RemoteETag = memberContent?.ETag, RemoteMissing = memberContent is null,
            });
        }
        result.Conflicts.Add(conflict);
    }

    private async Task<bool> TryAdoptRemoteMoveAsync(TodoTask localTask, TodoList destination,
        RemoteTaskUpdate target, SyncRunResult result, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(localTask.RemoteHref)) return false;
        foreach (Account account in await _store.GetAccountsAsync(cancellationToken).ConfigureAwait(false))
        {
            TodoList? sourceList = (await _store.GetSyncListsAsync(account.Id, includeDeleted: true,
                    cancellationToken).ConfigureAwait(false)).FirstOrDefault(list => list.Id == localTask.ListId);
            if (sourceList is null || sourceList.IsDeleted || !sourceList.IsSyncEnabled) continue;
            string? secret = await _credentialStore.GetSecretAsync(account.CredentialKey,
                account.Username, cancellationToken).ConfigureAwait(false);
            if (secret is null) return false;
            var endpoint = new MoveEndpoint(account, sourceList, secret);
            CalDavContent? source = await ReadMoveResourceAsync(endpoint,
                new Uri(localTask.RemoteHref, UriKind.Absolute), cancellationToken).ConfigureAwait(false);
            if (source is not null) return false;
            RemotePullApplyResult applied = await _store.ApplyRemoteTaskMoveAsync(destination.Id, target,
                new RemoteTaskState
                {
                    Uid = localTask.Uid, RemoteHref = localTask.RemoteHref,
                    RemoteETag = localTask.RemoteETag, Sequence = localTask.Sequence,
                    IsDirty = localTask.IsDirty, IsDeleted = localTask.IsDeleted,
                }, cancellationToken).ConfigureAwait(false);
            result.PulledCount += applied.AppliedCount;
            if (applied.HasDeferredChanges || applied.Conflicts.Count > 0)
                result.Status = SyncRunStatus.AttentionRequired;
            return applied.AppliedCount > 0;
        }
        return false;
    }
}
