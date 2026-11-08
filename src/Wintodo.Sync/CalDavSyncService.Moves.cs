using System.Net;
using System.Net.Http.Headers;
using Wintodo.Core.Ical;
using Wintodo.Core.Models;
using Wintodo.Core.Services;

namespace Wintodo.Sync;

public sealed partial class CalDavSyncService
{
    private sealed record MoveEndpoint(Account Account, TodoList List, string Secret);

    private async Task ExecuteTaskMovesAsync(long accountId, SyncRunResult result,
        HashSet<string> conflictedUids, CancellationToken cancellationToken)
    {
        IReadOnlyList<TaskMoveIntent> pending = await _store.GetPendingTaskMovesAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        foreach (IGrouping<string, TaskMoveIntent> group in pending.GroupBy(move => move.GroupId)
                     .Where(group => group.Any(move => move.SourceAccountId == accountId || move.DestinationAccountId == accountId)))
        {
            var moves = new List<TaskMoveIntent>();
            foreach (TaskMoveIntent observed in group.OrderBy(move => move.Id))
            {
                TaskMoveIntent? current = await _store.GetTaskMoveAsync(observed.Id, cancellationToken)
                    .ConfigureAwait(false);
                if (current is not null) moves.Add(current);
            }
            if (moves.Count == 0) continue;
            if (moves.Any(move => move.PredecessorId is not null))
            {
                result.Status = SyncRunStatus.AttentionRequired;
                continue;
            }
            try
            {
                await ExecuteTaskMoveGroupAsync(moves, result, conflictedUids, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                result.Status = SyncRunStatus.Offline;
            }
            catch (HttpRequestException exception)
            {
                result.Status = SyncRunStatus.Offline;
                result.ErrorMessage = DiagnosticException.Describe(exception);
            }
            catch (CalDavProtocolException exception)
            {
                result.Status = SyncRunStatus.Error;
                result.ErrorMessage = DiagnosticException.Describe(exception, exception.StatusCode);
            }
        }
        if ((await _store.GetPendingTaskMovesAsync(accountId, cancellationToken)
                .ConfigureAwait(false)).Count > 0 && result.Status == SyncRunStatus.Success)
            result.Status = SyncRunStatus.AttentionRequired;
    }

    private async Task ExecuteTaskMoveGroupAsync(List<TaskMoveIntent> moves, SyncRunResult result,
        HashSet<string> conflictedUids, CancellationToken cancellationToken)
    {
        if (moves.Any(move => move.SourceHref is not null && !move.SourceBindingVerified))
        { result.Status = SyncRunStatus.AttentionRequired; return; }
        if (moves.Any(move => move.ResolutionChoice is not null))
        {
            await ContinueTaskMoveResolutionAsync(moves, result, conflictedUids, cancellationToken)
                .ConfigureAwait(false);
            return;
        }
        var attempts = new List<TaskMoveAttempt>();
        foreach (TaskMoveIntent move in moves.Where(move => move.Phase == TaskMovePhase.Planned))
        {
            TodoTask? task = await _store.GetTaskForSyncAsync(move.TaskUid, cancellationToken)
                .ConfigureAwait(false);
            if (task is null) { result.Status = SyncRunStatus.AttentionRequired; return; }
            MoveEndpoint? destination = move.DestinationAccountId is null ? null
                : await GetMoveEndpointAsync(move.DestinationAccountId.Value, move.DestinationListId,
                    cancellationToken).ConfigureAwait(false);
            if (move.DestinationAccountId is not null && destination is null)
            { result.Status = SyncRunStatus.AttentionRequired; return; }
            string href = destination is null ? string.Empty
                : new Uri(EnsureTrailingSlash(new Uri(destination.List.RemoteUrl!, UriKind.Absolute)),
                    Uri.EscapeDataString(task.Uid) + ".ics").AbsoluteUri;
            attempts.Add(new TaskMoveAttempt
            {
                MoveId = move.Id, ExpectedSequence = task.Sequence, DestinationHref = href,
                Payload = destination is null || await IsMoveTaskDeletedAsync(task, cancellationToken)
                    .ConfigureAwait(false) ? [] : WriteMovePayload(destination.List, task),
            });
        }
        if (attempts.Count > 0)
        {
            IReadOnlyList<TaskMoveIntent>? prepared = await _store.PrepareTaskMoveAttemptsAsync(attempts,
                cancellationToken).ConfigureAwait(false);
            if (prepared is null) { result.Status = SyncRunStatus.AttentionRequired; return; }
            foreach (TaskMoveIntent preparedMove in prepared)
            {
                int index = moves.FindIndex(move => move.Id == preparedMove.Id);
                if (index >= 0) moves[index] = preparedMove;
            }
        }

        // Freeze every child's attempt before the first remote write. A subsequent
        // parent move/undo can then append a complete, ordered successor group.
        foreach (TaskMoveIntent move in moves)
        {
            if (move.Phase >= TaskMovePhase.DestinationConfirmed) continue;
            if (!await ConfirmMoveDestinationAsync(move, result, conflictedUids, cancellationToken)
                    .ConfigureAwait(false)) return;
        }

        foreach (TaskMoveIntent move in moves)
        {
            TodoTask? task = await _store.GetTaskForSyncAsync(move.TaskUid, cancellationToken)
                .ConfigureAwait(false);
            if (task is not null && await IsMoveTaskDeletedAsync(task, cancellationToken).ConfigureAwait(false)
                && !move.DestinationRemoved
                && move.AttemptedPayload is { Length: > 0 })
            {
                if (!await RemoveMoveDestinationAsync(move, result, conflictedUids, cancellationToken)
                        .ConfigureAwait(false)) return;
            }
        }
        foreach (TaskMoveIntent move in moves)
            if (!await VerifyRetainedDestinationAsync(move, result, cancellationToken, conflictedUids)
                    .ConfigureAwait(false)) return;
        if (!await CaptureMoveSourcesAsync(moves, result, conflictedUids, cancellationToken)
                .ConfigureAwait(false)) return;
        if (!await EnsureLiveMoveDestinationsAsync(moves, result, conflictedUids, cancellationToken)
                .ConfigureAwait(false)) return;
        await RemoveMoveSourcesAndCompleteAsync(moves, result, conflictedUids, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<MoveEndpoint?> GetMoveEndpointAsync(long accountId, long listId,
        CancellationToken cancellationToken)
    {
        Account? account = (await _store.GetAccountsAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(value => value.Id == accountId);
        if (account is null) return null;
        TodoList? list = (await _store.GetSyncListsAsync(accountId, includeDeleted: true,
                cancellationToken).ConfigureAwait(false)).FirstOrDefault(value => value.Id == listId);
        if (list is null || list.IsDeleted || !list.IsSyncEnabled) return null;
        string? secret = await _credentialStore.GetSecretAsync(account.CredentialKey,
            account.Username, cancellationToken).ConfigureAwait(false);
        if (secret is null) return null;
        if (string.IsNullOrWhiteSpace(list.RemoteUrl))
        {
            await EnsureDiscoveryAsync(account, secret, cancellationToken).ConfigureAwait(false);
            Uri url = await _client.CreateCollectionAsync(account, secret, list.Uid,
                list.DisplayName, cancellationToken).ConfigureAwait(false);
            await _store.SetListSyncMetadataAsync(list.Id, account.Id, url.AbsoluteUri, null, null,
                markListChangesSynced: true, cancellationToken).ConfigureAwait(false);
            list.RemoteUrl = url.AbsoluteUri;
        }
        return new MoveEndpoint(account, list, secret);
    }

    private async Task<bool> ConfirmMoveDestinationAsync(TaskMoveIntent move, SyncRunResult result,
        HashSet<string> conflictedUids, CancellationToken cancellationToken)
    {
        if (move.AttemptedPayload is null)
        { result.Status = SyncRunStatus.AttentionRequired; return false; }
        if (move.AttemptedPayload.Length == 0)
        {
            move.Phase = TaskMovePhase.DestinationConfirmed;
            return await SaveMoveAsync(move, result, cancellationToken).ConfigureAwait(false);
        }
        MoveEndpoint? endpoint = move.DestinationAccountId is null ? null
            : await GetMoveEndpointAsync(move.DestinationAccountId.Value, move.DestinationListId,
                cancellationToken).ConfigureAwait(false);
        if (endpoint is null || string.IsNullOrWhiteSpace(move.DestinationHref))
        { result.Status = SyncRunStatus.AttentionRequired; return false; }
        Uri href = new(move.DestinationHref, UriKind.Absolute);
        CalDavContent? content = await ReadMoveResourceAsync(endpoint, href, cancellationToken)
            .ConfigureAwait(false);
        if (content is null)
        {
            CalDavWriteResult created = await _client.PutTaskAsync(endpoint.Account, endpoint.Secret,
                href, move.AttemptedPayload, expectedETag: null, cancellationToken).ConfigureAwait(false);
            if (!created.IsSuccess && !created.IsConflict) EnsureWriteSucceeded(created, "PUT", href);
            content = await ReadMoveResourceAsync(endpoint, href, cancellationToken).ConfigureAwait(false);
        }
        if (content is null) { result.Status = SyncRunStatus.AttentionRequired; return false; }
        if (!MovePayloadMatches(move.AttemptedPayload, content.Content))
        {
            await AddMoveConflictAsync(move, endpoint, content, isDestination: true,
                result, conflictedUids, cancellationToken).ConfigureAwait(false);
            return false;
        }
        if (!IsStrongETag(content.ETag))
        { result.Status = SyncRunStatus.AttentionRequired; return false; }
        move.DestinationETag = content.ETag;
        move.Phase = TaskMovePhase.DestinationConfirmed;
        return await SaveMoveAsync(move, result, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> CaptureMoveSourcesAsync(List<TaskMoveIntent> moves, SyncRunResult result,
        HashSet<string> conflictedUids, CancellationToken cancellationToken)
    {
        var updates = new List<TaskMoveUpdate>();
        foreach (TaskMoveIntent move in moves.Where(move => move.Phase < TaskMovePhase.SourceRemoved || move.SourceRestored))
        {
            if (string.IsNullOrWhiteSpace(move.SourceHref) || move.SourceAccountId is null)
            {
                move.Phase = TaskMovePhase.SourceRemoved;
            }
            else
            {
                MoveEndpoint? endpoint = await GetMoveEndpointAsync(move.SourceAccountId.Value,
                    move.SourceListId, cancellationToken).ConfigureAwait(false);
                if (endpoint is null) { result.Status = SyncRunStatus.AttentionRequired; return false; }
                CalDavContent? content = await ReadMoveResourceAsync(endpoint,
                    new Uri(move.SourceHref, UriKind.Absolute), cancellationToken).ConfigureAwait(false);
                if (content is null) move.Phase = TaskMovePhase.SourceRemoved;
                else if (!IsStrongETag(move.SourceETag)
                    || !string.Equals(move.SourceETag, content.ETag, StringComparison.Ordinal)
                    || ReadMoveTaskOrNull(content.Content)?.Uid != move.TaskUid
                    || move.SourcePayload is not null && !MovePayloadMatches(move.SourcePayload, content.Content))
                {
                    await AddMoveConflictAsync(move, endpoint, content, isDestination: false,
                        result, conflictedUids, cancellationToken).ConfigureAwait(false);
                    return false;
                }
                else move.SourcePayload ??= content.Content;
            }
            updates.Add(new TaskMoveUpdate { Intent = move, ExpectedVersion = move.Version });
        }
        if (updates.Count == 0) return true;
        if (!await _store.UpdateTaskMoveGroupAsync(updates, cancellationToken).ConfigureAwait(false))
        { result.Status = SyncRunStatus.AttentionRequired; return false; }
        foreach (TaskMoveUpdate update in updates) update.Intent.Version = update.ExpectedVersion + 1;
        return true;
    }

    private async Task RemoveMoveSourcesAndCompleteAsync(List<TaskMoveIntent> moves, SyncRunResult result,
        HashSet<string> conflictedUids, CancellationToken cancellationToken)
    {
        foreach (long id in moves.Where(move => move.Phase < TaskMovePhase.SourceRemoved || move.SourceRestored)
                     .Select(move => move.Id).ToArray())
        {
            if (!await EnsureLiveMoveDestinationsAsync(moves, result, conflictedUids, cancellationToken)
                    .ConfigureAwait(false)) return;
            TaskMoveIntent move = moves.Single(member => member.Id == id);
            if (moves.Any(member => member.ResolutionChoice is not null)
                && !await ValidateResolutionGroupAsync(moves, cancellationToken).ConfigureAwait(false))
            { result.Status = SyncRunStatus.AttentionRequired; return; }
            foreach (TaskMoveIntent member in moves)
                if (!await VerifyRetainedDestinationAsync(member, result, cancellationToken, conflictedUids)
                        .ConfigureAwait(false)) return;
            MoveEndpoint? endpoint = move.SourceAccountId is null ? null
                : await GetMoveEndpointAsync(move.SourceAccountId.Value, move.SourceListId,
                    cancellationToken).ConfigureAwait(false);
            if (endpoint is null || string.IsNullOrWhiteSpace(move.SourceHref))
            { result.Status = SyncRunStatus.AttentionRequired; return; }
            Uri sourceHref = new(move.SourceHref, UriKind.Absolute);
            string? etag = move.ResolutionChoice is not null && !move.ResolutionIsDestinationConflict
                ? move.ResolutionSourceETag : move.SourceRestored ? move.RestoredSourceETag : move.SourceETag;
            if (!IsStrongETag(etag)) { result.Status = SyncRunStatus.AttentionRequired; return; }
            if (!await EnsureLiveMoveDestinationsAsync(moves, result, conflictedUids, cancellationToken)
                    .ConfigureAwait(false)) return;
            move = moves.Single(member => member.Id == id);
            CalDavWriteResult deleted = await _client.DeleteTaskAsync(endpoint.Account, endpoint.Secret,
                sourceHref, etag, cancellationToken).ConfigureAwait(false);
            if (deleted.IsConflict)
            {
                CalDavContent? content = await ReadMoveResourceAsync(endpoint,
                    sourceHref, cancellationToken).ConfigureAwait(false);
                if (content is not null)
                {
                    await AddMoveConflictAsync(move, endpoint, content, isDestination: false,
                        result, conflictedUids, cancellationToken).ConfigureAwait(false);
                    return;
                }
            }
            else if (!deleted.IsSuccess && deleted.StatusCode != HttpStatusCode.Gone)
                EnsureWriteSucceeded(deleted, "DELETE", sourceHref);
            move.Phase = TaskMovePhase.SourceRemoved;
            move.SourceRestored = false;
            if (!await SaveMoveAsync(move, result, cancellationToken).ConfigureAwait(false)) return;
        }
        if (!await EnsureLiveMoveDestinationsAsync(moves, result, conflictedUids, cancellationToken)
                .ConfigureAwait(false)) return;
        if (moves.Any(move => move.Phase < TaskMovePhase.SourceRemoved || move.SourceRestored))
        {
            // A restore after an in-flight DELETE can re-freeze an empty placeholder.
            // Confirm the now-live destination, then recover the already-completed removal.
            if (!await CaptureMoveSourcesAsync(moves, result, conflictedUids, cancellationToken)
                    .ConfigureAwait(false)) return;
            if (moves.Any(move => move.Phase < TaskMovePhase.SourceRemoved || move.SourceRestored))
            { result.Status = SyncRunStatus.AttentionRequired; return; }
        }
        foreach (TaskMoveIntent move in moves)
            if (!await VerifyRetainedDestinationAsync(move, result, cancellationToken, conflictedUids)
                    .ConfigureAwait(false)) return;
        // Retain the complete family until every source obligation is confirmed.
        if (!await _store.CompleteTaskMoveGroupAsync(moves.Select(move => new TaskMoveUpdate
                { Intent = move, ExpectedVersion = move.Version }).ToArray(), cancellationToken)
                .ConfigureAwait(false))
        { result.Status = SyncRunStatus.AttentionRequired; return; }
        result.PushedCount += moves.Count;
    }

    private async Task<bool> RemoveMoveDestinationAsync(TaskMoveIntent move, SyncRunResult result,
        HashSet<string> conflictedUids, CancellationToken cancellationToken)
    {
        MoveEndpoint? endpoint = move.DestinationAccountId is null ? null
            : await GetMoveEndpointAsync(move.DestinationAccountId.Value, move.DestinationListId,
                cancellationToken).ConfigureAwait(false);
        if (endpoint is null || string.IsNullOrWhiteSpace(move.DestinationHref))
        { result.Status = SyncRunStatus.AttentionRequired; return false; }
        Uri href = new(move.DestinationHref, UriKind.Absolute);
        CalDavContent? content = await ReadMoveResourceAsync(endpoint, href, cancellationToken)
            .ConfigureAwait(false);
        if (content is not null)
        {
            if (!IsStrongETag(move.DestinationETag) || content.ETag != move.DestinationETag
                || move.AttemptedPayload is null || !MovePayloadMatches(move.AttemptedPayload, content.Content))
            {
                await AddMoveConflictAsync(move, endpoint, content, isDestination: true,
                    result, conflictedUids, cancellationToken).ConfigureAwait(false);
                return false;
            }
            CalDavWriteResult deleted = await _client.DeleteTaskAsync(endpoint.Account, endpoint.Secret,
                href, move.DestinationETag, cancellationToken).ConfigureAwait(false);
            if (!deleted.IsSuccess && deleted.StatusCode != HttpStatusCode.Gone)
            {
                if (deleted.IsConflict) result.Status = SyncRunStatus.AttentionRequired;
                else EnsureWriteSucceeded(deleted, "DELETE", href);
                return false;
            }
        }
        move.DestinationRemoved = true;
        return await SaveMoveAsync(move, result, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> SaveMoveAsync(TaskMoveIntent move, SyncRunResult result,
        CancellationToken cancellationToken, int? expectedSequence = null)
    {
        int version = move.Version;
        if (!await _store.UpdateTaskMoveAsync(move, version, cancellationToken, expectedSequence)
                .ConfigureAwait(false))
        { result.Status = SyncRunStatus.AttentionRequired; return false; }
        move.Version = version + 1;
        return true;
    }

    private async Task<CalDavContent?> ReadMoveResourceAsync(MoveEndpoint endpoint, Uri href,
        CancellationToken cancellationToken)
    {
        try { return await _client.GetTaskAsync(endpoint.Account, endpoint.Secret, href,
            cancellationToken).ConfigureAwait(false); }
        catch (CalDavProtocolException exception) when (exception.StatusCode is HttpStatusCode.NotFound
            or HttpStatusCode.Gone) { return null; }
    }

    private static bool IsStrongETag(string? value) => EntityTagHeaderValue.TryParse(value,
        out EntityTagHeaderValue? parsed) && !parsed.IsWeak && parsed.Tag != "*";

    private async Task<bool> IsMoveTaskDeletedAsync(TodoTask task, CancellationToken cancellationToken)
        => task.IsDeleted || task.ParentUid is not null
            && (await _store.GetTaskForSyncAsync(task.ParentUid, cancellationToken).ConfigureAwait(false))?.IsDeleted == true;
}
