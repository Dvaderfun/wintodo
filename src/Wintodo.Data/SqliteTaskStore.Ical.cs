using Microsoft.Data.Sqlite;
using Wintodo.Core.Ical;
using Wintodo.Core.Models;
using Wintodo.Data.Mapping;

namespace Wintodo.Data;

public sealed partial class SqliteTaskStore
{
    public Task<IReadOnlyList<TodoTask>> GetTasksForExportAsync(
        long listId,
        CancellationToken cancellationToken = default)
    {
        return ExecuteReadAsync<IReadOnlyList<TodoTask>>(
            connection =>
            {
                using (SqliteCommand list = connection.CreateCommand())
                {
                    list.CommandText = "SELECT EXISTS(SELECT 1 FROM lists WHERE id = @id AND is_deleted = 0);";
                    list.Parameters.AddWithValue("@id", listId);
                    if (Convert.ToInt32(
                            list.ExecuteScalar(),
                            System.Globalization.CultureInfo.InvariantCulture) == 0)
                    {
                        throw new KeyNotFoundException($"List {listId} was not found.");
                    }
                }

                var tasks = new List<TodoTask>();
                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText = $"""
                        SELECT {SqliteMapper.TaskColumns}
                        FROM tasks t
                        WHERE t.list_id = @listId AND t.is_deleted = 0
                        ORDER BY t.sort_order, t.id;
                        """;
                    command.Parameters.AddWithValue("@listId", listId);
                    using SqliteDataReader reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        tasks.Add(SqliteMapper.ReadTask(reader));
                    }
                }

                foreach (TodoTask task in tasks)
                {
                    LoadTaskCollections(connection, task);
                }

                return tasks;
            },
            cancellationToken);
    }

    public Task<IcalImportPreview> PreviewIcalImportAsync(
        IReadOnlyList<IcalCalendar> calendars,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<IcalCalendar> validated = ValidateIcalBatch(calendars);
        return ExecuteReadAsync(
            connection =>
            {
                var importedUids = validated
                    .SelectMany(calendar => calendar.Tasks)
                    .Select(task => task.Uid)
                    .ToHashSet(StringComparer.Ordinal);
                int conflicts = 0;
                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT uid FROM tasks;";
                    using SqliteDataReader reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        if (importedUids.Contains(reader.GetString(0)))
                        {
                            conflicts++;
                        }
                    }
                }

                return new IcalImportPreview
                {
                    TaskCount = validated.Sum(calendar => calendar.Tasks.Count),
                    ListCount = validated
                        .Select(calendar => calendar.Name!.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count(),
                    ConflictCount = conflicts,
                };
            },
            cancellationToken);
    }

    public Task<IcalImportResult> ImportIcalAsync(
        IReadOnlyList<IcalCalendar> calendars,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<IcalCalendar> validated = ValidateIcalBatch(calendars);
        return ExecuteWriteAsync(
            (connection, transaction, context) =>
            {
                var importedLists = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                var pendingParents = new List<(TodoTask Task, string ParentUid)>();
                var nextFallbackSortOrders = new Dictionary<long, double>();
                int createdLists = 0;
                int createdTasks = 0;
                int updatedTasks = 0;

                foreach (IcalCalendar calendar in validated)
                {
                    string listName = calendar.Name!.Trim();
                    if (!importedLists.TryGetValue(listName, out long listId))
                    {
                        (listId, bool created) = ResolveImportList(
                            connection,
                            transaction,
                            context,
                            listName,
                            calendar.Color ?? ListColor.Blue);
                        importedLists.Add(listName, listId);
                        if (created)
                        {
                            createdLists++;
                        }
                    }

                    foreach (TodoTask source in calendar.Tasks)
                    {
                        TodoTask task = CloneForImport(source);
                        string? parentUid = task.ParentUid;
                        task.ParentUid = null;
                        task.ListId = listId;
                        task.IsDeleted = false;
                        task.IsDirty = true;
                        task.Created = task.Created == default ? _clock.UtcNow : task.Created;
                        task.LastModified = task.LastModified == default ? _clock.UtcNow : task.LastModified;
                        if (!double.IsFinite(task.SortOrder) || task.SortOrder <= 0)
                        {
                            if (!nextFallbackSortOrders.TryGetValue(listId, out double nextSortOrder))
                            {
                                nextSortOrder = GetNextSortOrder(
                                    connection,
                                    transaction,
                                    "tasks",
                                    "list_id = @listId",
                                    "@listId",
                                    listId);
                            }

                            task.SortOrder = nextSortOrder;
                            nextFallbackSortOrders[listId] = nextSortOrder + 1;
                        }

                        TodoTask? existing = ReadTaskByUid(
                            connection,
                            transaction,
                            task.Uid,
                            includeDeleted: true);
                        if (existing is null)
                        {
                            task.Id = InsertTask(connection, transaction, task);
                            ReplaceImportedCollections(connection, transaction, task);
                            LogChange(
                                connection,
                                transaction,
                                TaskEntityType,
                                task.Uid,
                                ChangeOperation.Create);
                            createdTasks++;
                        }
                        else
                        {
                            task.Id = existing.Id;
                            task.Created = source.Created == default ? existing.Created : source.Created;
                            task.RemoteHref ??= existing.RemoteHref;
                            task.RemoteETag ??= existing.RemoteETag;
                            UpdateTaskRow(connection, transaction, task);
                            ReplaceImportedCollections(connection, transaction, task);
                            LogChange(
                                connection,
                                transaction,
                                TaskEntityType,
                                task.Uid,
                                ChangeOperation.Update);
                            context.AddTask(existing.ListId, task.Uid);
                            updatedTasks++;
                        }

                        context.AddTask(listId, task.Uid);
                        if (parentUid is not null)
                        {
                            pendingParents.Add((task, parentUid));
                        }
                    }
                }

                foreach ((TodoTask task, string parentUid) in pendingParents)
                {
                    task.ParentUid = parentUid;
                    ValidateParent(connection, transaction, task);
                    using SqliteCommand command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = "UPDATE tasks SET parent_uid = @parentUid WHERE id = @id;";
                    command.Parameters.AddWithValue("@parentUid", parentUid);
                    command.Parameters.AddWithValue("@id", task.Id);
                    command.ExecuteNonQuery();
                }

                ValidateImportedHierarchy(connection, transaction);

                return new IcalImportResult
                {
                    CreatedListCount = createdLists,
                    CreatedTaskCount = createdTasks,
                    UpdatedTaskCount = updatedTasks,
                    ListIds = importedLists.Values.Distinct().ToArray(),
                };
            },
            cancellationToken);
    }

    private (long ListId, bool Created) ResolveImportList(
        SqliteConnection connection,
        SqliteTransaction transaction,
        WriteContext context,
        string displayName,
        ListColor color)
    {
        using (SqliteCommand query = connection.CreateCommand())
        {
            query.Transaction = transaction;
            query.CommandText = """
                SELECT id
                FROM lists
                WHERE display_name = @displayName COLLATE NOCASE AND is_deleted = 0
                ORDER BY id
                LIMIT 1;
                """;
            query.Parameters.AddWithValue("@displayName", displayName);
            object? existing = query.ExecuteScalar();
            if (existing is long existingId)
            {
                return (existingId, false);
            }
        }

        string uid = Guid.NewGuid().ToString("D");
        DateTimeOffset now = _clock.UtcNow;
        double sortOrder = GetNextSortOrder(connection, transaction, "lists");
        using SqliteCommand insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO lists(
                uid, display_name, color, background_theme, group_id,
                sort_order, sort_mode, sort_descending, show_completed,
                completed_expanded, is_deleted, created_utc, modified_utc)
            VALUES(
                @uid, @displayName, @color, NULL, NULL,
                @sortOrder, 0, 0, 1, 0, 0, @createdUtc, @modifiedUtc)
            RETURNING id;
            """;
        insert.Parameters.AddWithValue("@uid", uid);
        insert.Parameters.AddWithValue("@displayName", displayName);
        insert.Parameters.AddWithValue("@color", (int)color);
        insert.Parameters.AddWithValue("@sortOrder", sortOrder);
        insert.Parameters.AddWithValue("@createdUtc", SqliteMapper.FormatInstant(now));
        insert.Parameters.AddWithValue("@modifiedUtc", SqliteMapper.FormatInstant(now));
        long id = (long)(insert.ExecuteScalar()
            ?? throw new InvalidOperationException("Imported list insert did not return an id."));
        LogChange(connection, transaction, ListEntityType, uid, ChangeOperation.Create);
        context.AddList(id);
        return (id, true);
    }

    private static IReadOnlyList<IcalCalendar> ValidateIcalBatch(
        IReadOnlyList<IcalCalendar> calendars)
    {
        ArgumentNullException.ThrowIfNull(calendars);
        if (calendars.Count == 0)
        {
            throw new ArgumentException("At least one iCalendar is required.", nameof(calendars));
        }

        var uids = new HashSet<string>(StringComparer.Ordinal);
        foreach (IcalCalendar calendar in calendars)
        {
            ArgumentNullException.ThrowIfNull(calendar);
            ValidateName(calendar.Name ?? string.Empty, nameof(calendars));
            foreach (TodoTask task in calendar.Tasks)
            {
                ValidateTask(task);
                ArgumentException.ThrowIfNullOrWhiteSpace(task.Uid);
                if (!uids.Add(task.Uid))
                {
                    throw new ArgumentException(
                        $"Task UID '{task.Uid}' occurs more than once in the import.",
                        nameof(calendars));
                }

                if (string.Equals(task.Uid, task.ParentUid, StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        $"Task '{task.Uid}' cannot be its own parent.",
                        nameof(calendars));
                }
            }
        }

        return calendars;
    }

    private void ReplaceImportedCollections(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TodoTask task)
    {
        using (SqliteCommand delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = """
                DELETE FROM task_tags WHERE task_id = @taskId;
                DELETE FROM alarms WHERE task_id = @taskId;
                """;
            delete.Parameters.AddWithValue("@taskId", task.Id);
            delete.ExecuteNonQuery();
        }

        foreach (string category in task.Categories
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                     .Select(value => value.Trim())
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            using (SqliteCommand insertTag = connection.CreateCommand())
            {
                insertTag.Transaction = transaction;
                insertTag.CommandText =
                    "INSERT INTO tags(name) VALUES (@name) ON CONFLICT(name) DO NOTHING;";
                insertTag.Parameters.AddWithValue("@name", category);
                insertTag.ExecuteNonQuery();
            }

            using SqliteCommand insertLink = connection.CreateCommand();
            insertLink.Transaction = transaction;
            insertLink.CommandText = """
                INSERT INTO task_tags(task_id, tag_id)
                SELECT @taskId, id FROM tags WHERE name = @name COLLATE NOCASE;
                """;
            insertLink.Parameters.AddWithValue("@taskId", task.Id);
            insertLink.Parameters.AddWithValue("@name", category);
            insertLink.ExecuteNonQuery();
        }

        foreach (TaskAlarm alarm in task.Alarms)
        {
            alarm.TaskId = task.Id;
            ValidateAlarm(alarm);
            alarm.Id = InsertAlarm(connection, transaction, alarm);
        }
    }

    private static TodoTask CloneForImport(TodoTask source)
    {
        var clone = new TodoTask
        {
            Uid = source.Uid,
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
            Created = source.Created,
            LastModified = source.LastModified,
            Sequence = source.Sequence,
            RRule = source.RRule,
            MyDayDate = source.MyDayDate,
            SortOrder = source.SortOrder,
            Location = source.Location,
            Url = source.Url,
            Class = source.Class,
            IcalUnknown = source.IcalUnknown,
            RemoteHref = source.RemoteHref,
            RemoteETag = source.RemoteETag,
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
            ScheduledToastTag = alarm.ScheduledToastTag,
        }));
        return clone;
    }

    private static void ValidateImportedHierarchy(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        var tasks = new Dictionary<string, (long ListId, string? ParentUid)>(
            StringComparer.Ordinal);
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT uid, list_id, parent_uid FROM tasks WHERE is_deleted = 0;";
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                tasks.Add(
                    reader.GetString(0),
                    (reader.GetInt64(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
            }
        }

        var complete = new HashSet<string>(StringComparer.Ordinal);
        foreach (string startUid in tasks.Keys)
        {
            if (complete.Contains(startUid))
            {
                continue;
            }

            var chain = new HashSet<string>(StringComparer.Ordinal);
            string currentUid = startUid;
            while (!complete.Contains(currentUid))
            {
                if (!chain.Add(currentUid))
                {
                    throw new InvalidOperationException("The imported task hierarchy contains a cycle.");
                }

                (long listId, string? parentUid) = tasks[currentUid];
                if (parentUid is null)
                {
                    break;
                }

                if (!tasks.TryGetValue(parentUid, out (long ListId, string? ParentUid) parent))
                {
                    throw new KeyNotFoundException($"Task '{parentUid}' was not found.");
                }

                if (parent.ListId != listId)
                {
                    throw new InvalidOperationException(
                        "A step must belong to the same list as its parent.");
                }

                currentUid = parentUid;
            }

            complete.UnionWith(chain);
        }
    }
}
