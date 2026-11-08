using Wintodo.Core.Models;

namespace Wintodo.Sync;

public sealed partial class CalDavSyncService
{
    private async Task ResolveTaskMoveConflictAsync(SyncConflict conflict, SyncConflictChoice choice,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(choice)) throw new ArgumentOutOfRangeException(nameof(choice));
        var result = new SyncRunResult { Status = SyncRunStatus.AttentionRequired };
        var conflicted = new HashSet<string>(StringComparer.Ordinal);
        TaskMoveIntent? selected = await _store.GetTaskMoveAsync(conflict.TaskMoveId!.Value,
            cancellationToken).ConfigureAwait(false);
        if (selected is null) { RaiseStatus(result.Status, null); return; }
        List<TaskMoveIntent> moves = (await _store.GetPendingTaskMovesAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false)).Where(move => move.GroupId == selected.GroupId).OrderBy(move => move.Id).ToList();
        if (moves.Count == 0 || moves.Any(move => move.PredecessorId is not null))
        { RaiseStatus(result.Status, null); return; }
        bool renewing = selected.ResolutionChoice is not null;
        if (selected.ResolutionChoice == choice && conflict.MoveGroupSnapshots.Count == moves.Count
            && moves.All(move => conflict.MoveGroupSnapshots.Any(snapshot => snapshot.MoveId == move.Id
                && snapshot.LocalSequence == move.ResolutionSequence && snapshot.LocalListId == move.ResolutionListId
                && snapshot.RemoteETag == move.ResolutionSourceETag
                && (snapshot.RemotePayload is null ? move.ResolutionSourcePayload is null
                    : move.ResolutionSourcePayload is not null && MovePayloadMatches(snapshot.RemotePayload, move.ResolutionSourcePayload)))))
        {
            await ContinueTaskMoveResolutionAsync(moves, result, conflicted, cancellationToken)
                .ConfigureAwait(false);
            RaiseStatus(result.Status, result.ErrorMessage);
            return;
        }
        if (conflict.MoveGroupSnapshots.Count != moves.Count)
        { RaiseStatus(result.Status, null); return; }
        var snapshots = conflict.MoveGroupSnapshots.ToDictionary(snapshot => snapshot.MoveId);
        var updates = new List<TaskMoveUpdate>();
        var copies = new Dictionary<string, string>(StringComparer.Ordinal);
        if (renewing)
            foreach (TaskMoveIntent move in moves.Where(move => move.ResolutionCopyPayload is not null
                         && !move.ResolutionCopyConfirmed))
                if (!await ConfirmResolutionCopyAsync(move, moves, result, cancellationToken).ConfigureAwait(false))
                { RaiseStatus(result.Status, result.ErrorMessage); return; }
        if (choice == SyncConflictChoice.KeepBoth)
            foreach (TaskMoveIntent move in moves)
                if (snapshots.TryGetValue(move.Id, out TaskMoveConflictSnapshot? snapshot)
                    && snapshot.RemotePayload is not null)
                    copies[move.TaskUid] = move.ResolutionChoice == choice && move.ResolutionCopyUid is not null
                        && move.ResolutionSourcePayload is not null
                        && MovePayloadMatches(move.ResolutionSourcePayload, snapshot.RemotePayload)
                        ? move.ResolutionCopyUid : Guid.NewGuid().ToString("D");

        foreach (TaskMoveIntent move in moves)
        {
            if (!snapshots.TryGetValue(move.Id, out TaskMoveConflictSnapshot? snapshot))
            { RaiseStatus(result.Status, null); return; }
            TodoTask? task = await _store.GetTaskForSyncAsync(move.TaskUid, cancellationToken)
                .ConfigureAwait(false);
            if (task is null || task.Sequence != snapshot.LocalSequence || task.ListId != snapshot.LocalListId)
            { RaiseStatus(result.Status, null); return; }
            long? accountId = conflict.IsMoveDestinationConflict ? move.DestinationAccountId : move.SourceAccountId;
            long listId = conflict.IsMoveDestinationConflict ? move.DestinationListId : move.SourceListId;
            string? href = conflict.IsMoveDestinationConflict ? move.DestinationHref : move.SourceHref;
            MoveEndpoint? endpoint = accountId is null ? null
                : await GetMoveEndpointAsync(accountId.Value, listId, cancellationToken).ConfigureAwait(false);
            if (accountId is not null && endpoint is null)
            { RaiseStatus(result.Status, null); return; }
            if (endpoint is not null && !string.IsNullOrWhiteSpace(href))
            {
                CalDavContent? current = await ReadMoveResourceAsync(endpoint,
                    new Uri(href, UriKind.Absolute), cancellationToken).ConfigureAwait(false);
                if (snapshot.RemoteMissing ? current is not null
                    : current is null || current.ETag != snapshot.RemoteETag || snapshot.RemotePayload is null
                        || !MovePayloadMatches(snapshot.RemotePayload, current.Content))
                { RaiseStatus(result.Status, null); return; }
            }
            if (snapshot.RemotePayload is not null && ReadSingleMoveTask(snapshot.RemotePayload).Uid != move.TaskUid
                && !(choice == SyncConflictChoice.KeepTheirs && conflict.IsMoveDestinationConflict))
            { RaiseStatus(result.Status, null); return; }
            move.ResolutionChoice = choice;
            move.ResolutionSequence = task.Sequence;
            move.ResolutionListId = task.ListId;
            move.ResolutionIsDestinationConflict = conflict.IsMoveDestinationConflict;
            move.ResolutionSourcePayload = snapshot.RemotePayload;
            move.ResolutionSourceETag = snapshot.RemoteETag;
            if (choice == SyncConflictChoice.KeepTheirs && conflict.IsMoveDestinationConflict
                && !await CaptureOriginalSourceForRestorationAsync(move, cancellationToken).ConfigureAwait(false))
            { RaiseStatus(result.Status, null); return; }
            if (choice == SyncConflictChoice.KeepBoth && snapshot.RemotePayload is not null && endpoint is not null)
            {
                TodoTask copy = ReadSingleMoveTask(snapshot.RemotePayload);
                copy.Uid = copies[move.TaskUid];
                if (copy.ParentUid is not null && copies.TryGetValue(copy.ParentUid, out string? parentUid))
                    copy.ParentUid = parentUid;
                byte[] copyPayload = WriteMovePayload(endpoint.List, copy);
                if (copy.Uid == move.ResolutionCopyUid && move.ResolutionCopyPayload is not null
                    && !MovePayloadMatches(move.ResolutionCopyPayload, copyPayload))
                {
                    copy.Uid = Guid.NewGuid().ToString("D");
                    copies[move.TaskUid] = copy.Uid;
                    copyPayload = WriteMovePayload(endpoint.List, copy);
                }
                bool retainedCopy = copy.Uid == move.ResolutionCopyUid && move.ResolutionCopyConfirmed;
                move.ResolutionCopyUid = copy.Uid;
                move.ResolutionCopyHref = new Uri(EnsureTrailingSlash(new Uri(endpoint.List.RemoteUrl!, UriKind.Absolute)),
                    Uri.EscapeDataString(copy.Uid) + ".ics").AbsoluteUri;
                move.ResolutionCopyPayload = copyPayload;
                move.ResolutionCopyConfirmed = retainedCopy;
                if (!retainedCopy) move.ResolutionCopyETag = null;
            }
            else if (renewing)
            {
                move.ResolutionCopyUid = null;
                move.ResolutionCopyHref = null;
                move.ResolutionCopyPayload = null;
                move.ResolutionCopyETag = null;
                move.ResolutionCopyConfirmed = false;
            }
            updates.Add(new TaskMoveUpdate { Intent = move, ExpectedVersion = move.Version,
                ExpectedTaskSequence = task.Sequence });
        }
        bool accepted = renewing
            ? await _store.ReauthorizeTaskMoveResolutionAsync(updates, cancellationToken).ConfigureAwait(false)
            : await _store.UpdateTaskMoveGroupAsync(updates, cancellationToken).ConfigureAwait(false);
        if (!accepted)
        { RaiseStatus(result.Status, null); return; }
        foreach (TaskMoveUpdate update in updates) update.Intent.Version = update.ExpectedVersion + 1;
        await ContinueTaskMoveResolutionAsync(moves, result, conflicted, cancellationToken).ConfigureAwait(false);
        RaiseStatus(result.Status, result.ErrorMessage);
    }

    private async Task ContinueTaskMoveResolutionAsync(List<TaskMoveIntent> moves, SyncRunResult result,
        HashSet<string> conflictedUids, CancellationToken cancellationToken)
    {
        SyncConflictChoice? choice = moves[0].ResolutionChoice;
        if (choice is null || moves.Any(move => move.ResolutionChoice != choice)
            || !await ValidateResolutionGroupAsync(moves, cancellationToken).ConfigureAwait(false))
        {
            await SurfaceResolutionConflictAsync(moves, result, conflictedUids, cancellationToken)
                .ConfigureAwait(false);
            return;
        }
        if (choice == SyncConflictChoice.KeepTheirs)
        {
            await RestoreMoveSourceGroupAsync(moves, result, conflictedUids, cancellationToken)
                .ConfigureAwait(false);
            return;
        }
        // Every retained source version and child relation is durably planned
        // before any original UID is overwritten or removed.
        if (choice == SyncConflictChoice.KeepBoth)
            foreach (TaskMoveIntent move in moves.Where(move => move.ResolutionCopyPayload is not null))
                if (!await ConfirmResolutionCopyAsync(move, moves, result, cancellationToken)
                        .ConfigureAwait(false)) return;
        foreach (TaskMoveIntent move in moves)
        {
            if (!await ValidateResolutionGroupAsync(moves, cancellationToken).ConfigureAwait(false))
            { result.Status = SyncRunStatus.AttentionRequired; return; }
            if (move.ResolutionIsDestinationConflict && move.ResolutionSourcePayload is not null)
            {
                if (!await ReplaceMoveDestinationAsync(move, result, cancellationToken).ConfigureAwait(false)) return;
            }
            else if (move.Phase < TaskMovePhase.DestinationConfirmed)
            {
                if (!await ConfirmMoveDestinationAsync(move, result, conflictedUids, cancellationToken)
                        .ConfigureAwait(false)) return;
            }
            TodoTask? currentTask = await _store.GetTaskForSyncAsync(move.TaskUid, cancellationToken)
                .ConfigureAwait(false);
            if (currentTask is not null && await IsMoveTaskDeletedAsync(currentTask, cancellationToken)
                    .ConfigureAwait(false))
            {
                if (!move.DestinationRemoved && move.AttemptedPayload is { Length: > 0 }
                    && !await RemoveMoveDestinationAsync(move, result, conflictedUids, cancellationToken)
                        .ConfigureAwait(false)) return;
            }
            else if (!await RestoreApprovedMoveDestinationAsync(move, result, conflictedUids, cancellationToken)
                    .ConfigureAwait(false))
            {
                return;
            }
            if (!await VerifyRetainedDestinationAsync(move, result, cancellationToken, conflictedUids).ConfigureAwait(false)) return;
        }
        foreach (TaskMoveIntent move in moves.Where(move => move.Phase < TaskMovePhase.SourceRemoved || move.SourceRestored))
        {
            if (move.SourceAccountId is null || string.IsNullOrWhiteSpace(move.SourceHref))
            {
                move.Phase = TaskMovePhase.SourceRemoved;
                if (!await SaveMoveAsync(move, result, cancellationToken).ConfigureAwait(false)) return;
                continue;
            }
            MoveEndpoint? endpoint = await GetMoveEndpointAsync(move.SourceAccountId.Value,
                move.SourceListId, cancellationToken).ConfigureAwait(false);
            if (endpoint is null) { result.Status = SyncRunStatus.AttentionRequired; return; }
            CalDavContent? source = await ReadMoveResourceAsync(endpoint,
                new Uri(move.SourceHref, UriKind.Absolute), cancellationToken).ConfigureAwait(false);
            if (source is null)
            {
                move.Phase = TaskMovePhase.SourceRemoved;
                move.SourceRestored = false;
                if (!await SaveMoveAsync(move, result, cancellationToken).ConfigureAwait(false)) return;
                continue;
            }
            string? expectedETag = move.ResolutionIsDestinationConflict
                ? move.SourceRestored ? move.RestoredSourceETag : move.SourceETag : move.ResolutionSourceETag;
            byte[]? expectedPayload = move.ResolutionIsDestinationConflict ? move.SourcePayload : move.ResolutionSourcePayload;
            if (!IsStrongETag(expectedETag) || source.ETag != expectedETag
                || ReadMoveTaskOrNull(source.Content)?.Uid != move.TaskUid
                || expectedPayload is not null && !MovePayloadMatches(expectedPayload, source.Content))
            {
                await AddMoveConflictAsync(move, endpoint, source, isDestination: false,
                    result, conflictedUids, cancellationToken).ConfigureAwait(false);
                return;
            }
        }
        if (!await ValidateResolutionGroupAsync(moves, cancellationToken).ConfigureAwait(false))
        { result.Status = SyncRunStatus.AttentionRequired; return; }
        await RemoveMoveSourcesAndCompleteAsync(moves, result, conflictedUids, cancellationToken)
            .ConfigureAwait(false);
        if (result.Status == SyncRunStatus.AttentionRequired
            && (await _store.GetPendingTaskMovesAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false)).All(move => moves.All(member => member.Id != move.Id)))
            result.Status = SyncRunStatus.Success;
    }

    private async Task<bool> ValidateResolutionGroupAsync(List<TaskMoveIntent> moves,
        CancellationToken cancellationToken)
    {
        foreach (TaskMoveIntent move in moves)
        {
            TodoTask? task = await _store.GetTaskForSyncAsync(move.TaskUid, cancellationToken).ConfigureAwait(false);
            if (task is null || move.ResolutionSequence is null || task.Sequence < move.ResolutionSequence
                || move.ResolutionChoice == SyncConflictChoice.KeepTheirs
                    && (task.Sequence != move.ResolutionSequence || task.ListId != move.ResolutionListId))
                return false;
        }
        return true;
    }

    private async Task<bool> ConfirmResolutionCopyAsync(TaskMoveIntent move, List<TaskMoveIntent> group,
        SyncRunResult result, CancellationToken cancellationToken)
    {
        if (move.ResolutionCopyConfirmed) return true;
        if (!await ValidateResolutionGroupAsync(group, cancellationToken).ConfigureAwait(false))
        { result.Status = SyncRunStatus.AttentionRequired; return false; }
        long? accountId = move.ResolutionIsDestinationConflict ? move.DestinationAccountId : move.SourceAccountId;
        long listId = move.ResolutionIsDestinationConflict ? move.DestinationListId : move.SourceListId;
        MoveEndpoint? endpoint = accountId is null ? null
            : await GetMoveEndpointAsync(accountId.Value, listId, cancellationToken).ConfigureAwait(false);
        if (endpoint is null || move.ResolutionCopyHref is null || move.ResolutionCopyPayload is null)
        { result.Status = SyncRunStatus.AttentionRequired; return false; }
        Uri href = new(move.ResolutionCopyHref, UriKind.Absolute);
        CalDavContent? content = await ReadMoveResourceAsync(endpoint, href, cancellationToken).ConfigureAwait(false);
        if (content is null)
        {
            CalDavWriteResult created = await _client.PutTaskAsync(endpoint.Account, endpoint.Secret, href,
                move.ResolutionCopyPayload, expectedETag: null, cancellationToken).ConfigureAwait(false);
            if (!created.IsSuccess && !created.IsConflict) EnsureWriteSucceeded(created, "PUT", href);
            content = await ReadMoveResourceAsync(endpoint, href, cancellationToken).ConfigureAwait(false);
        }
        if (content is null || !IsStrongETag(content.ETag)
            || !MovePayloadMatches(move.ResolutionCopyPayload, content.Content))
        { result.Status = SyncRunStatus.AttentionRequired; return false; }
        move.ResolutionCopyETag = content.ETag;
        move.ResolutionCopyConfirmed = true;
        return await SaveMoveAsync(move, result, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> ReplaceMoveDestinationAsync(TaskMoveIntent move, SyncRunResult result,
        CancellationToken cancellationToken)
    {
        MoveEndpoint? endpoint = move.DestinationAccountId is null ? null
            : await GetMoveEndpointAsync(move.DestinationAccountId.Value, move.DestinationListId,
                cancellationToken).ConfigureAwait(false);
        if (endpoint is null || move.DestinationHref is null || move.AttemptedPayload is null)
        { result.Status = SyncRunStatus.AttentionRequired; return false; }
        Uri href = new(move.DestinationHref, UriKind.Absolute);
        CalDavContent? content = await ReadMoveResourceAsync(endpoint, href, cancellationToken).ConfigureAwait(false);
        if (content is null)
            return await RestoreApprovedMoveDestinationAsync(move, result,
                new HashSet<string>(StringComparer.Ordinal), cancellationToken).ConfigureAwait(false);
        if (!MovePayloadMatches(move.AttemptedPayload, content.Content))
        {
            if (!IsStrongETag(move.ResolutionSourceETag) || content.ETag != move.ResolutionSourceETag
                || move.ResolutionSourcePayload is null || !MovePayloadMatches(move.ResolutionSourcePayload, content.Content)
                || ReadSingleMoveTask(content.Content).Uid != move.TaskUid)
            { result.Status = SyncRunStatus.AttentionRequired; return false; }
            CalDavWriteResult changed = await _client.PutTaskAsync(endpoint.Account, endpoint.Secret,
                href, move.AttemptedPayload, move.ResolutionSourceETag, cancellationToken).ConfigureAwait(false);
            if (!changed.IsSuccess) { result.Status = SyncRunStatus.AttentionRequired; return false; }
            content = await ReadMoveResourceAsync(endpoint, href, cancellationToken).ConfigureAwait(false);
        }
        if (content is null || !IsStrongETag(content.ETag)
            || !MovePayloadMatches(move.AttemptedPayload, content.Content))
        { result.Status = SyncRunStatus.AttentionRequired; return false; }
        move.DestinationETag = content.ETag;
        if (move.Phase < TaskMovePhase.DestinationConfirmed) move.Phase = TaskMovePhase.DestinationConfirmed;
        return await SaveMoveAsync(move, result, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> VerifyRetainedDestinationAsync(TaskMoveIntent move, SyncRunResult result,
        CancellationToken cancellationToken, HashSet<string> conflictedUids)
    {
        if (move.DestinationAccountId is null || move.DestinationRemoved || move.AttemptedPayload is not { Length: > 0 })
            return true;
        MoveEndpoint? endpoint = await GetMoveEndpointAsync(move.DestinationAccountId.Value,
            move.DestinationListId, cancellationToken).ConfigureAwait(false);
        if (endpoint is null || move.DestinationHref is null)
        { result.Status = SyncRunStatus.AttentionRequired; return false; }
        CalDavContent? content = await ReadMoveResourceAsync(endpoint,
            new Uri(move.DestinationHref, UriKind.Absolute), cancellationToken).ConfigureAwait(false);
        if (content is null || !IsStrongETag(content.ETag) || !MovePayloadMatches(move.AttemptedPayload, content.Content))
        {
            result.Status = SyncRunStatus.AttentionRequired;
            await AddMoveConflictAsync(move, endpoint, content, isDestination: true,
                result, conflictedUids, cancellationToken).ConfigureAwait(false);
            return false;
        }
        if (content.ETag != move.DestinationETag)
        {
            move.DestinationETag = content.ETag;
            return await SaveMoveAsync(move, result, cancellationToken).ConfigureAwait(false);
        }
        return true;
    }
}
