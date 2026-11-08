using System.Net;
using Wintodo.Core.Models;

namespace Wintodo.Sync;

public sealed partial class CalDavSyncService
{
    private async Task<bool> CaptureOriginalSourceForRestorationAsync(TaskMoveIntent move,
        CancellationToken cancellationToken)
    {
        if (move.SourceAccountId is null) return move.SourcePayload is not null;
        if (string.IsNullOrWhiteSpace(move.SourceHref)) return false;
        MoveEndpoint? endpoint = await GetMoveEndpointAsync(move.SourceAccountId.Value,
            move.SourceListId, cancellationToken).ConfigureAwait(false);
        if (endpoint is null) return false;
        CalDavContent? content = await ReadMoveResourceAsync(endpoint,
            new Uri(move.SourceHref, UriKind.Absolute), cancellationToken).ConfigureAwait(false);
        if (content is null) return move.Phase == TaskMovePhase.SourceRemoved && move.SourcePayload is not null;
        if (!IsStrongETag(move.SourceETag) || content.ETag != move.SourceETag
            || ReadMoveTaskOrNull(content.Content)?.Uid != move.TaskUid
            || move.SourcePayload is not null && !MovePayloadMatches(move.SourcePayload, content.Content)) return false;
        move.SourcePayload ??= content.Content;
        return true;
    }

    private async Task RestoreMoveSourceGroupAsync(List<TaskMoveIntent> moves, SyncRunResult result,
        HashSet<string> conflictedUids, CancellationToken cancellationToken)
    {
        var restored = new List<TaskMoveSourceRestore>();
        var sourcePayloads = new Dictionary<long, byte[]>();
        // Restore/confirm every source before compensating any destination.
        // This includes sources already removed by a partially completed family.
        foreach (TaskMoveIntent move in moves)
        {
            if (!await ValidateResolutionGroupAsync(moves, cancellationToken).ConfigureAwait(false))
            { result.Status = SyncRunStatus.AttentionRequired; return; }
            byte[]? payload = move.ResolutionIsDestinationConflict ? move.SourcePayload
                : move.ResolutionSourcePayload ?? move.SourcePayload;
            if (payload is null) { result.Status = SyncRunStatus.AttentionRequired; return; }
            TodoTask sourceTask = ReadSingleMoveTask(payload);
            if (sourceTask.Uid != move.TaskUid) { result.Status = SyncRunStatus.AttentionRequired; return; }
            string sourceHref = move.SourceHref ?? string.Empty;
            string sourceETag = string.Empty;
            if (move.SourceAccountId is not null)
            {
                MoveEndpoint? endpoint = await GetMoveEndpointAsync(move.SourceAccountId.Value,
                    move.SourceListId, cancellationToken).ConfigureAwait(false);
                if (endpoint is null || string.IsNullOrWhiteSpace(sourceHref))
                { result.Status = SyncRunStatus.AttentionRequired; return; }
                Uri href = new(sourceHref, UriKind.Absolute);
                CalDavContent? content = await ReadMoveResourceAsync(endpoint, href, cancellationToken)
                    .ConfigureAwait(false);
                string? expectedETag = move.ResolutionIsDestinationConflict ? move.SourceETag : move.ResolutionSourceETag;
                bool recreated = move.Phase == TaskMovePhase.SourceRemoved;
                if (content is null)
                {
                    if (!recreated) { result.Status = SyncRunStatus.AttentionRequired; return; }
                    CalDavWriteResult created = await _client.PutTaskAsync(endpoint.Account,
                        endpoint.Secret, href, payload, expectedETag: null, cancellationToken).ConfigureAwait(false);
                    if (!created.IsSuccess && !created.IsConflict) EnsureWriteSucceeded(created, "PUT", href);
                    content = await ReadMoveResourceAsync(endpoint, href, cancellationToken).ConfigureAwait(false);
                }
                if (content is null || !IsStrongETag(content.ETag) || !MovePayloadMatches(payload, content.Content)
                    || !recreated && (!IsStrongETag(expectedETag) || content.ETag != expectedETag))
                { result.Status = SyncRunStatus.AttentionRequired; return; }
                sourceETag = content.ETag!;
                if (recreated && !move.SourceRestored)
                {
                    if (!await _store.ConfirmTaskMoveSourceRestorationAsync(move.Id, move.Version,
                            sourceETag, cancellationToken).ConfigureAwait(false))
                    { result.Status = SyncRunStatus.AttentionRequired; return; }
                    move.SourceRestored = true;
                    move.RestoredSourceETag = sourceETag;
                    move.Version++;
                }
            }
            sourcePayloads[move.Id] = payload;
            restored.Add(new TaskMoveSourceRestore
            {
                MoveId = move.Id, SourceTask = sourceTask, SourceHref = sourceHref,
                SourceETag = sourceETag, ExpectedSequence = move.ResolutionSequence!.Value,
            });
        }

        foreach (TaskMoveIntent move in moves)
        {
            if (!await ValidateResolutionGroupAsync(moves, cancellationToken).ConfigureAwait(false))
            { result.Status = SyncRunStatus.AttentionRequired; return; }
            if (move.ResolutionDestinationRemoved) continue;
            if (move.DestinationAccountId is not null && move.AttemptedPayload is { Length: > 0 })
            {
                MoveEndpoint? endpoint = await GetMoveEndpointAsync(move.DestinationAccountId.Value,
                    move.DestinationListId, cancellationToken).ConfigureAwait(false);
                if (endpoint is null || string.IsNullOrWhiteSpace(move.DestinationHref))
                { result.Status = SyncRunStatus.AttentionRequired; return; }
                Uri href = new(move.DestinationHref, UriKind.Absolute);
                CalDavContent? content = await ReadMoveResourceAsync(endpoint, href, cancellationToken)
                    .ConfigureAwait(false);
                if (content is not null)
                {
                    bool owned = MovePayloadMatches(move.AttemptedPayload, content.Content);
                    if (owned)
                    {
                        if (!IsStrongETag(move.DestinationETag) || content.ETag != move.DestinationETag)
                        { result.Status = SyncRunStatus.AttentionRequired; return; }
                        CalDavWriteResult deleted = await _client.DeleteTaskAsync(endpoint.Account,
                            endpoint.Secret, href, move.DestinationETag, cancellationToken).ConfigureAwait(false);
                        if (!deleted.IsSuccess && deleted.StatusCode != HttpStatusCode.Gone)
                        { result.Status = SyncRunStatus.AttentionRequired; return; }
                    }
                    else if (!move.ResolutionIsDestinationConflict || move.ResolutionSourcePayload is null
                        || !MovePayloadMatches(move.ResolutionSourcePayload, content.Content)
                        || content.ETag != move.ResolutionSourceETag)
                    {
                        await AddMoveConflictAsync(move, endpoint, content, isDestination: true,
                            result, conflictedUids, cancellationToken).ConfigureAwait(false);
                        return;
                    }
                    // A competing resource was present when creation failed.
                    // Preserve it; it is not this move's destination copy.
                }
            }
            move.ResolutionDestinationRemoved = true;
            if (!await SaveMoveAsync(move, result, cancellationToken).ConfigureAwait(false)) return;
        }
        foreach (TaskMoveSourceRestore source in restored)
        {
            TaskMoveIntent move = moves.Single(value => value.Id == source.MoveId);
            if (move.SourceAccountId is not null)
            {
                MoveEndpoint? endpoint = await GetMoveEndpointAsync(move.SourceAccountId.Value,
                    move.SourceListId, cancellationToken).ConfigureAwait(false);
                if (endpoint is null) { result.Status = SyncRunStatus.AttentionRequired; return; }
                CalDavContent? content = await ReadMoveResourceAsync(endpoint,
                    new Uri(source.SourceHref, UriKind.Absolute), cancellationToken).ConfigureAwait(false);
                if (content is null || content.ETag != source.SourceETag
                    || !MovePayloadMatches(sourcePayloads[move.Id], content.Content))
                { result.Status = SyncRunStatus.AttentionRequired; return; }
            }
            source.ExpectedVersion = move.Version;
        }
        if (!await _store.RestoreTaskMoveSourcesAsync(restored, cancellationToken).ConfigureAwait(false))
        { result.Status = SyncRunStatus.AttentionRequired; return; }
        result.Status = SyncRunStatus.Success;
    }

    private async Task<bool> RestoreApprovedMoveDestinationAsync(TaskMoveIntent move, SyncRunResult result,
        HashSet<string> conflictedUids, CancellationToken cancellationToken)
    {
        if (move.DestinationAccountId is null || move.AttemptedPayload is not { Length: > 0 }) return true;
        MoveEndpoint? endpoint = await GetMoveEndpointAsync(move.DestinationAccountId.Value,
            move.DestinationListId, cancellationToken).ConfigureAwait(false);
        if (endpoint is null || move.DestinationHref is null)
        { result.Status = SyncRunStatus.AttentionRequired; return false; }
        Uri href = new(move.DestinationHref, UriKind.Absolute);
        CalDavContent? content = await ReadMoveResourceAsync(endpoint, href, cancellationToken).ConfigureAwait(false);
        if (content is null)
        {
            CalDavWriteResult created = await _client.PutTaskAsync(endpoint.Account, endpoint.Secret,
                href, move.AttemptedPayload, expectedETag: null, cancellationToken).ConfigureAwait(false);
            if (!created.IsSuccess && !created.IsConflict) EnsureWriteSucceeded(created, "PUT", href);
            content = await ReadMoveResourceAsync(endpoint, href, cancellationToken).ConfigureAwait(false);
        }
        if (content is null || !IsStrongETag(content.ETag)
            || !MovePayloadMatches(move.AttemptedPayload, content.Content))
        {
            await AddMoveConflictAsync(move, endpoint, content, isDestination: true,
                result, conflictedUids, cancellationToken).ConfigureAwait(false);
            return false;
        }
        if (!move.DestinationRemoved && !move.ResolutionDestinationRemoved
            && move.DestinationETag == content.ETag && move.Phase >= TaskMovePhase.DestinationConfirmed)
            return true;
        if (!await _store.ConfirmTaskMoveDestinationRestorationAsync(move.Id, move.Version,
                content.ETag!, cancellationToken).ConfigureAwait(false))
        { result.Status = SyncRunStatus.AttentionRequired; return false; }
        move.DestinationETag = content.ETag;
        move.DestinationRemoved = false;
        move.ResolutionDestinationRemoved = false;
        if (move.Phase < TaskMovePhase.DestinationConfirmed) move.Phase = TaskMovePhase.DestinationConfirmed;
        move.Version++;
        return true;
    }

    private async Task SurfaceResolutionConflictAsync(List<TaskMoveIntent> moves, SyncRunResult result,
        HashSet<string> conflictedUids, CancellationToken cancellationToken)
    {
        result.Status = SyncRunStatus.AttentionRequired;
        foreach (TaskMoveIntent move in moves)
        {
            bool destination = move.ResolutionIsDestinationConflict || move.SourceAccountId is null;
            long? accountId = destination ? move.DestinationAccountId : move.SourceAccountId;
            long listId = destination ? move.DestinationListId : move.SourceListId;
            string? href = destination ? move.DestinationHref : move.SourceHref;
            if (accountId is null || string.IsNullOrWhiteSpace(href)) continue;
            MoveEndpoint? endpoint = await GetMoveEndpointAsync(accountId.Value, listId, cancellationToken)
                .ConfigureAwait(false);
            if (endpoint is null) continue;
            CalDavContent? content = await ReadMoveResourceAsync(endpoint,
                new Uri(href, UriKind.Absolute), cancellationToken).ConfigureAwait(false);
            await AddMoveConflictAsync(move, endpoint, content, destination,
                result, conflictedUids, cancellationToken).ConfigureAwait(false);
            return;
        }
    }
}
