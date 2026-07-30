using System.Text;
using Microsoft.Data.Sqlite;
using Wintodo.Core.Models;
using Wintodo.Core.Queries;
using Wintodo.Core.Recurrence;
using Wintodo.Data.Mapping;

namespace Wintodo.Data;

public sealed partial class SqliteTaskStore
{
    private const string AlarmColumns = """
        id, task_id, uid, action, trigger_utc, trigger_offset_seconds,
        trigger_related, description, snoozed_until_utc, is_acknowledged,
        scheduled_toast_tag
        """;

    private const string AttachmentColumns = """
        id, task_id, file_name, relative_path, size_bytes, mime_type, added_utc
        """;

    public Task<IReadOnlyList<TodoTask>> GetTasksAsync(
        ListQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Kind == ListQueryKind.UserList && query.ListId is null)
        {
            throw new ArgumentException("A user-list query requires a list id.", nameof(query));
        }

        if (query.Limit is < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "Query limit must be positive.");
        }

        return ExecuteReadAsync<IReadOnlyList<TodoTask>>(
            connection =>
            {
                using SqliteCommand command = connection.CreateCommand();
                var sql = new StringBuilder($"SELECT {SqliteMapper.TaskColumns} FROM tasks t WHERE t.is_deleted = 0 AND t.parent_uid IS NULL");

                AppendListPredicate(sql, command, query);
                if (!query.IncludeCompleted && query.Kind != ListQueryKind.Completed)
                {
                    sql.Append(" AND t.status <> 2");
                }

                if (!string.IsNullOrWhiteSpace(query.Filter))
                {
                    sql.Append(" AND (t.summary LIKE @filter ESCAPE '\\' OR COALESCE(t.description, '') LIKE @filter ESCAPE '\\')");
                    command.Parameters.AddWithValue("@filter", $"%{EscapeLike(query.Filter)}%");
                }

                sql.Append(GetOrderBy(query.SortMode, query.SortDescending, query.Kind));
                if (query.Limit is not null)
                {
                    sql.Append(" LIMIT @limit");
                    command.Parameters.AddWithValue("@limit", query.Limit.Value);
                }

                sql.Append(';');
                command.CommandText = sql.ToString();
                using SqliteDataReader reader = command.ExecuteReader();
                var tasks = new List<TodoTask>();
                while (reader.Read())
                {
                    tasks.Add(SqliteMapper.ReadTask(reader));
                }

                return tasks;
            },
            cancellationToken);
    }

    public Task<TodoTask?> GetTaskAsync(string uid, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uid);
        return ExecuteReadAsync(
            connection =>
            {
                TodoTask? task = ReadTaskByUid(connection, transaction: null, uid, includeDeleted: false);
                if (task is not null)
                {
                    LoadTaskCollections(connection, task);
                }

                return task;
            },
            cancellationToken);
    }

    public Task<TodoTask> CreateTaskAsync(
        TodoTask task,
        CancellationToken cancellationToken = default)
    {
        ValidateTask(task);
        return ExecuteWriteAsync(
            (connection, transaction, context) =>
            {
                GetListUid(connection, transaction, task.ListId);
                PrepareNewTask(connection, transaction, task);
                task.Id = InsertTask(connection, transaction, task);
                ReplaceTaskCollections(connection, transaction, task);
                LogChange(connection, transaction, TaskEntityType, task.Uid, ChangeOperation.Create);
                context.AddTask(task.ListId, task.Uid);
                return task;
            },
            cancellationToken);
    }

    public Task UpdateTaskAsync(TodoTask task, CancellationToken cancellationToken = default)
    {
        ValidateTask(task);
        ArgumentException.ThrowIfNullOrWhiteSpace(task.Uid);
        return ExecuteWriteAsync(
            (connection, transaction, context) =>
            {
                TodoTask existing = ReadTaskByUid(connection, transaction, task.Uid, includeDeleted: false)
                    ?? throw new KeyNotFoundException($"Task '{task.Uid}' was not found.");
                GetListUid(connection, transaction, task.ListId);
                ValidateParent(connection, transaction, task);

                task.Id = existing.Id;
                task.Created = existing.Created;
                task.LastModified = _clock.UtcNow;
                task.Sequence = Math.Max(existing.Sequence, task.Sequence) + 1;
                task.IsDirty = true;
                UpdateTaskRow(connection, transaction, task);
                ReplaceTaskCollections(connection, transaction, task);
                LogChange(connection, transaction, TaskEntityType, task.Uid, ChangeOperation.Update);
                context.AddTask(existing.ListId, task.Uid);
                context.AddTask(task.ListId, task.Uid);
            },
            cancellationToken);
    }

    public Task CompleteTaskAsync(string uid, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uid);
        return ExecuteWriteAsync(
            (connection, transaction, context) =>
            {
                TodoTask task = ReadTaskByUid(connection, transaction, uid, includeDeleted: false)
                    ?? throw new KeyNotFoundException($"Task '{uid}' was not found.");
                DateTimeOffset now = _clock.UtcNow;

                if (string.IsNullOrWhiteSpace(task.RRule))
                {
                    task.Status = Wintodo.Core.Models.TaskStatus.Completed;
                    task.PercentComplete = 100;
                    task.CompletedAt = now;
                }
                else
                {
                    AdvanceRecurringTask(connection, transaction, task, now);
                }

                task.LastModified = now;
                task.Sequence++;
                task.IsDirty = true;
                UpdateTaskRow(connection, transaction, task);
                LogChange(connection, transaction, TaskEntityType, task.Uid, ChangeOperation.Update);
                context.AddTask(task.ListId, task.Uid);
            },
            cancellationToken);
    }

    public Task UncompleteTaskAsync(string uid, CancellationToken cancellationToken = default)
    {
        return SetTaskCompletionAsync(
            uid,
            Wintodo.Core.Models.TaskStatus.NeedsAction,
            percentComplete: 0,
            completedAt: null,
            cancellationToken);
    }

    public Task DeleteTaskAsync(string uid, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uid);
        return ExecuteWriteAsync(
            (connection, transaction, context) =>
            {
                TaskIdentity identity = GetTaskIdentity(connection, transaction, uid);
                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    UPDATE tasks
                    SET is_deleted = 1, is_dirty = 1, modified_utc = @modifiedUtc,
                        sequence = sequence + 1
                    WHERE id = @id;
                    """;
                command.Parameters.AddWithValue("@modifiedUtc", SqliteMapper.FormatInstant(_clock.UtcNow));
                command.Parameters.AddWithValue("@id", identity.Id);
                command.ExecuteNonQuery();
                LogChange(connection, transaction, TaskEntityType, uid, ChangeOperation.Delete);
                context.AddTask(identity.ListId, uid);
            },
            cancellationToken);
    }

    public Task RestoreTaskAsync(string uid, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uid);
        return ExecuteWriteAsync(
            (connection, transaction, context) =>
            {
                TaskIdentity identity = GetTaskIdentity(connection, transaction, uid, includeDeleted: true);
                GetListUid(connection, transaction, identity.ListId);
                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    UPDATE tasks
                    SET is_deleted = 0, is_dirty = 1, modified_utc = @modifiedUtc,
                        sequence = sequence + 1
                    WHERE id = @id;
                    """;
                command.Parameters.AddWithValue("@modifiedUtc", SqliteMapper.FormatInstant(_clock.UtcNow));
                command.Parameters.AddWithValue("@id", identity.Id);
                command.ExecuteNonQuery();
                LogChange(connection, transaction, TaskEntityType, uid, ChangeOperation.Update);
                context.AddTask(identity.ListId, uid);
            },
            cancellationToken);
    }

    public Task ReorderTaskAsync(
        string uid,
        double sortOrder,
        CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(sortOrder))
        {
            throw new ArgumentOutOfRangeException(nameof(sortOrder));
        }

        return SetTaskSortOrderAsync(uid, sortOrder, cancellationToken);
    }

    public Task MoveTaskAsync(
        string uid,
        long destinationListId,
        double? sortOrder = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uid);
        if (sortOrder is not null && !double.IsFinite(sortOrder.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(sortOrder));
        }

        return ExecuteWriteAsync(
            (connection, transaction, context) =>
            {
                TaskIdentity identity = GetTaskIdentity(connection, transaction, uid);
                GetListUid(connection, transaction, destinationListId);
                double destinationSortOrder = sortOrder ?? GetNextSortOrder(
                    connection,
                    transaction,
                    "tasks",
                    "list_id = @listId AND parent_uid IS NULL",
                    "@listId",
                    destinationListId);

                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    UPDATE tasks
                    SET list_id = @listId, parent_uid = NULL, sort_order = @sortOrder,
                        modified_utc = @modifiedUtc, sequence = sequence + 1, is_dirty = 1
                    WHERE id = @id;
                    """;
                command.Parameters.AddWithValue("@listId", destinationListId);
                command.Parameters.AddWithValue("@sortOrder", destinationSortOrder);
                command.Parameters.AddWithValue("@modifiedUtc", SqliteMapper.FormatInstant(_clock.UtcNow));
                command.Parameters.AddWithValue("@id", identity.Id);
                command.ExecuteNonQuery();
                LogChange(connection, transaction, TaskEntityType, uid, ChangeOperation.Update);
                context.AddTask(identity.ListId, uid);
                context.AddTask(destinationListId, uid);
            },
            cancellationToken);
    }

    public Task<IReadOnlyList<TodoTask>> GetChildrenAsync(
        string parentUid,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentUid);
        return ExecuteReadAsync<IReadOnlyList<TodoTask>>(
            connection =>
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = $"""
                    SELECT {SqliteMapper.TaskColumns}
                    FROM tasks t
                    WHERE t.parent_uid = @parentUid AND t.is_deleted = 0
                    ORDER BY t.sort_order, t.id;
                    """;
                command.Parameters.AddWithValue("@parentUid", parentUid);
                using SqliteDataReader reader = command.ExecuteReader();
                var children = new List<TodoTask>();
                while (reader.Read())
                {
                    children.Add(SqliteMapper.ReadTask(reader));
                }

                return children;
            },
            cancellationToken);
    }

    public Task<TodoTask> AddStepAsync(
        string parentUid,
        string summary,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentUid);
        ValidateName(summary, nameof(summary));
        return ExecuteWriteAsync(
            (connection, transaction, context) =>
            {
                TodoTask parent = ReadTaskByUid(connection, transaction, parentUid, includeDeleted: false)
                    ?? throw new KeyNotFoundException($"Task '{parentUid}' was not found.");
                var step = new TodoTask
                {
                    ListId = parent.ListId,
                    ParentUid = parentUid,
                    Summary = summary.Trim(),
                };
                PrepareNewTask(connection, transaction, step);
                step.Id = InsertTask(connection, transaction, step);
                LogChange(connection, transaction, TaskEntityType, step.Uid, ChangeOperation.Create);
                context.AddTask(step.ListId, step.Uid);
                context.AddTask(parent.ListId, parentUid);
                return step;
            },
            cancellationToken);
    }

    public Task ReorderStepAsync(
        string uid,
        double sortOrder,
        CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(sortOrder))
        {
            throw new ArgumentOutOfRangeException(nameof(sortOrder));
        }

        return SetTaskSortOrderAsync(uid, sortOrder, cancellationToken);
    }

    public Task<IReadOnlyList<TodoTask>> SearchAsync(
        string text,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (limit is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        string ftsQuery = BuildFtsQuery(text);
        return ExecuteReadAsync<IReadOnlyList<TodoTask>>(
            connection =>
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = $"""
                    SELECT {SqliteMapper.TaskColumns}
                    FROM tasks_fts
                    JOIN tasks t ON t.id = tasks_fts.rowid
                    WHERE tasks_fts MATCH @query AND t.is_deleted = 0
                    ORDER BY rank
                    LIMIT @limit;
                    """;
                command.Parameters.AddWithValue("@query", ftsQuery);
                command.Parameters.AddWithValue("@limit", limit);
                using SqliteDataReader reader = command.ExecuteReader();
                var tasks = new List<TodoTask>();
                while (reader.Read())
                {
                    tasks.Add(SqliteMapper.ReadTask(reader));
                }

                return tasks;
            },
            cancellationToken);
    }

    private void AppendListPredicate(
        StringBuilder sql,
        SqliteCommand command,
        ListQuery query)
    {
        switch (query.Kind)
        {
            case ListQueryKind.UserList:
                sql.Append(" AND t.list_id = @listId");
                command.Parameters.AddWithValue("@listId", query.ListId!.Value);
                break;
            case ListQueryKind.MyDay:
                sql.Append(" AND t.my_day_date = @today");
                command.Parameters.AddWithValue(
                    "@today",
                    SqliteMapper.FormatDateOnly(_clock.Today));
                break;
            case ListQueryKind.Important:
                sql.Append(" AND t.priority BETWEEN 1 AND 4");
                break;
            case ListQueryKind.Planned:
                sql.Append(" AND t.due_utc IS NOT NULL");
                break;
            case ListQueryKind.All:
                break;
            case ListQueryKind.Completed:
                sql.Append(" AND t.status = 2");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(query), query.Kind, "Unknown list-query kind.");
        }
    }

    private static string GetOrderBy(
        SortMode sortMode,
        bool descending,
        ListQueryKind queryKind)
    {
        if (queryKind == ListQueryKind.Completed)
        {
            return descending
                ? " ORDER BY t.completed_utc, t.id"
                : " ORDER BY t.completed_utc DESC, t.id DESC";
        }

        string direction = descending ? " DESC" : string.Empty;
        if (queryKind == ListQueryKind.Important && sortMode == SortMode.Importance)
        {
            return $" ORDER BY t.priority{direction}, t.id{direction}";
        }

        if (queryKind == ListQueryKind.Planned && sortMode == SortMode.DueDate)
        {
            return $" ORDER BY t.due_utc{direction}, t.id{direction}";
        }

        string expression = sortMode switch
        {
            SortMode.Manual => "t.sort_order",
            SortMode.Importance => "t.priority = 0, t.priority",
            SortMode.DueDate => "t.due_utc IS NULL, t.due_utc",
            SortMode.Alphabetical => "t.summary COLLATE NOCASE",
            SortMode.CreationDate => "t.created_utc",
            _ => throw new ArgumentOutOfRangeException(nameof(sortMode), sortMode, "Unknown sort mode."),
        };
        return $" ORDER BY {expression}{direction}, t.id{direction}";
    }

    private static string EscapeLike(string value)
    {
        return value.Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);
    }

    private static string BuildFtsQuery(string value)
    {
        string[] tokens = value.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            throw new ArgumentException("Search text must contain a searchable term.", nameof(value));
        }

        return string.Join(
            " AND ",
            tokens.Select(token => $"\"{token.Replace("\"", "\"\"", StringComparison.Ordinal)}\"*"));
    }

    private void PrepareNewTask(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TodoTask task)
    {
        task.Uid = string.IsNullOrWhiteSpace(task.Uid) ? Guid.NewGuid().ToString("D") : task.Uid;
        ValidateParent(connection, transaction, task);
        DateTimeOffset now = _clock.UtcNow;
        task.Created = task.Created == default ? now : task.Created;
        task.LastModified = now;
        task.IsDirty = true;
        if (!double.IsFinite(task.SortOrder) || task.SortOrder <= 0)
        {
            string predicate = task.ParentUid is null
                ? "list_id = @owner AND parent_uid IS NULL"
                : "parent_uid = @owner";
            task.SortOrder = GetNextSortOrder(
                connection,
                transaction,
                "tasks",
                predicate,
                "@owner",
                task.ParentUid is null ? task.ListId : task.ParentUid);
        }
    }

    private static void ValidateParent(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TodoTask task)
    {
        if (task.ParentUid is null)
        {
            return;
        }

        TaskIdentity parent = GetTaskIdentity(connection, transaction, task.ParentUid);
        if (parent.ListId != task.ListId)
        {
            throw new InvalidOperationException("A step must belong to the same list as its parent.");
        }

        if (task.Uid.Equals(task.ParentUid, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A task cannot be its own parent.");
        }
    }

    private static long InsertTask(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TodoTask task)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO tasks(
                uid, list_id, parent_uid, summary, description, status,
                percent_complete, priority, due_utc, due_is_date_only, due_tzid,
                start_utc, start_is_date_only, completed_utc, created_utc,
                modified_utc, sequence, rrule, my_day_date, sort_order, location,
                url, classification, ical_unknown, remote_href, remote_etag,
                is_dirty, is_deleted)
            VALUES(
                @uid, @listId, @parentUid, @summary, @description, @status,
                @percentComplete, @priority, @dueUtc, @dueIsDateOnly, @dueTzid,
                @startUtc, @startIsDateOnly, @completedUtc, @createdUtc,
                @modifiedUtc, @sequence, @rrule, @myDayDate, @sortOrder, @location,
                @url, @classification, @icalUnknown, @remoteHref, @remoteEtag,
                @isDirty, @isDeleted)
            RETURNING id;
            """;
        AddTaskParameters(command, task);
        return (long)(command.ExecuteScalar()
            ?? throw new InvalidOperationException("Task insert did not return an id."));
    }

    private static void UpdateTaskRow(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TodoTask task)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE tasks
            SET list_id = @listId, parent_uid = @parentUid, summary = @summary,
                description = @description, status = @status,
                percent_complete = @percentComplete, priority = @priority,
                due_utc = @dueUtc, due_is_date_only = @dueIsDateOnly,
                due_tzid = @dueTzid, start_utc = @startUtc,
                start_is_date_only = @startIsDateOnly, completed_utc = @completedUtc,
                modified_utc = @modifiedUtc, sequence = @sequence, rrule = @rrule,
                my_day_date = @myDayDate, sort_order = @sortOrder,
                location = @location, url = @url, classification = @classification,
                ical_unknown = @icalUnknown, remote_href = @remoteHref,
                remote_etag = @remoteEtag, is_dirty = @isDirty,
                is_deleted = @isDeleted
            WHERE id = @id;
            """;
        AddTaskParameters(command, task);
        command.Parameters.AddWithValue("@id", task.Id);
        if (command.ExecuteNonQuery() != 1)
        {
            throw new KeyNotFoundException($"Task '{task.Uid}' was not found.");
        }
    }

    private static void AddTaskParameters(SqliteCommand command, TodoTask task)
    {
        command.Parameters.AddWithValue("@uid", task.Uid);
        command.Parameters.AddWithValue("@listId", task.ListId);
        AddParameter(command, "@parentUid", task.ParentUid);
        command.Parameters.AddWithValue("@summary", task.Summary.Trim());
        AddParameter(command, "@description", task.Description);
        command.Parameters.AddWithValue("@status", (int)task.Status);
        AddParameter(command, "@percentComplete", task.PercentComplete);
        command.Parameters.AddWithValue("@priority", task.Priority);
        AddParameter(command, "@dueUtc", SqliteMapper.FormatStoredDate(task.Due, task.DueIsDateOnly));
        command.Parameters.AddWithValue("@dueIsDateOnly", task.DueIsDateOnly);
        AddParameter(command, "@dueTzid", task.DueTzid);
        AddParameter(command, "@startUtc", SqliteMapper.FormatStoredDate(task.Start, task.StartIsDateOnly));
        command.Parameters.AddWithValue("@startIsDateOnly", task.StartIsDateOnly);
        AddParameter(command, "@completedUtc", SqliteMapper.FormatInstant(task.CompletedAt));
        command.Parameters.AddWithValue("@createdUtc", SqliteMapper.FormatInstant(task.Created));
        command.Parameters.AddWithValue("@modifiedUtc", SqliteMapper.FormatInstant(task.LastModified));
        command.Parameters.AddWithValue("@sequence", task.Sequence);
        AddParameter(command, "@rrule", task.RRule);
        AddParameter(command, "@myDayDate", SqliteMapper.FormatDateOnly(task.MyDayDate));
        command.Parameters.AddWithValue("@sortOrder", task.SortOrder);
        AddParameter(command, "@location", task.Location);
        AddParameter(command, "@url", task.Url);
        command.Parameters.AddWithValue("@classification", (int)task.Class);
        AddParameter(command, "@icalUnknown", task.IcalUnknown);
        AddParameter(command, "@remoteHref", task.RemoteHref);
        AddParameter(command, "@remoteEtag", task.RemoteETag);
        command.Parameters.AddWithValue("@isDirty", task.IsDirty);
        command.Parameters.AddWithValue("@isDeleted", task.IsDeleted);
    }

    private static TodoTask? ReadTaskByUid(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string uid,
        bool includeDeleted)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT {SqliteMapper.TaskColumns}
            FROM tasks t
            WHERE t.uid = @uid{(includeDeleted ? string.Empty : " AND t.is_deleted = 0")};
            """;
        command.Parameters.AddWithValue("@uid", uid);
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? SqliteMapper.ReadTask(reader) : null;
    }

    private static void LoadTaskCollections(SqliteConnection connection, TodoTask task)
    {
        using (SqliteCommand tags = connection.CreateCommand())
        {
            tags.CommandText = """
                SELECT tags.name
                FROM task_tags
                JOIN tags ON tags.id = task_tags.tag_id
                WHERE task_tags.task_id = @taskId
                ORDER BY tags.name COLLATE NOCASE;
                """;
            tags.Parameters.AddWithValue("@taskId", task.Id);
            using SqliteDataReader reader = tags.ExecuteReader();
            while (reader.Read())
            {
                task.Categories.Add(reader.GetString(0));
            }
        }

        using (SqliteCommand alarms = connection.CreateCommand())
        {
            alarms.CommandText = $"SELECT {AlarmColumns} FROM alarms WHERE task_id = @taskId ORDER BY id;";
            alarms.Parameters.AddWithValue("@taskId", task.Id);
            using SqliteDataReader reader = alarms.ExecuteReader();
            while (reader.Read())
            {
                task.Alarms.Add(SqliteMapper.ReadAlarm(reader));
            }
        }

        using SqliteCommand attachments = connection.CreateCommand();
        attachments.CommandText = $"SELECT {AttachmentColumns} FROM attachments WHERE task_id = @taskId ORDER BY id;";
        attachments.Parameters.AddWithValue("@taskId", task.Id);
        using SqliteDataReader attachmentReader = attachments.ExecuteReader();
        while (attachmentReader.Read())
        {
            task.Attachments.Add(SqliteMapper.ReadAttachment(attachmentReader));
        }
    }

    private void ReplaceTaskCollections(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TodoTask task)
    {
        DeleteTaskCollections(connection, transaction, task.Id);

        foreach (string category in task.Categories
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                     .Select(value => value.Trim())
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            using SqliteCommand insertTag = connection.CreateCommand();
            insertTag.Transaction = transaction;
            insertTag.CommandText = "INSERT INTO tags(name) VALUES (@name) ON CONFLICT(name) DO NOTHING;";
            insertTag.Parameters.AddWithValue("@name", category);
            insertTag.ExecuteNonQuery();

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
            alarm.Uid = string.IsNullOrWhiteSpace(alarm.Uid) ? Guid.NewGuid().ToString("D") : alarm.Uid;
            ValidateAlarm(alarm);
            alarm.Id = InsertAlarm(connection, transaction, alarm);
        }

        foreach (TaskAttachment attachment in task.Attachments)
        {
            ValidateAttachment(attachment);
            attachment.TaskId = task.Id;
            attachment.Added = attachment.Added == default ? _clock.UtcNow : attachment.Added;
            attachment.Id = InsertAttachment(connection, transaction, attachment);
        }
    }

    private static void DeleteTaskCollections(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long taskId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM task_tags WHERE task_id = @taskId;
            DELETE FROM alarms WHERE task_id = @taskId;
            DELETE FROM attachments WHERE task_id = @taskId;
            """;
        command.Parameters.AddWithValue("@taskId", taskId);
        command.ExecuteNonQuery();
    }

    private void AdvanceRecurringTask(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TodoTask task,
        DateTimeOffset completedAt)
    {
        DateTimeOffset occurrence = task.Due ?? task.Start ?? completedAt;
        int completedOccurrences = GetCompletionCount(connection, transaction, task.Uid) + 1;
        TimeZoneInfo? timeZone = ResolveTimeZone(task.DueTzid);
        DateTimeOffset? next = RecurrenceRule.Parse(task.RRule!)
            .ComputeNext(occurrence, completedOccurrences, timeZone);

        using (SqliteCommand history = connection.CreateCommand())
        {
            history.Transaction = transaction;
            history.CommandText = """
                INSERT INTO completion_history(task_uid, completed_utc, occurrence_due_utc)
                VALUES (@taskUid, @completedUtc, @occurrenceDueUtc);
                """;
            history.Parameters.AddWithValue("@taskUid", task.Uid);
            history.Parameters.AddWithValue("@completedUtc", SqliteMapper.FormatInstant(completedAt));
            history.Parameters.AddWithValue("@occurrenceDueUtc", SqliteMapper.FormatInstant(occurrence));
            history.ExecuteNonQuery();
        }

        if (next is null)
        {
            task.Status = Wintodo.Core.Models.TaskStatus.Completed;
            task.PercentComplete = 100;
            task.CompletedAt = completedAt;
            return;
        }

        TimeSpan shift = next.Value - occurrence;
        task.Due = task.Due is null ? null : task.Due.Value + shift;
        task.Start = task.Start is null ? null : task.Start.Value + shift;
        task.Status = Wintodo.Core.Models.TaskStatus.NeedsAction;
        task.PercentComplete = 0;
        task.CompletedAt = null;
        ShiftAbsoluteAlarms(connection, transaction, task.Id, shift);
    }

    private static int GetCompletionCount(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string taskUid)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM completion_history WHERE task_uid = @taskUid;";
        command.Parameters.AddWithValue("@taskUid", taskUid);
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static TimeZoneInfo? ResolveTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return null;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return null;
        }
        catch (InvalidTimeZoneException)
        {
            return null;
        }
    }

    private static void ShiftAbsoluteAlarms(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long taskId,
        TimeSpan shift)
    {
        var alarms = new List<(long Id, DateTimeOffset Trigger)>();
        using (SqliteCommand query = connection.CreateCommand())
        {
            query.Transaction = transaction;
            query.CommandText = """
                SELECT id, trigger_utc
                FROM alarms
                WHERE task_id = @taskId AND trigger_utc IS NOT NULL;
                """;
            query.Parameters.AddWithValue("@taskId", taskId);
            using SqliteDataReader reader = query.ExecuteReader();
            while (reader.Read())
            {
                alarms.Add((
                    reader.GetInt64(0),
                    DateTimeOffset.Parse(
                        reader.GetString(1),
                        System.Globalization.CultureInfo.InvariantCulture)));
            }
        }

        foreach ((long id, DateTimeOffset trigger) in alarms)
        {
            using SqliteCommand update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE alarms
                SET trigger_utc = @triggerUtc, snoozed_until_utc = NULL,
                    is_acknowledged = 0, scheduled_toast_tag = NULL
                WHERE id = @id;
                """;
            update.Parameters.AddWithValue("@triggerUtc", SqliteMapper.FormatInstant(trigger + shift));
            update.Parameters.AddWithValue("@id", id);
            update.ExecuteNonQuery();
        }
    }

    private Task SetTaskCompletionAsync(
        string uid,
        Wintodo.Core.Models.TaskStatus status,
        int percentComplete,
        DateTimeOffset? completedAt,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uid);
        return ExecuteWriteAsync(
            (connection, transaction, context) =>
            {
                TaskIdentity identity = GetTaskIdentity(connection, transaction, uid);
                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    UPDATE tasks
                    SET status = @status, percent_complete = @percentComplete,
                        completed_utc = @completedUtc, modified_utc = @modifiedUtc,
                        sequence = sequence + 1, is_dirty = 1
                    WHERE id = @id;
                    """;
                command.Parameters.AddWithValue("@status", (int)status);
                command.Parameters.AddWithValue("@percentComplete", percentComplete);
                AddParameter(command, "@completedUtc", SqliteMapper.FormatInstant(completedAt));
                command.Parameters.AddWithValue("@modifiedUtc", SqliteMapper.FormatInstant(_clock.UtcNow));
                command.Parameters.AddWithValue("@id", identity.Id);
                command.ExecuteNonQuery();
                LogChange(connection, transaction, TaskEntityType, uid, ChangeOperation.Update);
                context.AddTask(identity.ListId, uid);
            },
            cancellationToken);
    }

    private Task SetTaskSortOrderAsync(
        string uid,
        double sortOrder,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uid);
        return ExecuteWriteAsync(
            (connection, transaction, context) =>
            {
                TaskIdentity identity = GetTaskIdentity(connection, transaction, uid);
                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    UPDATE tasks
                    SET sort_order = @sortOrder, modified_utc = @modifiedUtc,
                        sequence = sequence + 1, is_dirty = 1
                    WHERE id = @id;
                    """;
                command.Parameters.AddWithValue("@sortOrder", sortOrder);
                command.Parameters.AddWithValue("@modifiedUtc", SqliteMapper.FormatInstant(_clock.UtcNow));
                command.Parameters.AddWithValue("@id", identity.Id);
                command.ExecuteNonQuery();
                LogChange(connection, transaction, TaskEntityType, uid, ChangeOperation.Update);
                context.AddTask(identity.ListId, uid);
            },
            cancellationToken);
    }

    private static void ValidateAlarm(TaskAlarm alarm)
    {
        ArgumentNullException.ThrowIfNull(alarm);
        if ((alarm.Trigger is null) == (alarm.TriggerOffsetSeconds is null))
        {
            throw new ArgumentException(
                "An alarm must have exactly one absolute or relative trigger.",
                nameof(alarm));
        }
    }

    private static long InsertAlarm(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TaskAlarm alarm)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO alarms(
                task_id, uid, action, trigger_utc, trigger_offset_seconds,
                trigger_related, description, snoozed_until_utc,
                is_acknowledged, scheduled_toast_tag)
            VALUES(
                @taskId, @uid, @action, @triggerUtc, @triggerOffsetSeconds,
                @triggerRelated, @description, @snoozedUntilUtc,
                @isAcknowledged, @scheduledToastTag)
            RETURNING id;
            """;
        command.Parameters.AddWithValue("@taskId", alarm.TaskId);
        command.Parameters.AddWithValue("@uid", alarm.Uid);
        command.Parameters.AddWithValue("@action", (int)alarm.Action);
        AddParameter(command, "@triggerUtc", SqliteMapper.FormatInstant(alarm.Trigger));
        AddParameter(command, "@triggerOffsetSeconds", alarm.TriggerOffsetSeconds);
        command.Parameters.AddWithValue("@triggerRelated", (int)alarm.TriggerRelated);
        AddParameter(command, "@description", alarm.Description);
        AddParameter(command, "@snoozedUntilUtc", SqliteMapper.FormatInstant(alarm.SnoozedUntil));
        command.Parameters.AddWithValue("@isAcknowledged", alarm.IsAcknowledged);
        AddParameter(command, "@scheduledToastTag", alarm.ScheduledToastTag);
        return (long)(command.ExecuteScalar()
            ?? throw new InvalidOperationException("Alarm insert did not return an id."));
    }

    private static void ValidateAttachment(TaskAttachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        if (string.IsNullOrWhiteSpace(attachment.FileName)
            || string.IsNullOrWhiteSpace(attachment.RelativePath))
        {
            throw new ArgumentException("Attachment file name and relative path are required.", nameof(attachment));
        }

        if (attachment.SizeBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attachment));
        }
    }

    private static long InsertAttachment(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TaskAttachment attachment)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO attachments(
                task_id, file_name, relative_path, size_bytes, mime_type, added_utc)
            VALUES(
                @taskId, @fileName, @relativePath, @sizeBytes, @mimeType, @addedUtc)
            RETURNING id;
            """;
        command.Parameters.AddWithValue("@taskId", attachment.TaskId);
        command.Parameters.AddWithValue("@fileName", attachment.FileName);
        command.Parameters.AddWithValue("@relativePath", attachment.RelativePath);
        command.Parameters.AddWithValue("@sizeBytes", attachment.SizeBytes);
        AddParameter(command, "@mimeType", attachment.MimeType);
        command.Parameters.AddWithValue("@addedUtc", SqliteMapper.FormatInstant(attachment.Added));
        return (long)(command.ExecuteScalar()
            ?? throw new InvalidOperationException("Attachment insert did not return an id."));
    }
}
