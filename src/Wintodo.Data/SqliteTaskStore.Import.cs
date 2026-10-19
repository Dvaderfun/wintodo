using Microsoft.Data.Sqlite;
using Wintodo.Core.Ical;
using Wintodo.Core.Import;
using Wintodo.Core.Models;
using Wintodo.Data.Mapping;

namespace Wintodo.Data;

public sealed partial class SqliteTaskStore
{
    public Task<ImportOutcome> PreviewImportAsync(
        ImportSnapshot snapshot,
        ImportOptions options,
        CancellationToken cancellationToken = default)
    {
        ValidateImportSnapshot(snapshot);
        ArgumentNullException.ThrowIfNull(options);
        return ExecuteRollbackAsync(
            (connection, transaction) => RunImport(
                connection,
                transaction,
                new WriteContext(),
                snapshot,
                options,
                runId: null),
            cancellationToken);
    }

    public Task<ImportOutcome> ApplyImportAsync(
        ImportSnapshot snapshot,
        ImportOptions options,
        CancellationToken cancellationToken = default)
    {
        ValidateImportSnapshot(snapshot);
        ArgumentNullException.ThrowIfNull(options);
        string runId = Guid.NewGuid().ToString("N");
        return ExecuteWriteAsync(
            (connection, transaction, context) => RunImport(
                connection,
                transaction,
                context,
                snapshot,
                options,
                runId),
            cancellationToken);
    }

    public Task<int> RemoveImportRunAsync(string runId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        return ExecuteWriteAsync(
            (connection, transaction, context) =>
            {
                var listUids = new List<string>();
                var taskUids = new List<string>();
                using (SqliteCommand select = connection.CreateCommand())
                {
                    select.Transaction = transaction;
                    select.CommandText = """
                        SELECT entity_type, local_uid
                        FROM import_links
                        WHERE created_run_id = @runId AND entity_type IN (0, 1, 2);
                        """;
                    select.Parameters.AddWithValue("@runId", runId);
                    using SqliteDataReader reader = select.ExecuteReader();
                    while (reader.Read())
                    {
                        (reader.GetInt32(0) == (int)ImportEntityType.List ? listUids : taskUids)
                            .Add(reader.GetString(1));
                    }
                }

                string now = SqliteMapper.FormatInstant(_clock.UtcNow);
                int removed = 0;
                foreach (string taskUid in taskUids)
                {
                    using SqliteCommand delete = connection.CreateCommand();
                    delete.Transaction = transaction;
                    delete.CommandText = """
                        UPDATE tasks
                        SET is_deleted = 1, is_dirty = 1, modified_utc = @now, sequence = sequence + 1
                        WHERE uid = @uid AND is_deleted = 0
                        RETURNING list_id;
                        """;
                    delete.Parameters.AddWithValue("@uid", taskUid);
                    delete.Parameters.AddWithValue("@now", now);
                    if (delete.ExecuteScalar() is long listId)
                    {
                        LogChange(connection, transaction, TaskEntityType, taskUid, ChangeOperation.Delete);
                        context.AddTask(listId, taskUid);
                        removed++;
                    }
                }

                foreach (string listUid in listUids)
                {
                    using SqliteCommand delete = connection.CreateCommand();
                    delete.Transaction = transaction;
                    delete.CommandText = """
                        UPDATE lists
                        SET is_deleted = 1, modified_utc = @now
                        WHERE uid = @uid AND is_deleted = 0 AND is_default = 0
                            AND NOT EXISTS(
                                SELECT 1 FROM tasks
                                WHERE tasks.list_id = lists.id AND tasks.is_deleted = 0)
                        RETURNING id;
                        """;
                    delete.Parameters.AddWithValue("@uid", listUid);
                    delete.Parameters.AddWithValue("@now", now);
                    if (delete.ExecuteScalar() is long listId)
                    {
                        LogChange(connection, transaction, ListEntityType, listUid, ChangeOperation.Delete);
                        context.AddList(listId);
                    }
                }

                // Forget the run's links so importing again recreates the items
                // instead of treating them as deleted on purpose.
                using (SqliteCommand forget = connection.CreateCommand())
                {
                    forget.Transaction = transaction;
                    forget.CommandText = """
                        DELETE FROM import_links
                        WHERE created_run_id = @runId
                            AND (entity_type <> 3 OR local_uid IN (
                                SELECT local_uid FROM import_links
                                WHERE created_run_id = @runId AND entity_type IN (0, 2)));
                        """;
                    forget.Parameters.AddWithValue("@runId", runId);
                    forget.ExecuteNonQuery();
                }

                return removed;
            },
            cancellationToken);
    }

    public Task<IReadOnlyList<ImportSourceInfo>> GetImportSourcesAsync(
        string provider,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        return ExecuteReadAsync<IReadOnlyList<ImportSourceInfo>>(
            connection =>
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = """
                    SELECT account_key, display_name, last_import_utc
                    FROM import_sources
                    WHERE provider = @provider AND last_import_utc IS NOT NULL
                    ORDER BY last_import_utc DESC;
                    """;
                command.Parameters.AddWithValue("@provider", provider);
                using SqliteDataReader reader = command.ExecuteReader();
                var sources = new List<ImportSourceInfo>();
                while (reader.Read())
                {
                    sources.Add(new ImportSourceInfo
                    {
                        Provider = provider,
                        AccountKey = reader.GetString(0),
                        DisplayName = reader.GetString(1),
                        LastImport = SqliteMapper.ReadInstant(reader, 2),
                    });
                }

                return sources;
            },
            cancellationToken);
    }

    private Task<T> ExecuteRollbackAsync<T>(
        Func<SqliteConnection, SqliteTransaction, T> action,
        CancellationToken cancellationToken)
    {
        EnsureInitialized();
        return Task.Run(
            () =>
            {
                _writeGate.Wait(cancellationToken);
                try
                {
                    SqliteConnection writer = _writer
                        ?? throw new InvalidOperationException("The store is not initialized.");
                    using SqliteTransaction transaction = writer.BeginTransaction();
                    T result = action(writer, transaction);
                    transaction.Rollback();
                    return result;
                }
                finally
                {
                    _writeGate.Release();
                }
            },
            cancellationToken);
    }

    private static void ValidateImportSnapshot(ImportSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshot.Provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshot.AccountKey);
        foreach (ImportList list in snapshot.Lists)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(list.ExternalId);
            ValidateName(list.DisplayName, nameof(snapshot));
            foreach (ImportTask task in list.Tasks.Concat(list.Tasks.SelectMany(task => task.Steps)))
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(task.ExternalId);
                ValidateTask(task.Task);
            }
        }
    }

    private ImportOutcome RunImport(
        SqliteConnection connection,
        SqliteTransaction transaction,
        WriteContext context,
        ImportSnapshot snapshot,
        ImportOptions options,
        string? runId)
    {
        var run = new ImportRun(snapshot, options, runId, SqliteMapper.FormatInstant(_clock.UtcNow));
        run.SourceId = UpsertImportSource(connection, transaction, snapshot);
        LoadImportLinks(connection, transaction, run);

        foreach (ImportList list in snapshot.Lists)
        {
            if (!run.Seen.Add((ImportEntityType.List, list.ExternalId)))
            {
                continue;
            }

            long? listId = ResolveImportedList(connection, transaction, context, run, list);
            if (listId is null)
            {
                run.Outcome.SkippedLists++;
                run.Outcome.SkippedDeletedTasks += list.Tasks.Count;
                continue;
            }

            double nextSortOrder = GetNextSortOrder(
                connection,
                transaction,
                "tasks",
                "list_id = @listId AND parent_uid IS NULL",
                "@listId",
                listId.Value);
            foreach (ImportTask item in list.Tasks)
            {
                if (!run.Seen.Add((ImportEntityType.Task, item.ExternalId)))
                {
                    continue;
                }

                ImportedTaskRef? parent = ImportTaskItem(
                    connection,
                    transaction,
                    context,
                    run,
                    item,
                    ImportEntityType.Task,
                    listId.Value,
                    parentUid: null,
                    ref nextSortOrder);
                if (parent is null)
                {
                    continue;
                }

                double nextStepOrder = GetNextSortOrder(
                    connection,
                    transaction,
                    "tasks",
                    "parent_uid = @parentUid",
                    "@parentUid",
                    parent.Value.Uid);
                foreach (ImportTask step in item.Steps)
                {
                    if (run.Seen.Add((ImportEntityType.Step, step.ExternalId)))
                    {
                        ImportTaskItem(
                            connection,
                            transaction,
                            context,
                            run,
                            step,
                            ImportEntityType.Step,
                            parent.Value.ListId,
                            parent.Value.Uid,
                            ref nextStepOrder);
                    }
                }
            }
        }

        if (runId is not null)
        {
            using SqliteCommand touch = connection.CreateCommand();
            touch.Transaction = transaction;
            touch.CommandText = "UPDATE import_sources SET last_import_utc = @now WHERE id = @id;";
            touch.Parameters.AddWithValue("@now", run.Now);
            touch.Parameters.AddWithValue("@id", run.SourceId);
            touch.ExecuteNonQuery();
            CopyStagedAttachments(run);
            run.Outcome.RunId = runId;
        }

        return run.Outcome;
    }

    private static long UpsertImportSource(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ImportSnapshot snapshot)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO import_sources(provider, account_key, display_name, last_import_utc)
            VALUES (@provider, @accountKey, @displayName, NULL)
            ON CONFLICT(provider, account_key) DO UPDATE SET display_name = excluded.display_name
            RETURNING id;
            """;
        command.Parameters.AddWithValue("@provider", snapshot.Provider);
        command.Parameters.AddWithValue("@accountKey", snapshot.AccountKey);
        command.Parameters.AddWithValue(
            "@displayName",
            string.IsNullOrWhiteSpace(snapshot.AccountDisplayName) ? snapshot.AccountKey : snapshot.AccountDisplayName);
        return (long)(command.ExecuteScalar()
            ?? throw new InvalidOperationException("Import source upsert did not return an id."));
    }

    private static void LoadImportLinks(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ImportRun run)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT entity_type, external_id, local_uid, fingerprint,
                   external_modified, local_modified, created_run_id
            FROM import_links
            WHERE source_id = @sourceId;
            """;
        command.Parameters.AddWithValue("@sourceId", run.SourceId);
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            var link = new ImportLink
            {
                EntityType = (ImportEntityType)reader.GetInt32(0),
                ExternalId = reader.GetString(1),
                LocalUid = reader.GetString(2),
                Fingerprint = reader.GetString(3),
                ExternalModified = reader.IsDBNull(4) ? null : reader.GetString(4),
                LocalModified = reader.IsDBNull(5) ? null : reader.GetString(5),
                CreatedRunId = reader.IsDBNull(6) ? null : reader.GetString(6),
            };
            run.Links[(link.EntityType, link.ExternalId)] = link;
        }

        foreach (ImportList list in run.Snapshot.Lists)
        {
            run.SnapshotIds.Add((ImportEntityType.List, list.ExternalId));
            foreach (ImportTask task in list.Tasks)
            {
                run.SnapshotIds.Add((ImportEntityType.Task, task.ExternalId));
                foreach (ImportTask step in task.Steps)
                {
                    run.SnapshotIds.Add((ImportEntityType.Step, step.ExternalId));
                }
            }
        }

        // Only links whose external id vanished from the source can be found by
        // fingerprint: that is a task Microsoft re-identified after a move.
        foreach (ImportLink link in run.Links.Values)
        {
            if (link.EntityType is ImportEntityType.Task or ImportEntityType.Step
                && !run.SnapshotIds.Contains((link.EntityType, link.ExternalId)))
            {
                (ImportEntityType, string) key = (link.EntityType, link.Fingerprint);
                if (!run.StaleByFingerprint.TryGetValue(key, out List<ImportLink>? links))
                {
                    links = [];
                    run.StaleByFingerprint.Add(key, links);
                }

                links.Add(link);
            }
        }
    }

    private long? ResolveImportedList(
        SqliteConnection connection,
        SqliteTransaction transaction,
        WriteContext context,
        ImportRun run,
        ImportList list)
    {
        string fingerprint = "list|" + ImportIdentity.NormalizeTitle(list.DisplayName);
        if (run.Links.TryGetValue((ImportEntityType.List, list.ExternalId), out ImportLink? link))
        {
            using SqliteCommand query = connection.CreateCommand();
            query.Transaction = transaction;
            query.CommandText = "SELECT id FROM lists WHERE uid = @uid AND is_deleted = 0;";
            query.Parameters.AddWithValue("@uid", link.LocalUid);
            if (query.ExecuteScalar() is long linkedId)
            {
                run.Outcome.ExistingLists++;
                return linkedId;
            }

            // The person deleted the imported list here; don't bring it back.
            return null;
        }

        long listId;
        bool created = false;
        if (list.Kind == ImportListKind.Default && FindDefaultList(connection, transaction) is long defaultId)
        {
            listId = defaultId;
        }
        else
        {
            (listId, created) = ResolveImportList(
                connection,
                transaction,
                context,
                list.DisplayName.Trim(),
                ListColor.Blue,
                ListEmoji.TryNormalize(list.IconEmoji, out string? emoji) ? emoji : null);
        }

        if (created)
        {
            run.Outcome.NewLists++;
        }
        else
        {
            run.Outcome.ExistingLists++;
        }

        SaveImportLink(
            connection,
            transaction,
            run,
            new ImportLink
            {
                EntityType = ImportEntityType.List,
                ExternalId = list.ExternalId,
                LocalUid = GetListUid(connection, transaction, listId),
                Fingerprint = fingerprint,
                CreatedRunId = created ? run.RunId : null,
            });
        return listId;
    }

    private static long? FindDefaultList(SqliteConnection connection, SqliteTransaction transaction)
    {
        using SqliteCommand query = connection.CreateCommand();
        query.Transaction = transaction;
        query.CommandText = """
            SELECT id FROM lists
            WHERE is_default = 1 AND is_deleted = 0
            ORDER BY account_id IS NOT NULL, id
            LIMIT 1;
            """;
        return query.ExecuteScalar() as long?;
    }

    private ImportedTaskRef? ImportTaskItem(
        SqliteConnection connection,
        SqliteTransaction transaction,
        WriteContext context,
        ImportRun run,
        ImportTask item,
        ImportEntityType entityType,
        long listId,
        string? parentUid,
        ref double nextSortOrder)
    {
        bool isRoot = entityType == ImportEntityType.Task;
        TodoTask source = item.Task;
        string fingerprint = ImportIdentity.Fingerprint(source.Created, source.Summary);
        string sourceModified = SqliteMapper.FormatInstant(source.LastModified);
        ImportLink? link = FindTaskLink(run, entityType, item.ExternalId, fingerprint);

        if (link is not null)
        {
            TodoTask? local = ReadTaskByUid(connection, transaction, link.LocalUid, includeDeleted: true);
            if (local is null || local.IsDeleted)
            {
                if (isRoot)
                {
                    run.Outcome.SkippedDeletedTasks++;
                }

                RelinkIfMoved(connection, transaction, run, link, item.ExternalId, fingerprint);
                return null;
            }

            run.ClaimedLocalUids.Add(local.Uid);
            bool sourceChanged = !string.Equals(link.ExternalId, item.ExternalId, StringComparison.Ordinal)
                || !string.Equals(link.ExternalModified, sourceModified, StringComparison.Ordinal);
            bool localChanged = !string.Equals(
                link.LocalModified,
                SqliteMapper.FormatInstant(local.LastModified),
                StringComparison.Ordinal);

            if (!sourceChanged)
            {
                if (isRoot)
                {
                    run.Outcome.UnchangedTasks++;
                }
            }
            else if (localChanged && !run.Options.OverwriteLocalEdits)
            {
                if (isRoot)
                {
                    run.Outcome.KeptLocalTasks++;
                }

                RelinkIfMoved(connection, transaction, run, link, item.ExternalId, fingerprint);
            }
            else
            {
                long targetList = isRoot ? listId : local.ListId;
                TodoTask updated = BuildImportedTask(source, local.Uid, targetList, parentUid ?? local.ParentUid);
                updated.Id = local.Id;
                updated.MyDayDate = local.MyDayDate;
                updated.RemoteHref = local.RemoteHref;
                updated.RemoteETag = local.RemoteETag;
                updated.Sequence = local.Sequence + 1;
                updated.LastModified = _clock.UtcNow;
                updated.IcalUnknown = IcalExtensionProperties.Overlay(
                    local.IcalUnknown,
                    source.IcalUnknown,
                    run.Snapshot.ManagedPropertyNames);
                updated.SortOrder = local.ListId == targetList ? local.SortOrder : nextSortOrder++;
                UpdateTaskRow(connection, transaction, updated);
                ReplaceImportedCollections(connection, transaction, updated);
                LogChange(connection, transaction, TaskEntityType, updated.Uid, ChangeOperation.Update);
                context.AddTask(local.ListId, updated.Uid);
                context.AddTask(targetList, updated.Uid);
                if (isRoot)
                {
                    run.Outcome.UpdatedTasks++;
                }

                SaveImportLink(
                    connection,
                    transaction,
                    run,
                    new ImportLink
                    {
                        EntityType = entityType,
                        ExternalId = item.ExternalId,
                        LocalUid = updated.Uid,
                        Fingerprint = fingerprint,
                        ExternalModified = sourceModified,
                        LocalModified = SqliteMapper.FormatInstant(updated.LastModified),
                        CreatedRunId = link.CreatedRunId,
                    },
                    replacing: link);
                local = updated;
            }

            ImportAttachments(connection, transaction, run, item, local);
            return new ImportedTaskRef(local.Uid, local.ListId);
        }

        if (isRoot && HasUnlinkedDuplicate(connection, transaction, listId, source))
        {
            run.Outcome.PossibleDuplicates++;
            return null;
        }

        string uid = ImportIdentity.DeterministicUid(
            run.Snapshot.Provider,
            run.Snapshot.AccountKey,
            entityType,
            item.ExternalId);
        TodoTask? existing = ReadTaskByUid(connection, transaction, uid, includeDeleted: true);
        if (existing is { IsDeleted: true } || (existing is not null && run.ClaimedLocalUids.Contains(uid)))
        {
            uid = Guid.NewGuid().ToString("D");
            existing = null;
        }

        TodoTask task = BuildImportedTask(source, uid, existing?.ListId ?? listId, parentUid);
        if (existing is not null)
        {
            // Same deterministic UID but no link: the link table was lost. Adopt the row.
            task.Id = existing.Id;
            task.ListId = isRoot ? listId : existing.ListId;
            task.MyDayDate = existing.MyDayDate;
            task.RemoteHref = existing.RemoteHref;
            task.RemoteETag = existing.RemoteETag;
            task.Sequence = existing.Sequence + 1;
            task.SortOrder = existing.SortOrder;
            task.IcalUnknown = IcalExtensionProperties.Overlay(
                existing.IcalUnknown,
                source.IcalUnknown,
                run.Snapshot.ManagedPropertyNames);
            UpdateTaskRow(connection, transaction, task);
            ReplaceImportedCollections(connection, transaction, task);
            LogChange(connection, transaction, TaskEntityType, uid, ChangeOperation.Update);
            context.AddTask(existing.ListId, uid);
            if (isRoot)
            {
                run.Outcome.UpdatedTasks++;
            }
        }
        else
        {
            task.SortOrder = nextSortOrder++;
            task.Id = InsertTask(connection, transaction, task);
            ReplaceImportedCollections(connection, transaction, task);
            LogChange(connection, transaction, TaskEntityType, uid, ChangeOperation.Create);
            if (isRoot)
            {
                run.Outcome.NewTasks++;
            }
            else
            {
                run.Outcome.NewSteps++;
            }
        }

        context.AddTask(task.ListId, uid);
        run.ClaimedLocalUids.Add(uid);
        SaveImportLink(
            connection,
            transaction,
            run,
            new ImportLink
            {
                EntityType = entityType,
                ExternalId = item.ExternalId,
                LocalUid = uid,
                Fingerprint = fingerprint,
                ExternalModified = sourceModified,
                LocalModified = SqliteMapper.FormatInstant(task.LastModified),
                CreatedRunId = existing is null ? run.RunId : null,
            });
        ImportAttachments(connection, transaction, run, item, task);
        return new ImportedTaskRef(uid, task.ListId);
    }

    private static ImportLink? FindTaskLink(
        ImportRun run,
        ImportEntityType entityType,
        string externalId,
        string fingerprint)
    {
        if (run.Links.TryGetValue((entityType, externalId), out ImportLink? direct))
        {
            return direct;
        }

        if (!run.StaleByFingerprint.TryGetValue((entityType, fingerprint), out List<ImportLink>? candidates))
        {
            return null;
        }

        ImportLink? match = candidates.FirstOrDefault(link => !run.ClaimedLocalUids.Contains(link.LocalUid));
        if (match is not null)
        {
            candidates.Remove(match);
        }

        return match;
    }

    private TodoTask BuildImportedTask(TodoTask source, string uid, long listId, string? parentUid)
    {
        TodoTask task = CloneForImport(source);
        task.Uid = uid;
        task.ListId = listId;
        task.ParentUid = parentUid;
        task.Summary = source.Summary.Trim();
        task.IsDirty = true;
        task.IsDeleted = false;
        task.RemoteHref = null;
        task.RemoteETag = null;
        task.MyDayDate = null;
        DateTimeOffset now = _clock.UtcNow;
        task.Created = source.Created == default ? now : source.Created;
        task.LastModified = source.LastModified == default ? now : source.LastModified;
        ValidateTask(task);
        return task;
    }

    private static bool HasUnlinkedDuplicate(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long listId,
        TodoTask source)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT t.summary, t.due_utc, t.due_is_date_only
            FROM tasks t
            WHERE t.list_id = @listId AND t.is_deleted = 0 AND t.parent_uid IS NULL
                AND trim(t.summary) = @summary COLLATE NOCASE
                AND NOT EXISTS(SELECT 1 FROM import_links l WHERE l.local_uid = t.uid);
            """;
        command.Parameters.AddWithValue("@listId", listId);
        command.Parameters.AddWithValue("@summary", source.Summary.Trim());
        string? sourceDue = SqliteMapper.FormatStoredDate(source.Due, source.DueIsDateOnly);
        string normalized = ImportIdentity.NormalizeTitle(source.Summary);
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            string? due = reader.IsDBNull(1) ? null : reader.GetString(1);
            if (ImportIdentity.NormalizeTitle(reader.GetString(0)) == normalized
                && string.Equals(due, sourceDue, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private void ImportAttachments(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ImportRun run,
        ImportTask item,
        TodoTask task)
    {
        if (string.IsNullOrWhiteSpace(run.Options.AttachmentRoot))
        {
            return;
        }

        foreach (ImportAttachment attachment in item.Attachments)
        {
            if (string.IsNullOrWhiteSpace(attachment.ExternalId)
                || !run.Seen.Add((ImportEntityType.Attachment, attachment.ExternalId))
                || run.Links.ContainsKey((ImportEntityType.Attachment, attachment.ExternalId)))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(attachment.StagedPath))
            {
                // A preview runs before anything is downloaded: count what would be new.
                if (run.RunId is null)
                {
                    run.Outcome.NewAttachments++;
                }

                continue;
            }

            string fileName = UniqueAttachmentName(run, task.Uid, attachment.FileName);
            string relativePath = Path.Combine("attachments", task.Uid, fileName);
            var row = new TaskAttachment
            {
                TaskId = task.Id,
                FileName = fileName,
                RelativePath = relativePath,
                SizeBytes = attachment.SizeBytes,
                MimeType = attachment.MimeType,
                Added = _clock.UtcNow,
            };
            ValidateAttachment(row);
            row.Id = InsertAttachment(connection, transaction, row);
            run.FileCopies.Add((attachment.StagedPath, Path.Combine(run.Options.AttachmentRoot, relativePath)));
            run.Outcome.NewAttachments++;
            SaveImportLink(
                connection,
                transaction,
                run,
                new ImportLink
                {
                    EntityType = ImportEntityType.Attachment,
                    ExternalId = attachment.ExternalId,
                    LocalUid = task.Uid,
                    Fingerprint = fileName,
                    CreatedRunId = run.RunId,
                });
        }
    }

    private static string UniqueAttachmentName(ImportRun run, string taskUid, string fileName)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string sanitized = new string(fileName.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        if (sanitized.Length == 0 || sanitized.Trim('.').Length == 0)
        {
            sanitized = "attachment";
        }

        string folder = Path.Combine(run.Options.AttachmentRoot!, "attachments", taskUid);
        string stem = Path.GetFileNameWithoutExtension(sanitized);
        string extension = Path.GetExtension(sanitized);
        string candidate = sanitized;
        for (int suffix = 2;
             File.Exists(Path.Combine(folder, candidate))
                 || !run.PlannedFiles.Add(Path.Combine(taskUid, candidate).ToUpperInvariant());
             suffix++)
        {
            candidate = $"{stem} ({suffix}){extension}";
        }

        return candidate;
    }

    private static void CopyStagedAttachments(ImportRun run)
    {
        var copied = new List<string>();
        try
        {
            foreach ((string source, string destination) in run.FileCopies)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, overwrite: false);
                copied.Add(destination);
            }
        }
        catch
        {
            // The transaction rolls back; take the files back out with it.
            foreach (string path in copied)
            {
                try
                {
                    File.Delete(path);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            throw;
        }
    }

    private static void RelinkIfMoved(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ImportRun run,
        ImportLink link,
        string externalId,
        string fingerprint)
    {
        if (string.Equals(link.ExternalId, externalId, StringComparison.Ordinal))
        {
            return;
        }

        SaveImportLink(
            connection,
            transaction,
            run,
            new ImportLink
            {
                EntityType = link.EntityType,
                ExternalId = externalId,
                LocalUid = link.LocalUid,
                Fingerprint = fingerprint,
                ExternalModified = link.ExternalModified,
                LocalModified = link.LocalModified,
                CreatedRunId = link.CreatedRunId,
            },
            replacing: link);
    }

    private static void SaveImportLink(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ImportRun run,
        ImportLink link,
        ImportLink? replacing = null)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM import_links
            WHERE source_id = @sourceId AND entity_type = @entityType AND external_id = @oldExternalId;

            INSERT INTO import_links(
                source_id, entity_type, external_id, local_uid, fingerprint,
                external_modified, local_modified, imported_utc, created_run_id)
            VALUES (
                @sourceId, @entityType, @externalId, @localUid, @fingerprint,
                @externalModified, @localModified, @importedUtc, @createdRunId)
            ON CONFLICT(source_id, entity_type, external_id) DO UPDATE SET
                local_uid = excluded.local_uid,
                fingerprint = excluded.fingerprint,
                external_modified = excluded.external_modified,
                local_modified = excluded.local_modified,
                imported_utc = excluded.imported_utc,
                created_run_id = excluded.created_run_id;
            """;
        command.Parameters.AddWithValue("@sourceId", run.SourceId);
        command.Parameters.AddWithValue("@entityType", (int)link.EntityType);
        command.Parameters.AddWithValue("@oldExternalId", (replacing ?? link).ExternalId);
        command.Parameters.AddWithValue("@externalId", link.ExternalId);
        command.Parameters.AddWithValue("@localUid", link.LocalUid);
        command.Parameters.AddWithValue("@fingerprint", link.Fingerprint);
        AddParameter(command, "@externalModified", link.ExternalModified);
        AddParameter(command, "@localModified", link.LocalModified);
        command.Parameters.AddWithValue("@importedUtc", run.Now);
        AddParameter(command, "@createdRunId", link.CreatedRunId);
        command.ExecuteNonQuery();
        if (replacing is not null)
        {
            run.Links.Remove((replacing.EntityType, replacing.ExternalId));
        }

        run.Links[(link.EntityType, link.ExternalId)] = link;
    }

    private readonly record struct ImportedTaskRef(string Uid, long ListId);

    private sealed class ImportLink
    {
        internal ImportEntityType EntityType { get; init; }

        internal string ExternalId { get; init; } = string.Empty;

        internal string LocalUid { get; init; } = string.Empty;

        internal string Fingerprint { get; init; } = string.Empty;

        internal string? ExternalModified { get; init; }

        internal string? LocalModified { get; init; }

        internal string? CreatedRunId { get; init; }
    }

    private sealed class ImportRun(ImportSnapshot snapshot, ImportOptions options, string? runId, string now)
    {
        internal ImportSnapshot Snapshot { get; } = snapshot;

        internal ImportOptions Options { get; } = options;

        internal string? RunId { get; } = runId;

        internal string Now { get; } = now;

        internal long SourceId { get; set; }

        internal ImportOutcome Outcome { get; } = new();

        internal Dictionary<(ImportEntityType, string), ImportLink> Links { get; } = [];

        internal Dictionary<(ImportEntityType, string), List<ImportLink>> StaleByFingerprint { get; } = [];

        internal HashSet<(ImportEntityType, string)> SnapshotIds { get; } = [];

        internal HashSet<(ImportEntityType, string)> Seen { get; } = [];

        internal HashSet<string> ClaimedLocalUids { get; } = new(StringComparer.Ordinal);

        internal HashSet<string> PlannedFiles { get; } = new(StringComparer.Ordinal);

        internal List<(string Source, string Destination)> FileCopies { get; } = [];
    }
}
