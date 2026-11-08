using Wintodo.Core.Models;

namespace Wintodo.Core.Services;

public interface ISyncStore
{
    Task<IReadOnlyList<TaskMoveIntent>> GetPendingTaskMovesAsync(
        long? accountId = null, CancellationToken cancellationToken = default);

    Task<TaskMoveIntent?> GetTaskMoveAsync(long id, CancellationToken cancellationToken = default);

    Task<TaskMoveIntent?> PrepareTaskMoveAttemptAsync(long id, int expectedSequence,
        string destinationHref, byte[] payload, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TaskMoveIntent>?> PrepareTaskMoveAttemptsAsync(
        IReadOnlyList<TaskMoveAttempt> attempts, CancellationToken cancellationToken = default);

    Task<bool> UpdateTaskMoveAsync(TaskMoveIntent intent, int expectedVersion,
        CancellationToken cancellationToken = default, int? expectedTaskSequence = null);

    Task<bool> UpdateTaskMoveGroupAsync(IReadOnlyList<TaskMoveUpdate> updates,
        CancellationToken cancellationToken = default);

    Task<bool> ReauthorizeTaskMoveResolutionAsync(IReadOnlyList<TaskMoveUpdate> updates,
        CancellationToken cancellationToken = default);

    Task<bool> ConfirmTaskMoveDestinationRestorationAsync(long id, int expectedVersion,
        string destinationETag, CancellationToken cancellationToken = default);

    Task<bool> ConfirmTaskMoveSourceRestorationAsync(long id, int expectedVersion,
        string sourceETag, CancellationToken cancellationToken = default);

    Task<bool> CompleteTaskMoveAsync(long id, int expectedVersion,
        CancellationToken cancellationToken = default);

    Task<bool> CompleteTaskMoveGroupAsync(IReadOnlyList<TaskMoveUpdate> updates,
        CancellationToken cancellationToken = default);

    Task<bool> RestoreTaskMoveSourceAsync(long id, int expectedVersion, TodoTask sourceTask,
        string sourceHref, string sourceETag, int expectedSequence,
        CancellationToken cancellationToken = default);

    Task<bool> RestoreTaskMoveSourcesAsync(IReadOnlyList<TaskMoveSourceRestore> restores,
        CancellationToken cancellationToken = default);

    /// <summary>Adopts a verified remote move only if its source binding and local revision still match.</summary>
    Task<RemotePullApplyResult> ApplyRemoteTaskMoveAsync(long destinationListId,
        RemoteTaskUpdate target, RemoteTaskState expectedLocal,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Account>> GetAccountsAsync(
        CancellationToken cancellationToken = default);

    Task<Account> SaveAccountAsync(
        Account account,
        CancellationToken cancellationToken = default);

    Task DeleteAccountAsync(
        long accountId,
        CancellationToken cancellationToken = default);

    Task UpdateAccountDiscoveryAsync(
        long accountId,
        string principalUrl,
        string homeSetUrl,
        CancellationToken cancellationToken = default);

    Task SetAccountLastSyncAsync(
        long accountId,
        DateTimeOffset value,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TodoList>> GetSyncListsAsync(
        long accountId,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default);

    Task<TodoList> UpsertRemoteListAsync(
        long accountId,
        string remoteUrl,
        string displayName,
        string? remoteCTag,
        string? syncToken,
        CancellationToken cancellationToken = default);

    Task SetListSyncMetadataAsync(
        long listId,
        long? accountId,
        string? remoteUrl,
        string? remoteCTag,
        string? syncToken,
        bool markListChangesSynced = false,
        CancellationToken cancellationToken = default);

    Task SetListSyncEnabledAsync(
        long listId,
        bool isEnabled,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SyncChange>> GetPendingSyncChangesAsync(
        long accountId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RemoteTaskState>> GetRemoteTaskStatesAsync(
        long listId,
        CancellationToken cancellationToken = default);

    Task<TodoTask?> GetTaskForSyncAsync(
        string uid,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TodoTask>> GetTaskFamilyForSyncAsync(string uid,
        CancellationToken cancellationToken = default);

    Task<bool> ResolveRemoteConflictAsync(SyncConflict conflict, SyncConflictChoice choice,
        CancellationToken cancellationToken = default);

    Task ApplyRemoteChangesAsync(
        long listId,
        IReadOnlyList<RemoteTaskUpdate> updates,
        IReadOnlyList<string> deletedHrefs,
        CancellationToken cancellationToken = default,
        bool deleteLocalSteps = false);

    /// <summary>
    /// Applies a pull only while its original task snapshots still match.
    /// Conflicts and deferred changes never acknowledge local change-log rows.
    /// </summary>
    Task<RemotePullApplyResult> ApplyRemotePullAsync(
        long listId,
        IReadOnlyList<RemoteTaskUpdate> updates,
        IReadOnlyList<string> deletedHrefs,
        IReadOnlyList<RemoteTaskState> expectedStates,
        CancellationToken cancellationToken = default);

    Task MarkTaskChangeSyncedAsync(
        long changeId,
        string uid,
        string? remoteHref,
        string? remoteETag,
        CancellationToken cancellationToken = default);

    Task MarkListChangeSyncedAsync(
        long changeId,
        string uid,
        string? remoteUrl,
        CancellationToken cancellationToken = default);

    Task SetTaskRemoteVersionAsync(
        string uid,
        string? remoteHref,
        string? remoteETag,
        CancellationToken cancellationToken = default,
        bool ensureLocalChange = false);

    Task<TodoTask> CreateConflictCopyAsync(
        TodoTask source,
        CancellationToken cancellationToken = default,
        bool preserveSteps = false);
}
