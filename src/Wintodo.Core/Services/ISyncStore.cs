using Wintodo.Core.Models;

namespace Wintodo.Core.Services;

public interface ISyncStore
{
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

    Task ApplyRemoteChangesAsync(
        long listId,
        IReadOnlyList<RemoteTaskUpdate> updates,
        IReadOnlyList<string> deletedHrefs,
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
        CancellationToken cancellationToken = default);

    Task<TodoTask> CreateConflictCopyAsync(
        TodoTask source,
        CancellationToken cancellationToken = default);
}
