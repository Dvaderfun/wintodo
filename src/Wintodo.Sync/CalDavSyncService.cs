using System.Net;
using Wintodo.Core.Ical;
using Wintodo.Core.Models;
using Wintodo.Core.Services;

namespace Wintodo.Sync;

public sealed class CalDavSyncService : IDisposable
{
    private readonly ISyncStore _store;
    private readonly ICredentialStore _credentialStore;
    private readonly IClock _clock;
    private readonly CalDavClient _client;
    private readonly IcalReader _reader;
    private readonly IcalWriter _writer;
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private int _disposed;

    public CalDavSyncService(
        ISyncStore store,
        ICredentialStore credentialStore,
        IClock clock,
        HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(credentialStore);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(httpClient);
        _store = store;
        _credentialStore = credentialStore;
        _clock = clock;
        _client = new CalDavClient(httpClient);
        _reader = new IcalReader(clock.LocalTimeZone);
        _writer = new IcalWriter(clock.LocalTimeZone);
    }

    public event EventHandler<SyncStatusChangedEventArgs>? StatusChanged;

    public async Task<Account> ConnectAsync(
        string displayName,
        string serverUrl,
        string username,
        string secret,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(serverUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        ValidateServerUrl(serverUrl);
        await _syncGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string credentialKey = "Wintodo.CalDAV." + Guid.NewGuid().ToString("N");
        Account? account = null;
        try
        {
            RaiseStatus(SyncRunStatus.Syncing, null);
            await _credentialStore.SaveAsync(
                credentialKey,
                username.Trim(),
                secret,
                cancellationToken).ConfigureAwait(false);
            account = await _store.SaveAccountAsync(
                new Account
                {
                    DisplayName = displayName.Trim(),
                    ServerUrl = serverUrl.Trim(),
                    Username = username.Trim(),
                    CredentialKey = credentialKey,
                },
                cancellationToken).ConfigureAwait(false);
            await DiscoverAndRefreshCollectionsAsync(
                account,
                secret,
                cancellationToken).ConfigureAwait(false);
            RaiseStatus(SyncRunStatus.Success, null);
            return account;
        }
        catch
        {
            if (account is not null)
            {
                await _store.DeleteAccountAsync(account.Id, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            await _credentialStore.DeleteAsync(
                credentialKey,
                username.Trim(),
                CancellationToken.None).ConfigureAwait(false);
            RaiseStatus(SyncRunStatus.Error, null);
            throw;
        }
        finally
        {
            _syncGate.Release();
        }
    }

    public async Task DisconnectAsync(
        Account account,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(account);
        await _syncGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _credentialStore.DeleteAsync(
                account.CredentialKey,
                account.Username,
                cancellationToken).ConfigureAwait(false);
            await _store.DeleteAccountAsync(account.Id, cancellationToken).ConfigureAwait(false);
            RaiseStatus(SyncRunStatus.Idle, null);
        }
        finally
        {
            _syncGate.Release();
        }
    }

    public async Task SetListSyncEnabledAsync(
        Account account,
        TodoList list,
        bool isEnabled,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(list);
        await _syncGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string secret = await GetRequiredSecretAsync(account, cancellationToken)
                .ConfigureAwait(false);
            if (isEnabled && string.IsNullOrWhiteSpace(list.RemoteUrl))
            {
                await EnsureDiscoveryAsync(account, secret, cancellationToken).ConfigureAwait(false);
                Uri remoteUrl = await _client.CreateCollectionAsync(
                    account,
                    secret,
                    list.Uid,
                    list.DisplayName,
                    cancellationToken).ConfigureAwait(false);
                await _store.SetListSyncMetadataAsync(
                    list.Id,
                    account.Id,
                    remoteUrl.AbsoluteUri,
                    remoteCTag: null,
                    syncToken: null,
                    markListChangesSynced: true,
                    cancellationToken).ConfigureAwait(false);
                list.AccountId = account.Id;
                list.RemoteUrl = remoteUrl.AbsoluteUri;
            }

            await _store.SetListSyncEnabledAsync(list.Id, isEnabled, cancellationToken)
                .ConfigureAwait(false);
            list.IsSyncEnabled = isEnabled;
        }
        finally
        {
            _syncGate.Release();
        }
    }

    public async Task<SyncRunResult> SyncAllAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _syncGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RaiseStatus(SyncRunStatus.Syncing, null);
            IReadOnlyList<Account> accounts = await _store.GetAccountsAsync(cancellationToken)
                .ConfigureAwait(false);
            var aggregate = new SyncRunResult { Status = SyncRunStatus.Success };
            foreach (Account account in accounts)
            {
                SyncRunResult result = await SyncAccountCoreAsync(account, cancellationToken)
                    .ConfigureAwait(false);
                aggregate.PulledCount += result.PulledCount;
                aggregate.PushedCount += result.PushedCount;
                aggregate.Conflicts.AddRange(result.Conflicts);
                if (result.Status != SyncRunStatus.Success)
                {
                    aggregate.Status = result.Status;
                    aggregate.ErrorMessage ??= result.ErrorMessage;
                }
            }

            if (accounts.Count == 0)
            {
                aggregate.Status = SyncRunStatus.Idle;
            }

            RaiseStatus(aggregate.Status, aggregate.ErrorMessage);
            return aggregate;
        }
        finally
        {
            _syncGate.Release();
        }
    }

    public async Task<SyncRunResult> SyncAccountAsync(
        Account account,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(account);
        await _syncGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RaiseStatus(SyncRunStatus.Syncing, null);
            SyncRunResult result = await SyncAccountCoreAsync(account, cancellationToken)
                .ConfigureAwait(false);
            RaiseStatus(result.Status, result.ErrorMessage);
            return result;
        }
        finally
        {
            _syncGate.Release();
        }
    }

    public async Task ResolveConflictAsync(
        SyncConflict conflict,
        SyncConflictChoice choice,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(conflict);
        await _syncGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            switch (choice)
            {
                case SyncConflictChoice.KeepMine:
                    await _store.SetTaskRemoteVersionAsync(
                        conflict.LocalTask.Uid,
                        conflict.RemoteDeleted ? null : conflict.RemoteHref,
                        conflict.RemoteDeleted ? null : conflict.RemoteETag,
                        cancellationToken,
                        ensureLocalChange: true).ConfigureAwait(false);
                    break;
                case SyncConflictChoice.KeepTheirs:
                    await ApplyConflictRemoteAsync(conflict, cancellationToken).ConfigureAwait(false);
                    break;
                case SyncConflictChoice.KeepBoth:
                    await _store.CreateConflictCopyAsync(conflict.LocalTask, cancellationToken,
                        preserveSteps: conflict.RemoteDeleted)
                        .ConfigureAwait(false);
                    await ApplyConflictRemoteAsync(conflict, cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(choice),
                        choice,
                        "Unknown conflict choice.");
            }
        }
        finally
        {
            _syncGate.Release();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _syncGate.Dispose();
    }

    private async Task<SyncRunResult> SyncAccountCoreAsync(
        Account account,
        CancellationToken cancellationToken)
    {
        try
        {
            string secret = await GetRequiredSecretAsync(account, cancellationToken)
                .ConfigureAwait(false);
            await DiscoverAndRefreshCollectionsAsync(account, secret, cancellationToken)
                .ConfigureAwait(false);
            IReadOnlyList<TodoList> lists = await _store.GetSyncListsAsync(
                account.Id,
                includeDeleted: true,
                cancellationToken).ConfigureAwait(false);
            var result = new SyncRunResult { Status = SyncRunStatus.Success };
            var conflictedUids = new HashSet<string>(StringComparer.Ordinal);
            foreach (TodoList list in lists.Where(value => !value.IsDeleted && value.RemoteUrl is not null))
            {
                await PullListAsync(
                    account,
                    secret,
                    list,
                    result,
                    conflictedUids,
                    cancellationToken).ConfigureAwait(false);
            }

            await PushChangesAsync(
                account,
                secret,
                lists,
                result,
                conflictedUids,
                cancellationToken).ConfigureAwait(false);
            await _store.SetAccountLastSyncAsync(account.Id, _clock.UtcNow, cancellationToken)
                .ConfigureAwait(false);
            account.LastSync = _clock.UtcNow;
            if (result.Conflicts.Count > 0)
            {
                result.Status = SyncRunStatus.AttentionRequired;
            }

            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new SyncRunResult
            {
                Status = SyncRunStatus.Offline,
                ErrorMessage = "The CalDAV server did not respond.",
            };
        }
        catch (HttpRequestException exception)
        {
            return new SyncRunResult
            {
                Status = SyncRunStatus.Offline,
                ErrorMessage = DiagnosticException.Describe(exception),
            };
        }
        catch (CalDavProtocolException exception)
        {
            return new SyncRunResult
            {
                Status = SyncRunStatus.Error,
                ErrorMessage = DiagnosticException.Describe(exception, exception.StatusCode),
            };
        }
    }

    private async Task DiscoverAndRefreshCollectionsAsync(
        Account account,
        string secret,
        CancellationToken cancellationToken)
    {
        await EnsureDiscoveryAsync(account, secret, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<CalDavCollection> collections = await _client.GetCollectionsAsync(
            account,
            secret,
            cancellationToken).ConfigureAwait(false);
        foreach (CalDavCollection collection in collections)
        {
            await _store.UpsertRemoteListAsync(
                account.Id,
                collection.Url.AbsoluteUri,
                collection.DisplayName,
                collection.CTag,
                collection.SyncToken,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task EnsureDiscoveryAsync(
        Account account,
        string secret,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(account.PrincipalUrl)
            && !string.IsNullOrWhiteSpace(account.HomeSetUrl))
        {
            return;
        }

        CalDavDiscovery discovery = await _client.DiscoverAsync(
            account,
            secret,
            cancellationToken).ConfigureAwait(false);
        account.PrincipalUrl = discovery.PrincipalUrl.AbsoluteUri;
        account.HomeSetUrl = discovery.HomeSetUrl.AbsoluteUri;
        await _store.UpdateAccountDiscoveryAsync(
            account.Id,
            account.PrincipalUrl,
            account.HomeSetUrl,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task PullListAsync(
        Account account,
        string secret,
        TodoList list,
        SyncRunResult result,
        HashSet<string> conflictedUids,
        CancellationToken cancellationToken)
    {
        CalDavDelta delta = await _client.GetCollectionDeltaAsync(
            account,
            secret,
            list,
            cancellationToken).ConfigureAwait(false);
        IReadOnlyList<RemoteTaskState> states = await _store.GetRemoteTaskStatesAsync(
            list.Id,
            cancellationToken).ConfigureAwait(false);
        Dictionary<string, RemoteTaskState> statesByHref = states.ToDictionary(
            state => NormalizeHref(state.RemoteHref),
            StringComparer.OrdinalIgnoreCase);
        var presentHrefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var updates = new List<RemoteTaskUpdate>();
        var deletions = new List<string>();

        foreach (CalDavResource resource in delta.Resources)
        {
            string href = NormalizeHref(resource.Href);
            if (!resource.IsDeleted)
            {
                presentHrefs.Add(href);
            }

            statesByHref.TryGetValue(href, out RemoteTaskState? state);
            if (resource.IsDeleted)
            {
                if (state?.IsDirty == true)
                {
                    TodoTask local = await GetRequiredTaskAsync(state.Uid, cancellationToken)
                        .ConfigureAwait(false);
                    AddConflict(
                        result,
                        conflictedUids,
                        account,
                        list,
                        local,
                        remoteTask: null,
                        href,
                        remoteETag: null,
                        remoteDeleted: true);
                }
                else
                {
                    deletions.Add(href);
                }

                continue;
            }

            if (state is not null
                && string.Equals(state.RemoteETag, resource.ETag, StringComparison.Ordinal))
            {
                continue;
            }

            CalDavContent content = await _client.GetTaskAsync(
                account,
                secret,
                resource.Url,
                cancellationToken).ConfigureAwait(false);
            IcalCalendar calendar = _reader.Read(content.Content);
            TodoTask remoteTask = calendar.Tasks.Count == 1
                ? calendar.Tasks[0]
                : throw new InvalidOperationException(
                    $"CalDAV resource '{resource.Url}' must contain exactly one VTODO.");
            TodoTask? localTask = state is null
                ? await _store.GetTaskForSyncAsync(remoteTask.Uid, cancellationToken)
                    .ConfigureAwait(false)
                : await _store.GetTaskForSyncAsync(state.Uid, cancellationToken)
                    .ConfigureAwait(false);
            if (localTask?.IsDirty == true)
            {
                AddConflict(
                    result,
                    conflictedUids,
                    account,
                    list,
                    localTask,
                    remoteTask,
                    href,
                    content.ETag ?? resource.ETag,
                    remoteDeleted: false);
                continue;
            }

            updates.Add(new RemoteTaskUpdate
            {
                Task = remoteTask,
                Href = href,
                ETag = content.ETag ?? resource.ETag,
            });
        }

        if (delta.IsFullSnapshot)
        {
            foreach (RemoteTaskState state in states)
            {
                string href = NormalizeHref(state.RemoteHref);
                if (presentHrefs.Contains(href) || state.IsDeleted)
                {
                    continue;
                }

                if (state.IsDirty)
                {
                    TodoTask local = await GetRequiredTaskAsync(state.Uid, cancellationToken)
                        .ConfigureAwait(false);
                    AddConflict(
                        result,
                        conflictedUids,
                        account,
                        list,
                        local,
                        remoteTask: null,
                        href,
                        remoteETag: null,
                        remoteDeleted: true);
                }
                else
                {
                    deletions.Add(href);
                }
            }
        }

        if (updates.Count > 0 || deletions.Count > 0)
        {
            RemotePullApplyResult applied = await _store.ApplyRemotePullAsync(
                list.Id,
                updates,
                deletions,
                states,
                cancellationToken).ConfigureAwait(false);
            result.PulledCount += applied.AppliedCount;
            foreach (SyncConflict conflict in applied.Conflicts)
            {
                AddConflict(result, conflictedUids, account, list, conflict.LocalTask,
                    conflict.RemoteTask, conflict.RemoteHref, conflict.RemoteETag, conflict.RemoteDeleted);
            }

            if (applied.HasDeferredChanges)
            {
                result.Status = SyncRunStatus.AttentionRequired;
                return;
            }
        }

        if (result.Conflicts.Any(conflict => conflict.ListId == list.Id))
        {
            // Retain the delta position so unresolved pulls survive dismissal/restart.
            return;
        }

        await _store.SetListSyncMetadataAsync(
            list.Id,
            account.Id,
            list.RemoteUrl,
            delta.CTag ?? list.RemoteCTag,
            delta.SyncToken ?? list.SyncToken,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task PushChangesAsync(
        Account account,
        string secret,
        IReadOnlyList<TodoList> lists,
        SyncRunResult result,
        HashSet<string> conflictedUids,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SyncChange> changes = await _store.GetPendingSyncChangesAsync(
            account.Id,
            cancellationToken).ConfigureAwait(false);
        Dictionary<long, TodoList> listsById = lists.ToDictionary(list => list.Id);
        foreach (SyncChange change in changes.Where(value => value.EntityType == SyncEntityType.List
                     && value.Operation == ChangeOperation.Create && value.List is { IsDeleted: false, RemoteUrl: null }))
        {
            TodoList list = change.List!;
            Uri url = await _client.CreateCollectionAsync(account, secret, list.Uid, list.DisplayName,
                cancellationToken).ConfigureAwait(false);
            await _store.MarkListChangeSyncedAsync(change.Id, list.Uid, url.AbsoluteUri,
                cancellationToken).ConfigureAwait(false);
            list.RemoteUrl = url.AbsoluteUri;
            listsById[list.Id] = list;
            result.PushedCount++;
        }
        foreach (SyncChange change in changes
                     .OrderBy(value => value.EntityType == SyncEntityType.Task ? 0 : 1)
                     .ThenBy(value => value.Id))
        {
            if (change.EntityType == SyncEntityType.Task)
            {
                TodoTask task = change.Task
                    ?? throw new InvalidOperationException("A task sync change has no task.");
                if (conflictedUids.Contains(task.Uid)
                    || task.ParentUid is not null && conflictedUids.Contains(task.ParentUid)
                    || !listsById.TryGetValue(task.ListId, out TodoList? list)
                    || string.IsNullOrWhiteSpace(list.RemoteUrl))
                {
                    continue;
                }

                await PushTaskAsync(
                    account,
                    secret,
                    list,
                    change,
                    task,
                    result,
                    conflictedUids,
                    cancellationToken).ConfigureAwait(false);
                continue;
            }

            TodoList changedList = change.List
                ?? throw new InvalidOperationException("A list sync change has no list.");
            if (change.Operation == ChangeOperation.Create) continue;
            if (!listsById.ContainsKey(changedList.Id)
                || string.IsNullOrWhiteSpace(changedList.RemoteUrl))
            {
                continue;
            }

            Uri collectionUrl = new(changedList.RemoteUrl, UriKind.Absolute);
            if (change.Operation == ChangeOperation.Delete || changedList.IsDeleted)
            {
                await _client.DeleteCollectionAsync(
                    account,
                    secret,
                    collectionUrl,
                    cancellationToken).ConfigureAwait(false);
                await _store.MarkListChangeSyncedAsync(
                    change.Id,
                    changedList.Uid,
                    remoteUrl: null,
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await _client.RenameCollectionAsync(
                    account,
                    secret,
                    collectionUrl,
                    changedList.DisplayName,
                    cancellationToken).ConfigureAwait(false);
                await _store.MarkListChangeSyncedAsync(
                    change.Id,
                    changedList.Uid,
                    changedList.RemoteUrl,
                    cancellationToken).ConfigureAwait(false);
            }

            result.PushedCount++;
        }
    }

    private async Task PushTaskAsync(
        Account account,
        string secret,
        TodoList list,
        SyncChange change,
        TodoTask task,
        SyncRunResult result,
        HashSet<string> conflictedUids,
        CancellationToken cancellationToken)
    {
        Uri resourceUrl = string.IsNullOrWhiteSpace(task.RemoteHref)
            ? new Uri(
                EnsureTrailingSlash(new Uri(list.RemoteUrl!, UriKind.Absolute)),
                Uri.EscapeDataString(task.Uid) + ".ics")
            : new Uri(task.RemoteHref, UriKind.Absolute);
        if (change.Operation == ChangeOperation.Delete || task.IsDeleted)
        {
            if (string.IsNullOrWhiteSpace(task.RemoteHref))
            {
                await _store.MarkTaskChangeSyncedAsync(
                    change.Id,
                    task.Uid,
                    remoteHref: null,
                    remoteETag: null,
                    cancellationToken).ConfigureAwait(false);
                result.PushedCount++;
                return;
            }

            CalDavWriteResult delete = await _client.DeleteTaskAsync(
                account,
                secret,
                resourceUrl,
                task.RemoteETag,
                cancellationToken).ConfigureAwait(false);
            if (delete.IsConflict)
            {
                await AddPushConflictAsync(
                    account,
                    secret,
                    list,
                    task,
                    resourceUrl,
                    result,
                    conflictedUids,
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            EnsureWriteSucceeded(delete, "DELETE", resourceUrl);
            await _store.MarkTaskChangeSyncedAsync(
                change.Id,
                task.Uid,
                resourceUrl.AbsoluteUri,
                remoteETag: null,
                cancellationToken).ConfigureAwait(false);
            result.PushedCount++;
            return;
        }

        byte[] payload = _writer.WriteCalendar(list, [task], _clock.UtcNow);
        CalDavWriteResult put = await _client.PutTaskAsync(
            account,
            secret,
            resourceUrl,
            payload,
            task.RemoteETag,
            cancellationToken).ConfigureAwait(false);
        if (put.IsConflict)
        {
            await AddPushConflictAsync(
                account,
                secret,
                list,
                task,
                resourceUrl,
                result,
                conflictedUids,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        EnsureWriteSucceeded(put, "PUT", resourceUrl);
        await _store.MarkTaskChangeSyncedAsync(
            change.Id,
            task.Uid,
            resourceUrl.AbsoluteUri,
            put.ETag,
            cancellationToken).ConfigureAwait(false);
        result.PushedCount++;
    }

    private async Task AddPushConflictAsync(
        Account account,
        string secret,
        TodoList list,
        TodoTask localTask,
        Uri resourceUrl,
        SyncRunResult result,
        HashSet<string> conflictedUids,
        CancellationToken cancellationToken)
    {
        try
        {
            CalDavContent content = await _client.GetTaskAsync(
                account,
                secret,
                resourceUrl,
                cancellationToken).ConfigureAwait(false);
            IcalCalendar calendar = _reader.Read(content.Content);
            TodoTask? remoteTask = calendar.Tasks.Count == 1 ? calendar.Tasks[0] : null;
            AddConflict(
                result,
                conflictedUids,
                account,
                list,
                localTask,
                remoteTask,
                resourceUrl.AbsoluteUri,
                content.ETag,
                remoteDeleted: false);
        }
        catch (CalDavProtocolException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            AddConflict(
                result,
                conflictedUids,
                account,
                list,
                localTask,
                remoteTask: null,
                resourceUrl.AbsoluteUri,
                remoteETag: null,
                remoteDeleted: true);
        }
    }

    private async Task ApplyConflictRemoteAsync(
        SyncConflict conflict,
        CancellationToken cancellationToken)
    {
        if (conflict.RemoteDeleted)
        {
            await _store.ApplyRemoteChangesAsync(
                conflict.ListId,
                [],
                [conflict.RemoteHref],
                cancellationToken,
                deleteLocalSteps: true).ConfigureAwait(false);
            return;
        }

        TodoTask remoteTask = conflict.RemoteTask
            ?? throw new InvalidOperationException("The conflict has no remote task state.");
        await _store.ApplyRemoteChangesAsync(
            conflict.ListId,
            [new RemoteTaskUpdate
            {
                Task = remoteTask,
                Href = conflict.RemoteHref,
                ETag = conflict.RemoteETag,
            }],
            [],
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<TodoTask> GetRequiredTaskAsync(
        string uid,
        CancellationToken cancellationToken)
    {
        return await _store.GetTaskForSyncAsync(uid, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Task '{uid}' was not found.");
    }

    private async Task<string> GetRequiredSecretAsync(
        Account account,
        CancellationToken cancellationToken)
    {
        return await _credentialStore.GetSecretAsync(
            account.CredentialKey,
            account.Username,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Credentials for '{account.DisplayName}' are unavailable.");
    }

    private static void AddConflict(
        SyncRunResult result,
        HashSet<string> conflictedUids,
        Account account,
        TodoList list,
        TodoTask localTask,
        TodoTask? remoteTask,
        string href,
        string? remoteETag,
        bool remoteDeleted)
    {
        if (!conflictedUids.Add(localTask.Uid))
        {
            return;
        }

        result.Conflicts.Add(new SyncConflict
        {
            AccountId = account.Id,
            ListId = list.Id,
            ListName = list.DisplayName,
            LocalTask = localTask,
            RemoteTask = remoteTask,
            RemoteHref = href,
            RemoteETag = remoteETag,
            RemoteDeleted = remoteDeleted,
        });
    }

    private void RaiseStatus(SyncRunStatus status, string? message)
    {
        StatusChanged?.Invoke(this, new SyncStatusChangedEventArgs(status, message));
    }

    private static void EnsureWriteSucceeded(
        CalDavWriteResult result,
        string operation,
        Uri resourceUrl)
    {
        if (!result.IsSuccess)
        {
            throw new CalDavProtocolException(
                $"{operation} '{resourceUrl}' failed with HTTP {(int)result.StatusCode}.",
                result.StatusCode);
        }
    }

    private static Uri EnsureTrailingSlash(Uri value)
    {
        return value.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? value
            : new Uri(value.AbsoluteUri + "/", UriKind.Absolute);
    }

    private static string NormalizeHref(string value)
    {
        return new Uri(value, UriKind.Absolute).AbsoluteUri;
    }

    private static void ValidateServerUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || (uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback))
        {
            throw new ArgumentException(
                "Use an HTTPS CalDAV URL, or HTTP only for a loopback development server.",
                nameof(value));
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }
}
