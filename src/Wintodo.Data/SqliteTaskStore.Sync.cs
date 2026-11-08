using System.Globalization;
using Microsoft.Data.Sqlite;
using Wintodo.Core.Models;
using Wintodo.Data.Mapping;

namespace Wintodo.Data;

public sealed partial class SqliteTaskStore
{
    public Task<IReadOnlyList<Account>> GetAccountsAsync(
        CancellationToken cancellationToken = default)
    {
        return ExecuteReadAsync<IReadOnlyList<Account>>(
            connection =>
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = """
                    SELECT id, display_name, server_url, username, credential_key,
                           principal_url, home_set_url, last_sync_utc
                    FROM accounts
                    ORDER BY display_name COLLATE NOCASE, id;
                    """;
                using SqliteDataReader reader = command.ExecuteReader();
                var accounts = new List<Account>();
                while (reader.Read())
                {
                    accounts.Add(ReadAccount(reader));
                }

                return accounts;
            },
            cancellationToken);
    }

    public Task<Account> SaveAccountAsync(
        Account account,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        ValidateName(account.DisplayName, nameof(account));
        ArgumentException.ThrowIfNullOrWhiteSpace(account.ServerUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(account.Username);
        ArgumentException.ThrowIfNullOrWhiteSpace(account.CredentialKey);
        return ExecuteWriteAsync(
            (connection, transaction, _) =>
            {
                if (account.Id != 0)
                {
                    using SqliteCommand existing = connection.CreateCommand();
                    existing.Transaction = transaction;
                    existing.CommandText = "SELECT server_url, username, credential_key FROM accounts WHERE id = @id;";
                    existing.Parameters.AddWithValue("@id", account.Id);
                    using SqliteDataReader reader = existing.ExecuteReader();
                    bool identityChanged = reader.Read() && (reader.GetString(0) != account.ServerUrl
                        || reader.GetString(1) != account.Username || reader.GetString(2) != account.CredentialKey);
                    reader.Dispose();
                    if (identityChanged) GuardMoveObligations(connection, transaction, accountId: account.Id);
                }
                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                if (account.Id == 0)
                {
                    command.CommandText = """
                        INSERT INTO accounts(
                            display_name, server_url, username, credential_key,
                            principal_url, home_set_url, last_sync_utc)
                        VALUES(
                            @displayName, @serverUrl, @username, @credentialKey,
                            @principalUrl, @homeSetUrl, @lastSyncUtc)
                        RETURNING id;
                        """;
                }
                else
                {
                    command.CommandText = """
                        UPDATE accounts
                        SET display_name = @displayName, server_url = @serverUrl,
                            username = @username, credential_key = @credentialKey,
                            principal_url = @principalUrl, home_set_url = @homeSetUrl,
                            last_sync_utc = @lastSyncUtc
                        WHERE id = @id
                        RETURNING id;
                        """;
                    command.Parameters.AddWithValue("@id", account.Id);
                }

                command.Parameters.AddWithValue("@displayName", account.DisplayName.Trim());
                command.Parameters.AddWithValue("@serverUrl", account.ServerUrl);
                command.Parameters.AddWithValue("@username", account.Username);
                command.Parameters.AddWithValue("@credentialKey", account.CredentialKey);
                AddParameter(command, "@principalUrl", account.PrincipalUrl);
                AddParameter(command, "@homeSetUrl", account.HomeSetUrl);
                AddParameter(command, "@lastSyncUtc", SqliteMapper.FormatInstant(account.LastSync));
                account.Id = (long)(command.ExecuteScalar()
                    ?? throw new InvalidOperationException("Account save did not return an id."));
                return account;
            },
            cancellationToken);
    }

    public Task DeleteAccountAsync(
        long accountId,
        CancellationToken cancellationToken = default)
    {
        return ExecuteWriteAsync(
            (connection, transaction, context) =>
            {
                GuardMoveObligations(connection, transaction, accountId: accountId);
                using (SqliteCommand lists = connection.CreateCommand())
                {
                    lists.Transaction = transaction;
                    lists.CommandText = "SELECT id FROM lists WHERE account_id = @accountId;";
                    lists.Parameters.AddWithValue("@accountId", accountId);
                    using SqliteDataReader reader = lists.ExecuteReader();
                    while (reader.Read())
                    {
                        context.AddList(reader.GetInt64(0));
                    }
                }

                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    UPDATE lists
                    SET account_id = NULL, remote_url = NULL, remote_ctag = NULL,
                        sync_token = NULL
                    WHERE account_id = @accountId;
                    DELETE FROM accounts WHERE id = @accountId;
                    """;
                command.Parameters.AddWithValue("@accountId", accountId);
                command.ExecuteNonQuery();
            },
            cancellationToken);
    }

    public Task UpdateAccountDiscoveryAsync(
        long accountId,
        string principalUrl,
        string homeSetUrl,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(principalUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(homeSetUrl);
        return ExecuteWriteAsync(
            (connection, transaction, _) =>
            {
                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    UPDATE accounts
                    SET principal_url = @principalUrl, home_set_url = @homeSetUrl
                    WHERE id = @accountId;
                    """;
                command.Parameters.AddWithValue("@principalUrl", principalUrl);
                command.Parameters.AddWithValue("@homeSetUrl", homeSetUrl);
                command.Parameters.AddWithValue("@accountId", accountId);
                if (command.ExecuteNonQuery() != 1)
                {
                    throw new KeyNotFoundException($"Account {accountId} was not found.");
                }
            },
            cancellationToken);
    }

    public Task SetAccountLastSyncAsync(
        long accountId,
        DateTimeOffset value,
        CancellationToken cancellationToken = default)
    {
        return ExecuteWriteAsync(
            (connection, transaction, _) =>
            {
                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "UPDATE accounts SET last_sync_utc = @value WHERE id = @id;";
                command.Parameters.AddWithValue("@value", SqliteMapper.FormatInstant(value));
                command.Parameters.AddWithValue("@id", accountId);
                if (command.ExecuteNonQuery() != 1)
                {
                    throw new KeyNotFoundException($"Account {accountId} was not found.");
                }
            },
            cancellationToken);
    }

    public Task<IReadOnlyList<TodoList>> GetSyncListsAsync(
        long accountId,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        return ExecuteReadAsync<IReadOnlyList<TodoList>>(
            connection =>
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = $"""
                    SELECT {ListColumns}
                    FROM lists
                    WHERE account_id = @accountId AND (remote_url IS NOT NULL OR
                          (is_deleted = 0 AND EXISTS(SELECT 1 FROM change_log
                           WHERE entity_type = 1 AND entity_uid = lists.uid AND synced = 0 AND operation = 0))
                          OR EXISTS(SELECT 1 FROM task_move_outbox
                              WHERE source_list_id = lists.id OR destination_list_id = lists.id))
                          AND NOT EXISTS(
                              SELECT 1 FROM settings
                              WHERE key = 'sync.list.' || lists.uid || '.disabled'
                                    AND value = 'true'
                          )
                          {(includeDeleted ? string.Empty : "AND is_deleted = 0")}
                    ORDER BY sort_order, id;
                    """;
                command.Parameters.AddWithValue("@accountId", accountId);
                using SqliteDataReader reader = command.ExecuteReader();
                var lists = new List<TodoList>();
                while (reader.Read())
                {
                    TodoList list = SqliteMapper.ReadList(reader);
                    list.IsSyncEnabled = true;
                    lists.Add(list);
                }

                return lists;
            },
            cancellationToken);
    }

    public Task<TodoList> UpsertRemoteListAsync(
        long accountId,
        string remoteUrl,
        string displayName,
        string? remoteCTag,
        string? syncToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteUrl);
        ValidateName(displayName, nameof(displayName));
        return ExecuteWriteAsync(
            (connection, transaction, context) =>
            {
                TodoList? list = ReadListByRemoteUrl(connection, transaction, accountId, remoteUrl);
                list ??= ReadUnlinkedListByName(connection, transaction, displayName);
                bool wasMapped = !string.IsNullOrWhiteSpace(list?.RemoteUrl);
                bool preserveLocalState = wasMapped
                    && list is not null
                    && HasUnsyncedEntityChange(
                        connection,
                        transaction,
                        SyncEntityType.List,
                        list.Uid);
                if (list is null)
                {
                    DateTimeOffset now = _clock.UtcNow;
                    list = new TodoList
                    {
                        Uid = Guid.NewGuid().ToString("D"),
                        DisplayName = displayName.Trim(),
                        Color = ListColor.Blue,
                        SortOrder = GetNextSortOrder(connection, transaction, "lists"),
                        ShowCompleted = true,
                        Created = now,
                        Modified = now,
                        AccountId = accountId,
                        RemoteUrl = remoteUrl,
                        RemoteCTag = null,
                        SyncToken = null,
                    };
                    using SqliteCommand insert = connection.CreateCommand();
                    insert.Transaction = transaction;
                    insert.CommandText = """
                        INSERT INTO lists(
                            uid, display_name, color, background_theme, group_id,
                            sort_order, sort_mode, sort_descending, show_completed,
                            completed_expanded, is_default, is_deleted, created_utc,
                            modified_utc, account_id, remote_url, remote_ctag, sync_token)
                        VALUES(
                            @uid, @displayName, @color, NULL, NULL,
                            @sortOrder, 0, 0, 1, 0, 0, 0, @createdUtc,
                            @modifiedUtc, @accountId, @remoteUrl, @remoteCTag, @syncToken)
                        RETURNING id;
                        """;
                    insert.Parameters.AddWithValue("@uid", list.Uid);
                    insert.Parameters.AddWithValue("@displayName", list.DisplayName);
                    insert.Parameters.AddWithValue("@color", (int)list.Color);
                    insert.Parameters.AddWithValue("@sortOrder", list.SortOrder);
                    insert.Parameters.AddWithValue("@createdUtc", SqliteMapper.FormatInstant(now));
                    insert.Parameters.AddWithValue("@modifiedUtc", SqliteMapper.FormatInstant(now));
                    insert.Parameters.AddWithValue("@accountId", accountId);
                    insert.Parameters.AddWithValue("@remoteUrl", remoteUrl);
                    AddParameter(insert, "@remoteCTag", null);
                    AddParameter(insert, "@syncToken", null);
                    list.Id = (long)(insert.ExecuteScalar()
                        ?? throw new InvalidOperationException("Remote list insert did not return an id."));
                }
                else
                {
                    using SqliteCommand update = connection.CreateCommand();
                    update.Transaction = transaction;
                    update.CommandText = """
                        UPDATE lists
                        SET display_name = @displayName, account_id = @accountId,
                            remote_url = @remoteUrl, remote_ctag = @remoteCTag,
                            sync_token = @syncToken, is_deleted = @isDeleted,
                            modified_utc = @modifiedUtc
                        WHERE id = @id;
                        """;
                    string effectiveName = preserveLocalState ? list.DisplayName : displayName.Trim();
                    bool effectiveIsDeleted = preserveLocalState && list.IsDeleted;
                    string? effectiveCTag = wasMapped ? list.RemoteCTag : null;
                    string? effectiveSyncToken = wasMapped ? list.SyncToken : null;
                    update.Parameters.AddWithValue("@displayName", effectiveName);
                    update.Parameters.AddWithValue("@accountId", accountId);
                    update.Parameters.AddWithValue("@remoteUrl", remoteUrl);
                    AddParameter(update, "@remoteCTag", effectiveCTag);
                    AddParameter(update, "@syncToken", effectiveSyncToken);
                    update.Parameters.AddWithValue("@isDeleted", effectiveIsDeleted);
                    update.Parameters.AddWithValue("@modifiedUtc", SqliteMapper.FormatInstant(_clock.UtcNow));
                    update.Parameters.AddWithValue("@id", list.Id);
                    update.ExecuteNonQuery();
                    list.DisplayName = effectiveName;
                    list.AccountId = accountId;
                    list.RemoteUrl = remoteUrl;
                    list.RemoteCTag = effectiveCTag;
                    list.SyncToken = effectiveSyncToken;
                    list.IsSyncEnabled = IsListSyncEnabled(connection, list);
                    list.IsDeleted = effectiveIsDeleted;
                }

                if (!preserveLocalState)
                {
                    using SqliteCommand changes = connection.CreateCommand();
                    changes.Transaction = transaction;
                    changes.CommandText = """
                        UPDATE change_log SET synced = 1
                        WHERE entity_type = 1 AND entity_uid = @uid;
                        """;
                    changes.Parameters.AddWithValue("@uid", list.Uid);
                    changes.ExecuteNonQuery();
                }

                list.IsSyncEnabled = IsListSyncEnabled(connection, list);
                context.AddList(list.Id);
                return list;
            },
            cancellationToken);
    }

    public Task SetListSyncMetadataAsync(
        long listId,
        long? accountId,
        string? remoteUrl,
        string? remoteCTag,
        string? syncToken,
        bool markListChangesSynced = false,
        CancellationToken cancellationToken = default)
    {
        return ExecuteWriteAsync(
            (connection, transaction, context) =>
            {
                string uid = GetListUid(connection, transaction, listId, includeDeleted: true);
                TodoList binding = ReadListById(connection, transaction, listId);
                if (binding.AccountId != accountId || (!string.IsNullOrWhiteSpace(binding.RemoteUrl)
                    && binding.RemoteUrl != remoteUrl))
                    GuardMoveObligations(connection, transaction, listId: listId);
                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    UPDATE lists
                    SET account_id = @accountId, remote_url = @remoteUrl,
                        remote_ctag = @remoteCTag, sync_token = @syncToken
                    WHERE id = @listId;
                    """;
                AddParameter(command, "@accountId", accountId);
                AddParameter(command, "@remoteUrl", remoteUrl);
                AddParameter(command, "@remoteCTag", remoteCTag);
                AddParameter(command, "@syncToken", syncToken);
                command.Parameters.AddWithValue("@listId", listId);
                if (command.ExecuteNonQuery() != 1)
                {
                    throw new KeyNotFoundException($"List {listId} was not found.");
                }

                if (markListChangesSynced)
                {
                    using SqliteCommand changes = connection.CreateCommand();
                    changes.Transaction = transaction;
                    changes.CommandText = """
                        UPDATE change_log SET synced = 1
                        WHERE entity_type = 1 AND entity_uid = @uid;
                        """;
                    changes.Parameters.AddWithValue("@uid", uid);
                    changes.ExecuteNonQuery();
                }

                context.AddList(listId);
            },
            cancellationToken);
    }

    public Task SetListSyncEnabledAsync(
        long listId,
        bool isEnabled,
        CancellationToken cancellationToken = default)
    {
        return ExecuteWriteAsync(
            (connection, transaction, context) =>
            {
                string uid = GetListUid(connection, transaction, listId);
                string key = GetListSyncDisabledKey(uid);
                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                if (isEnabled)
                {
                    command.CommandText = "DELETE FROM settings WHERE key = @key;";
                }
                else
                {
                    command.CommandText = """
                        INSERT INTO settings(key, value) VALUES (@key, 'true')
                        ON CONFLICT(key) DO UPDATE SET value = excluded.value;
                        """;
                }

                command.Parameters.AddWithValue("@key", key);
                command.ExecuteNonQuery();
                context.AddList(listId);
            },
            cancellationToken);
    }

    public Task<IReadOnlyList<SyncChange>> GetPendingSyncChangesAsync(
        long accountId,
        CancellationToken cancellationToken = default)
    {
        return ExecuteReadAsync<IReadOnlyList<SyncChange>>(
            connection =>
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = """
                    SELECT c.id, c.entity_type, c.entity_uid, c.operation, c.changed_utc
                    FROM change_log c
                    JOIN (
                        SELECT entity_type, entity_uid, MAX(id) AS id
                        FROM change_log
                        WHERE synced = 0
                        GROUP BY entity_type, entity_uid
                    ) latest ON latest.id = c.id
                    ORDER BY c.id;
                    """;
                using SqliteDataReader reader = command.ExecuteReader();
                var candidates = new List<SyncChange>();
                while (reader.Read())
                {
                    candidates.Add(new SyncChange
                    {
                        Id = reader.GetInt64(0),
                        EntityType = (SyncEntityType)reader.GetInt32(1),
                        EntityUid = reader.GetString(2),
                        Operation = (ChangeOperation)reader.GetInt32(3),
                        Changed = ParseSyncInstant(reader.GetString(4)),
                    });
                }

                var changes = new List<SyncChange>();
                foreach (SyncChange candidate in candidates)
                {
                    if (candidate.EntityType == SyncEntityType.Task)
                    {
                        if (HasTaskMove(connection, null, candidate.EntityUid)) continue;
                        TodoTask? task = ReadTaskByUid(
                            connection,
                            transaction: null,
                            candidate.EntityUid,
                            includeDeleted: true);
                        if (task is null || !ListBelongsToAccount(connection, task.ListId, accountId))
                        {
                            continue;
                        }

                        LoadTaskCollections(connection, task);
                        candidate.Task = task;
                    }
                    else
                    {
                        TodoList? list = ReadListByUid(connection, transaction: null, candidate.EntityUid);
                        if (list?.AccountId != accountId ||
                            (string.IsNullOrWhiteSpace(list.RemoteUrl) && candidate.Operation != ChangeOperation.Create))
                        {
                            continue;
                        }

                        candidate.List = list;
                    }

                    changes.Add(candidate);
                }

                return changes;
            },
            cancellationToken);
    }

    public Task<IReadOnlyList<RemoteTaskState>> GetRemoteTaskStatesAsync(
        long listId,
        CancellationToken cancellationToken = default)
    {
        return ExecuteReadAsync<IReadOnlyList<RemoteTaskState>>(
            connection =>
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = """
                    SELECT uid, remote_href, remote_etag, is_dirty, is_deleted, sequence
                    FROM tasks
                    WHERE list_id = @listId AND remote_href IS NOT NULL;
                    """;
                command.Parameters.AddWithValue("@listId", listId);
                using SqliteDataReader reader = command.ExecuteReader();
                var states = new List<RemoteTaskState>();
                while (reader.Read())
                {
                    states.Add(new RemoteTaskState
                    {
                        Uid = reader.GetString(0),
                        RemoteHref = reader.GetString(1),
                        RemoteETag = reader.IsDBNull(2) ? null : reader.GetString(2),
                        IsDirty = reader.GetBoolean(3),
                        IsDeleted = reader.GetBoolean(4),
                        Sequence = reader.GetInt32(5),
                    });
                }

                return states;
            },
            cancellationToken);
    }

    public Task<TodoTask?> GetTaskForSyncAsync(
        string uid,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uid);
        return ExecuteReadAsync(
            connection =>
            {
                TodoTask? task = ReadTaskByUid(connection, transaction: null, uid, includeDeleted: true);
                if (task is not null)
                {
                    LoadTaskCollections(connection, task);
                }

                return task;
            },
            cancellationToken);
    }

    public Task ApplyRemoteChangesAsync(
        long listId,
        IReadOnlyList<RemoteTaskUpdate> updates,
        IReadOnlyList<string> deletedHrefs,
        CancellationToken cancellationToken = default,
        bool deleteLocalSteps = false)
    {
        return ApplyRemoteChangesCoreAsync(listId, updates, deletedHrefs, null, cancellationToken, deleteLocalSteps);
    }

    public Task<RemotePullApplyResult> ApplyRemotePullAsync(
        long listId,
        IReadOnlyList<RemoteTaskUpdate> updates,
        IReadOnlyList<string> deletedHrefs,
        IReadOnlyList<RemoteTaskState> expectedStates,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedStates);
        return ApplyRemoteChangesCoreAsync(listId, updates, deletedHrefs, expectedStates, cancellationToken);
    }

    private Task<RemotePullApplyResult> ApplyRemoteChangesCoreAsync(
        long listId,
        IReadOnlyList<RemoteTaskUpdate> updates,
        IReadOnlyList<string> deletedHrefs,
        IReadOnlyList<RemoteTaskState>? expectedStates,
        CancellationToken cancellationToken,
        bool deleteLocalSteps = false)
    {
        ArgumentNullException.ThrowIfNull(updates);
        ArgumentNullException.ThrowIfNull(deletedHrefs);
        return ExecuteWriteAsync(
            (connection, transaction, context) =>
            {
                GetListUid(connection, transaction, listId);
                var result = new RemotePullApplyResult();
                Dictionary<string, RemoteTaskState>? expectedByUid = expectedStates?.ToDictionary(
                    state => state.Uid, StringComparer.Ordinal);
                Dictionary<string, RemoteTaskState>? expectedByHref = expectedStates?.ToDictionary(
                    state => state.RemoteHref, StringComparer.OrdinalIgnoreCase);
                var parents = new List<(long TaskId, string Uid, string ParentUid)>();
                context.IsRemoteChange = true;
                foreach (RemoteTaskUpdate remote in updates)
                {
                    ArgumentNullException.ThrowIfNull(remote);
                    ArgumentNullException.ThrowIfNull(remote.Task);
                    ArgumentException.ThrowIfNullOrWhiteSpace(remote.Href);
                    TodoTask task = remote.Task;
                    ValidateTask(task);
                    ArgumentException.ThrowIfNullOrWhiteSpace(task.Uid);
                    TodoTask? existing = ReadTaskByUid(
                        connection,
                        transaction,
                        task.Uid,
                        includeDeleted: true);
                    if (HasTaskMove(connection, transaction, task.Uid))
                    {
                        result.HasDeferredChanges = true;
                        continue;
                    }
                    if (expectedByUid is not null && existing is not null
                        && ReadRemoteDeleteState(connection, transaction, existing.Uid) == 2)
                    {
                        result.HasDeferredChanges = true;
                        AddPullConflict(connection, transaction, result, existing, task, remote.Href, remote.ETag);
                        continue;
                    }
                    if (expectedByUid is not null)
                    {
                        expectedByUid.TryGetValue(task.Uid, out RemoteTaskState? expected);
                        if (existing is null && expected is not null)
                        {
                            result.HasDeferredChanges = true;
                            continue;
                        }

                        if (existing is not null && IsPullConflict(
                            connection, transaction, listId, existing, expected))
                        {
                            if (expected is not null && existing.ListId != listId
                                && existing.RemoteHref != remote.Href && !existing.IsDirty
                                && !HasUnsyncedEntityChange(connection, transaction, SyncEntityType.Task, existing.Uid))
                            {
                                // An in-flight response for the old binding cannot revive a confirmed move.
                                result.HasDeferredChanges = true;
                                continue;
                            }
                            AddPullConflict(connection, transaction, result, existing,
                                task, remote.Href, remote.ETag);
                            continue;
                        }
                    }

                    string? parentUid = task.ParentUid;
                    task.ParentUid = null;
                    task.ListId = listId;
                    task.RemoteHref = remote.Href;
                    task.RemoteETag = remote.ETag;
                    task.IsDirty = false;
                    task.IsDeleted = false;
                    task.Created = task.Created == default
                        ? existing?.Created ?? _clock.UtcNow
                        : task.Created;
                    task.LastModified = task.LastModified == default
                        ? _clock.UtcNow
                        : task.LastModified;
                    if (!double.IsFinite(task.SortOrder) || task.SortOrder <= 0)
                    {
                        task.SortOrder = existing?.SortOrder > 0
                            ? existing.SortOrder
                            : GetNextSortOrder(
                                connection,
                                transaction,
                                "tasks",
                                "list_id = @listId AND parent_uid IS NULL",
                                "@listId",
                                listId);
                    }

                    if (existing is null)
                    {
                        task.Id = InsertTask(connection, transaction, task);
                    }
                    else
                    {
                        task.Id = existing.Id;
                        UpdateTaskRow(connection, transaction, task);
                        context.AddTask(existing.ListId, task.Uid);
                    }

                    ReplaceTaskCollections(connection, transaction, task);
                    MarkEntityChangesSynced(connection, transaction, SyncEntityType.Task, task.Uid);
                    context.AddTask(listId, task.Uid);
                    result.AppliedCount++;
                    if (parentUid is not null)
                    {
                        parents.Add((task.Id, task.Uid, parentUid));
                    }
                }

                foreach ((long taskId, string uid, string parentUid) in parents)
                {
                    TodoTask? parent = ReadTaskByUid(
                        connection,
                        transaction,
                        parentUid,
                        includeDeleted: false);
                    if (parent?.ListId != listId)
                    {
                        continue;
                    }

                    using SqliteCommand parentUpdate = connection.CreateCommand();
                    parentUpdate.Transaction = transaction;
                    parentUpdate.CommandText =
                        "UPDATE tasks SET parent_uid = @parentUid WHERE id = @id;";
                    parentUpdate.Parameters.AddWithValue("@parentUid", parentUid);
                    parentUpdate.Parameters.AddWithValue("@id", taskId);
                    parentUpdate.ExecuteNonQuery();
                    context.AddTask(listId, uid);
                }

                foreach (string href in deletedHrefs.Distinct(StringComparer.Ordinal))
                {
                    using SqliteCommand find = connection.CreateCommand();
                    find.Transaction = transaction;
                    find.CommandText = """
                        SELECT uid
                        FROM tasks
                        WHERE list_id = @listId AND remote_href = @href;
                        """;
                    find.Parameters.AddWithValue("@listId", listId);
                    find.Parameters.AddWithValue("@href", href);
                    string? uid = find.ExecuteScalar() as string;
                    RemoteTaskState? expected = null;
                    if (expectedByHref is not null && expectedByHref.TryGetValue(href, out expected))
                    {
                        uid = expected.Uid;
                    }

                    if (uid is null)
                    {
                        continue;
                    }

                    if (HasTaskMove(connection, transaction, uid))
                    {
                        result.HasDeferredChanges = true;
                        continue;
                    }
                    if (expectedByHref is not null && ReadRemoteDeleteState(connection, transaction, uid) == 2)
                    {
                        result.HasDeferredChanges = true;
                        continue;
                    }

                    if (expectedByHref is not null)
                    {
                        TodoTask? existing = ReadTaskByUid(connection, transaction, uid, includeDeleted: true);
                        if (existing is null)
                        {
                            result.HasDeferredChanges |= expected is not null;
                            continue;
                        }

                        if (existing.ListId != listId && existing.RemoteHref != href && !existing.IsDirty
                            && !HasUnsyncedEntityChange(connection, transaction, SyncEntityType.Task, existing.Uid))
                            continue;

                        if (IsPullConflict(connection, transaction, listId, existing, expected)
                            || HasPullStepConflict(connection, transaction, existing, expectedByUid!))
                        {
                            AddPullConflict(connection, transaction, result, existing,
                                remote: null, href, etag: null);
                            continue;
                        }
                    }

                    using SqliteCommand delete = connection.CreateCommand();
                    delete.Transaction = transaction;
                    delete.CommandText = """
                        UPDATE tasks
                        SET is_deleted = 1, is_dirty = 0, remote_etag = NULL,
                            remote_delete_state = CASE WHEN @fromPull AND (is_deleted = 0 OR remote_delete_state = 1)
                                THEN 1 ELSE 0 END
                        WHERE list_id = @listId AND remote_href = @href;
                        """;
                    delete.Parameters.AddWithValue("@listId", listId);
                    delete.Parameters.AddWithValue("@href", href);
                    delete.Parameters.AddWithValue("@fromPull", expectedStates is not null);
                    int deletedCount = delete.ExecuteNonQuery();
                    MarkEntityChangesSynced(connection, transaction, SyncEntityType.Task, uid);
                    context.AddTask(listId, uid);
                    result.AppliedCount += deletedCount;
                    if (deleteLocalSteps)
                    {
                        foreach (string stepUid in GetConflictStepUids(connection, transaction, uid))
                        {
                            using SqliteCommand discard = connection.CreateCommand();
                            discard.Transaction = transaction;
                            discard.CommandText = """
                                UPDATE tasks SET is_deleted = 1, is_dirty = 1, remote_delete_state = 0,
                                    modified_utc = @modifiedUtc, sequence = sequence + 1
                                WHERE uid = @uid;
                                """;
                            discard.Parameters.AddWithValue("@modifiedUtc", SqliteMapper.FormatInstant(_clock.UtcNow));
                            discard.Parameters.AddWithValue("@uid", stepUid);
                            discard.ExecuteNonQuery();
                            LogChange(connection, transaction, TaskEntityType, stepUid, ChangeOperation.Delete);
                            context.AddTask(listId, stepUid);
                        }
                    }
                }

                return result;
            },
            cancellationToken);
    }

    private static bool IsPullConflict(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long listId,
        TodoTask existing,
        RemoteTaskState? expected)
    {
        return existing.IsDirty
            || HasTaskMove(connection, transaction, existing.Uid)
            || HasUnsyncedEntityChange(connection, transaction, SyncEntityType.Task, existing.Uid)
            || expected is null
            || existing.ListId != listId
            || existing.Sequence != expected.Sequence
            || existing.IsDeleted != expected.IsDeleted
            || !string.Equals(existing.RemoteHref, expected.RemoteHref, StringComparison.Ordinal)
            || !string.Equals(existing.RemoteETag, expected.RemoteETag, StringComparison.Ordinal);
    }

    private static void AddPullConflict(
        SqliteConnection connection,
        SqliteTransaction transaction,
        RemotePullApplyResult result,
        TodoTask local,
        TodoTask? remote,
        string href,
        string? etag)
    {
        LoadTaskCollections(connection, local, transaction);
        result.Conflicts.Add(new SyncConflict
        {
            LocalTask = local,
            RemoteTask = remote,
            RemoteHref = href,
            RemoteETag = etag,
            RemoteDeleted = remote is null,
        });
    }

    private static bool HasPullStepConflict(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TodoTask parent,
        IReadOnlyDictionary<string, RemoteTaskState> expectedByUid)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT {SqliteMapper.TaskColumns} FROM tasks t WHERE t.parent_uid = @parentUid AND t.is_deleted = 0;";
        command.Parameters.AddWithValue("@parentUid", parent.Uid);
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            TodoTask step = SqliteMapper.ReadTask(reader);
            expectedByUid.TryGetValue(step.Uid, out RemoteTaskState? expected);
            if (IsPullConflict(connection, transaction, parent.ListId, step, expected))
            {
                return true;
            }
        }

        return false;
    }

    public Task MarkTaskChangeSyncedAsync(
        long changeId,
        string uid,
        string? remoteHref,
        string? remoteETag,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uid);
        return ExecuteWriteAsync(
            (connection, transaction, context) =>
            {
                TaskIdentity identity = GetTaskIdentity(connection, transaction, uid, includeDeleted: true);
                if (HasTaskMove(connection, transaction, uid)) return;
                MarkEntityChangesSyncedThrough(
                    connection,
                    transaction,
                    SyncEntityType.Task,
                    uid,
                    changeId);
                bool hasNewer = HasUnsyncedEntityChange(
                    connection,
                    transaction,
                    SyncEntityType.Task,
                    uid);
                using SqliteCommand update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = """
                    UPDATE tasks
                    SET remote_href = @remoteHref, remote_etag = @remoteETag,
                        is_dirty = @isDirty, remote_delete_state = 0
                    WHERE id = @id;
                    """;
                AddParameter(update, "@remoteHref", remoteHref);
                AddParameter(update, "@remoteETag", remoteETag);
                update.Parameters.AddWithValue("@isDirty", hasNewer);
                update.Parameters.AddWithValue("@id", identity.Id);
                update.ExecuteNonQuery();
                context.AddTask(identity.ListId, uid);
            },
            cancellationToken);
    }

    public Task MarkListChangeSyncedAsync(
        long changeId,
        string uid,
        string? remoteUrl,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uid);
        return ExecuteWriteAsync(
            (connection, transaction, context) =>
            {
                TodoList list = ReadListByUid(connection, transaction, uid)
                    ?? throw new KeyNotFoundException($"List '{uid}' was not found.");
                MarkEntityChangesSyncedThrough(
                    connection,
                    transaction,
                    SyncEntityType.List,
                    uid,
                    changeId);
                using SqliteCommand update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = "UPDATE lists SET remote_url = @remoteUrl WHERE id = @id;";
                AddParameter(update, "@remoteUrl", remoteUrl);
                update.Parameters.AddWithValue("@id", list.Id);
                update.ExecuteNonQuery();
                context.AddList(list.Id);
            },
            cancellationToken);
    }

    public Task SetTaskRemoteVersionAsync(
        string uid,
        string? remoteHref,
        string? remoteETag,
        CancellationToken cancellationToken = default,
        bool ensureLocalChange = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uid);
        return ExecuteWriteAsync(
            (connection, transaction, context) =>
            {
                TodoTask task = ReadTaskByUid(connection, transaction, uid, includeDeleted: true)
                    ?? throw new KeyNotFoundException($"Task '{uid}' was not found.");
                if (HasTaskMove(connection, transaction, uid)) return;
                if (ensureLocalChange && QueueReverseMove(connection, transaction, context, task, remoteHref, remoteETag)) return;
                bool queueLocalChoice = ensureLocalChange && !task.IsDirty
                    && !HasUnsyncedEntityChange(connection, transaction, SyncEntityType.Task, uid);
                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    UPDATE tasks
                    SET remote_href = @remoteHref, remote_etag = @remoteETag,
                        is_dirty = CASE WHEN @queueLocalChoice THEN 1 ELSE is_dirty END,
                        remote_delete_state = CASE WHEN @explicitChoice THEN 0 ELSE remote_delete_state END
                    WHERE uid = @uid;
                    """;
                AddParameter(command, "@remoteHref", remoteHref);
                AddParameter(command, "@remoteETag", remoteETag);
                command.Parameters.AddWithValue("@queueLocalChoice", queueLocalChoice);
                command.Parameters.AddWithValue("@explicitChoice", ensureLocalChange);
                command.Parameters.AddWithValue("@uid", uid);
                if (command.ExecuteNonQuery() != 1)
                {
                    throw new KeyNotFoundException($"Task '{uid}' was not found.");
                }

                if (queueLocalChoice)
                {
                    LogChange(connection, transaction, TaskEntityType, uid,
                        task.IsDeleted ? ChangeOperation.Delete : ChangeOperation.Update);
                    context.AddTask(task.ListId, uid);
                }
            },
            cancellationToken);
    }

    public Task<TodoTask> CreateConflictCopyAsync(
        TodoTask source,
        CancellationToken cancellationToken = default,
        bool preserveSteps = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        TodoTask copy = CloneTask(source);
        copy.Id = 0;
        copy.Uid = Guid.NewGuid().ToString("D");
        copy.RemoteHref = null;
        copy.RemoteETag = null;
        copy.IsDeleted = false;
        copy.IsDirty = true;
        copy.Created = default;
        copy.LastModified = default;
        copy.SortOrder = 0;
        if (!preserveSteps)
        {
            return CreateTaskAsync(copy, cancellationToken);
        }

        return ExecuteWriteAsync(
            (connection, transaction, context) =>
            {
                GetListUid(connection, transaction, copy.ListId);
                PrepareNewTask(connection, transaction, copy);
                copy.Id = InsertTask(connection, transaction, copy);
                ReplaceTaskCollections(connection, transaction, copy);
                LogChange(connection, transaction, TaskEntityType, copy.Uid, ChangeOperation.Create);
                context.AddTask(copy.ListId, copy.Uid);
                foreach (string stepUid in GetConflictStepUids(connection, transaction, source.Uid))
                {
                    using SqliteCommand reparent = connection.CreateCommand();
                    reparent.Transaction = transaction;
                    reparent.CommandText = """
                        UPDATE tasks SET parent_uid = @parentUid, is_dirty = 1, remote_delete_state = 0,
                            modified_utc = @modifiedUtc, sequence = sequence + 1
                        WHERE uid = @uid;
                        """;
                    reparent.Parameters.AddWithValue("@parentUid", copy.Uid);
                    reparent.Parameters.AddWithValue("@modifiedUtc", SqliteMapper.FormatInstant(_clock.UtcNow));
                    reparent.Parameters.AddWithValue("@uid", stepUid);
                    reparent.ExecuteNonQuery();
                    LogChange(connection, transaction, TaskEntityType, stepUid, ChangeOperation.Update);
                    context.AddTask(copy.ListId, stepUid);
                }

                context.AddTask(source.ListId, source.Uid);
                return copy;
            },
            cancellationToken);
    }

    private static IReadOnlyList<string> GetConflictStepUids(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string parentUid)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT uid FROM tasks WHERE parent_uid = @parentUid AND is_deleted = 0;";
        command.Parameters.AddWithValue("@parentUid", parentUid);
        using SqliteDataReader reader = command.ExecuteReader();
        var uids = new List<string>();
        while (reader.Read())
        {
            uids.Add(reader.GetString(0));
        }

        return uids;
    }

    private static Account ReadAccount(SqliteDataReader reader)
    {
        return new Account
        {
            Id = reader.GetInt64(0),
            DisplayName = reader.GetString(1),
            ServerUrl = reader.GetString(2),
            Username = reader.GetString(3),
            CredentialKey = reader.GetString(4),
            PrincipalUrl = reader.IsDBNull(5) ? null : reader.GetString(5),
            HomeSetUrl = reader.IsDBNull(6) ? null : reader.GetString(6),
            LastSync = reader.IsDBNull(7) ? null : ParseSyncInstant(reader.GetString(7)),
        };
    }

    private static TodoList? ReadListByUid(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string uid)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT {ListColumns} FROM lists WHERE uid = @uid;";
        command.Parameters.AddWithValue("@uid", uid);
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? SqliteMapper.ReadList(reader) : null;
    }

    private static TodoList? ReadListByRemoteUrl(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long accountId,
        string remoteUrl)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT {ListColumns}
            FROM lists
            WHERE account_id = @accountId AND remote_url = @remoteUrl
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@accountId", accountId);
        command.Parameters.AddWithValue("@remoteUrl", remoteUrl);
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? SqliteMapper.ReadList(reader) : null;
    }

    private static TodoList? ReadUnlinkedListByName(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string displayName)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT {ListColumns}
            FROM lists
            WHERE account_id IS NULL AND remote_url IS NULL AND is_deleted = 0
                  AND display_name = @displayName COLLATE NOCASE
            ORDER BY is_default DESC, id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@displayName", displayName.Trim());
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? SqliteMapper.ReadList(reader) : null;
    }

    private static bool ListBelongsToAccount(
        SqliteConnection connection,
        long listId,
        long accountId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS(
                SELECT 1 FROM lists
                WHERE id = @listId AND account_id = @accountId
                      AND (remote_url IS NOT NULL OR EXISTS(SELECT 1 FROM change_log
                          WHERE entity_type = 1 AND entity_uid = lists.uid AND synced = 0 AND operation = 0))
            );
            """;
        command.Parameters.AddWithValue("@listId", listId);
        command.Parameters.AddWithValue("@accountId", accountId);
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 1;
    }

    private static bool IsListSyncEnabled(SqliteConnection connection, TodoList list)
    {
        if (list.AccountId is null || string.IsNullOrWhiteSpace(list.RemoteUrl))
        {
            return false;
        }

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT NOT EXISTS(SELECT 1 FROM settings WHERE key = @key AND value = 'true');";
        command.Parameters.AddWithValue("@key", GetListSyncDisabledKey(list.Uid));
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 1;
    }

    private static string GetListSyncDisabledKey(string uid)
    {
        return "sync.list." + uid + ".disabled";
    }

    private static void MarkEntityChangesSynced(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SyncEntityType entityType,
        string uid)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE change_log SET synced = 1
            WHERE entity_type = @entityType AND entity_uid = @uid;
            """;
        command.Parameters.AddWithValue("@entityType", (int)entityType);
        command.Parameters.AddWithValue("@uid", uid);
        command.ExecuteNonQuery();
    }

    private static void MarkEntityChangesSyncedThrough(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SyncEntityType entityType,
        string uid,
        long changeId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE change_log SET synced = 1
            WHERE entity_type = @entityType AND entity_uid = @uid AND id <= @changeId;
            """;
        command.Parameters.AddWithValue("@entityType", (int)entityType);
        command.Parameters.AddWithValue("@uid", uid);
        command.Parameters.AddWithValue("@changeId", changeId);
        command.ExecuteNonQuery();
    }

    private static bool HasUnsyncedEntityChange(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SyncEntityType entityType,
        string uid)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT EXISTS(
                SELECT 1 FROM change_log
                WHERE entity_type = @entityType AND entity_uid = @uid AND synced = 0
            );
            """;
        command.Parameters.AddWithValue("@entityType", (int)entityType);
        command.Parameters.AddWithValue("@uid", uid);
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 1;
    }

    private static DateTimeOffset ParseSyncInstant(string value)
    {
        return DateTimeOffset.ParseExact(
            value,
            "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }

    private static TodoTask CloneTask(TodoTask source)
    {
        var clone = new TodoTask
        {
            ListId = source.ListId,
            ParentUid = source.ParentUid,
            Summary = source.Summary,
            Description = source.Description,
            Status = source.Status,
            PercentComplete = source.PercentComplete,
            Priority = source.Priority,
            Due = source.Due,
            DueIsDateOnly = source.DueIsDateOnly,
            DueTzid = source.DueTzid,
            Start = source.Start,
            StartIsDateOnly = source.StartIsDateOnly,
            CompletedAt = source.CompletedAt,
            Sequence = source.Sequence,
            RRule = source.RRule,
            MyDayDate = source.MyDayDate,
            Location = source.Location,
            Url = source.Url,
            Class = source.Class,
            IcalUnknown = source.IcalUnknown,
        };
        clone.Categories.AddRange(source.Categories);
        clone.Alarms.AddRange(source.Alarms.Select(alarm => new TaskAlarm
        {
            Uid = alarm.Uid,
            Action = alarm.Action,
            Trigger = alarm.Trigger,
            TriggerOffsetSeconds = alarm.TriggerOffsetSeconds,
            TriggerRelated = alarm.TriggerRelated,
            Description = alarm.Description,
            SnoozedUntil = alarm.SnoozedUntil,
            IsAcknowledged = alarm.IsAcknowledged,
        }));
        clone.Attachments.AddRange(source.Attachments.Select(attachment => new TaskAttachment
        {
            FileName = attachment.FileName,
            RelativePath = attachment.RelativePath,
            SizeBytes = attachment.SizeBytes,
            MimeType = attachment.MimeType,
            Added = attachment.Added,
        }));
        return clone;
    }
}
