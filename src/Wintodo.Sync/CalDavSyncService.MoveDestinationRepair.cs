using Wintodo.Core.Models;

namespace Wintodo.Sync;

public sealed partial class CalDavSyncService
{
    private async Task<bool> EnsureLiveMoveDestinationsAsync(List<TaskMoveIntent> moves, SyncRunResult result,
        HashSet<string> conflictedUids, CancellationToken cancellationToken)
    {
        var current = new Dictionary<string, TodoTask>(StringComparer.Ordinal);
        var live = new HashSet<string>(StringComparer.Ordinal);
        foreach (TaskMoveIntent move in moves)
        {
            TodoTask? task = await _store.GetTaskForSyncAsync(move.TaskUid, cancellationToken).ConfigureAwait(false);
            if (task is null) { result.Status = SyncRunStatus.AttentionRequired; return false; }
            current.Add(task.Uid, task);
            if (!await IsMoveTaskDeletedAsync(task, cancellationToken).ConfigureAwait(false)) live.Add(task.Uid);
        }
        if (moves.Any(move => move.DestinationAccountId is not null && live.Contains(move.TaskUid)
            && move.AttemptedPayload is { Length: 0 }))
        {
            var attempts = new List<TaskMoveAttempt>();
            foreach (TaskMoveIntent move in moves)
            {
                TodoTask task = current[move.TaskUid];
                byte[] payload = move.AttemptedPayload ?? [];
                if (move.DestinationAccountId is not null && live.Contains(task.Uid) && payload.Length == 0)
                {
                    MoveEndpoint? endpoint = await GetMoveEndpointAsync(move.DestinationAccountId.Value,
                        move.DestinationListId, cancellationToken).ConfigureAwait(false);
                    if (endpoint is null || string.IsNullOrWhiteSpace(move.DestinationHref))
                    { result.Status = SyncRunStatus.AttentionRequired; return false; }
                    payload = WriteMovePayload(endpoint.List, task);
                }
                attempts.Add(new TaskMoveAttempt { MoveId = move.Id, ExpectedSequence = task.Sequence,
                    DestinationHref = move.DestinationHref ?? string.Empty, Payload = payload });
            }
            IReadOnlyList<TaskMoveIntent>? prepared = await _store.PrepareTaskMoveAttemptsAsync(attempts,
                cancellationToken).ConfigureAwait(false);
            if (prepared is null) { result.Status = SyncRunStatus.AttentionRequired; return false; }
            foreach (TaskMoveIntent intent in prepared)
            {
                int index = moves.FindIndex(move => move.Id == intent.Id);
                if (index >= 0) moves[index] = intent;
            }
        }
        foreach (TaskMoveIntent move in moves)
        {
            if (move.Phase < TaskMovePhase.DestinationConfirmed
                && !await ConfirmMoveDestinationAsync(move, result, conflictedUids, cancellationToken)
                    .ConfigureAwait(false)) return false;
            if (move.DestinationAccountId is not null && live.Contains(move.TaskUid)
                && (move.DestinationRemoved || move.ResolutionDestinationRemoved)
                && !await RestoreApprovedMoveDestinationAsync(move, result, conflictedUids, cancellationToken)
                    .ConfigureAwait(false)) return false;
        }
        foreach (TaskMoveIntent move in moves)
            if (!await VerifyRetainedDestinationAsync(move, result, cancellationToken, conflictedUids)
                    .ConfigureAwait(false)) return false;
        return true;
    }
}
