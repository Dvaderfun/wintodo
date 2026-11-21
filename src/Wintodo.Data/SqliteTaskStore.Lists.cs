using Microsoft.Data.Sqlite;
using Wintodo.Core.Models;
using Wintodo.Core.Queries;
using Wintodo.Data.Mapping;

namespace Wintodo.Data;

public sealed partial class SqliteTaskStore
{
    private const string ListColumns = """
        id, uid, display_name, color, background_theme, group_id,
        sort_order, sort_mode, sort_descending, show_completed,
        completed_expanded, is_default, is_deleted, created_utc,
        modified_utc, account_id, remote_url, remote_ctag, sync_token,
        icon_emoji
        """;

    public Task<IReadOnlyList<TodoList>> GetListsAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteReadAsync<IReadOnlyList<TodoList>>(
            connection =>
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = """
                    SELECT id, uid, display_name, color, background_theme, group_id,
                           sort_order, sort_mode, sort_descending, show_completed,
                           completed_expanded, is_default, is_deleted, created_utc,
                           modified_utc, account_id, remote_url, remote_ctag, sync_token,
                           icon_emoji,
                           account_id IS NOT NULL AND remote_url IS NOT NULL AND NOT EXISTS(
                               SELECT 1 FROM settings
                               WHERE key = 'sync.list.' || lists.uid || '.disabled'
                                     AND value = 'true'
                           ),
                           (
                               SELECT COUNT(*)
                               FROM tasks
                               WHERE tasks.list_id = lists.id
                                     AND tasks.parent_uid IS NULL
                                     AND tasks.status <> 2
                                     AND tasks.is_deleted = 0
                           )
                    FROM lists
                    WHERE is_deleted = 0
                    ORDER BY is_default DESC, sort_order, id;
                    """;
                using SqliteDataReader reader = command.ExecuteReader();
                var lists = new List<TodoList>();
                while (reader.Read())
                {
                    TodoList list = SqliteMapper.ReadList(reader);
                    list.IsSyncEnabled = reader.GetBoolean(20);
                    list.IncompleteTaskCount = checked((int)reader.GetInt64(21));
                    lists.Add(list);
                }

                return lists;
            },
            cancellationToken);
    }

    public Task<IReadOnlyDictionary<long, int>> GetIncompleteTaskCountsAsync(
        CancellationToken cancellationToken = default)
    {
        return ExecuteReadAsync<IReadOnlyDictionary<long, int>>(
            connection =>
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = """
                    SELECT lists.id, COUNT(tasks.id)
                    FROM lists
                    LEFT JOIN tasks
                           ON tasks.list_id = lists.id
                          AND tasks.parent_uid IS NULL
                          AND tasks.status <> 2
                          AND tasks.is_deleted = 0
                    WHERE lists.is_deleted = 0
                    GROUP BY lists.id;
                    """;
                using SqliteDataReader reader = command.ExecuteReader();
                var counts = new Dictionary<long, int>();
                while (reader.Read())
                {
                    counts.Add(reader.GetInt64(0), checked((int)reader.GetInt64(1)));
                }

                return counts;
            },
            cancellationToken);
    }

    public Task<IReadOnlyDictionary<ListQueryKind, int>> GetSmartListTaskCountsAsync(
        CancellationToken cancellationToken = default)
    {
        return ExecuteReadAsync<IReadOnlyDictionary<ListQueryKind, int>>(
            connection =>
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = $"""
                    SELECT
                        COALESCE(SUM(CASE
                            WHEN t.status <> 2 AND {MyDayPredicate} THEN 1
                            ELSE 0
                        END), 0),
                        COALESCE(SUM(CASE
                            WHEN t.status <> 2 AND t.priority BETWEEN 1 AND 4 THEN 1
                            ELSE 0
                        END), 0),
                        COALESCE(SUM(CASE
                            WHEN t.status <> 2 AND t.due_utc IS NOT NULL THEN 1
                            ELSE 0
                        END), 0),
                        COALESCE(SUM(CASE WHEN t.status <> 2 THEN 1 ELSE 0 END), 0),
                        COALESCE(SUM(CASE WHEN t.status = 2 THEN 1 ELSE 0 END), 0)
                    FROM tasks t
                    JOIN lists l ON l.id = t.list_id AND l.is_deleted = 0
                    WHERE t.parent_uid IS NULL
                      AND t.is_deleted = 0;
                    """;
                AddMyDayParameters(command);
                using SqliteDataReader reader = command.ExecuteReader();
                if (!reader.Read())
                {
                    throw new InvalidOperationException("The smart-list count query returned no row.");
                }

                return new Dictionary<ListQueryKind, int>
                {
                    [ListQueryKind.MyDay] = checked((int)reader.GetInt64(0)),
                    [ListQueryKind.Important] = checked((int)reader.GetInt64(1)),
                    [ListQueryKind.Planned] = checked((int)reader.GetInt64(2)),
                    [ListQueryKind.All] = checked((int)reader.GetInt64(3)),
                    [ListQueryKind.Completed] = checked((int)reader.GetInt64(4)),
                };
            },
            cancellationToken);
    }

    public Task<TodoList> CreateListAsync(
        string displayName,
        ListColor color = ListColor.Blue,
        long? groupId = null,
        CancellationToken cancellationToken = default)
    {
        ValidateName(displayName, nameof(displayName));
        return ExecuteWriteAsync(
            (connection, transaction, context) =>
            {
                string uid = Guid.NewGuid().ToString("D");
                DateTimeOffset now = _clock.UtcNow;
                double sortOrder = GetNextSortOrder(connection, transaction, "lists");

                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO lists(
                        uid, display_name, color, background_theme, group_id,
                        sort_order, sort_mode, sort_descending, show_completed,
                        completed_expanded, is_deleted, created_utc, modified_utc)
                    VALUES(
                        @uid, @displayName, @color, NULL, @groupId,
                        @sortOrder, 0, 0, 1, 0, 0, @createdUtc, @modifiedUtc)
                    RETURNING id;
                    """;
                command.Parameters.AddWithValue("@uid", uid);
                command.Parameters.AddWithValue("@displayName", displayName.Trim());
                command.Parameters.AddWithValue("@color", (int)color);
                AddParameter(command, "@groupId", groupId);
                command.Parameters.AddWithValue("@sortOrder", sortOrder);
                command.Parameters.AddWithValue("@createdUtc", SqliteMapper.FormatInstant(now));
                command.Parameters.AddWithValue("@modifiedUtc", SqliteMapper.FormatInstant(now));
                long id = (long)(command.ExecuteScalar()
                    ?? throw new InvalidOperationException("List insert did not return an id."));

                LogChange(connection, transaction, ListEntityType, uid, ChangeOperation.Create);
                context.AddList(id);
                return new TodoList
                {
                    Id = id,
                    Uid = uid,
                    DisplayName = displayName.Trim(),
                    Color = color,
                    GroupId = groupId,
                    SortOrder = sortOrder,
                    ShowCompleted = true,
                    Created = now,
                    Modified = now,
                };
            },
            cancellationToken);
    }

    public Task<TodoList> EnsureDefaultListAsync(
        string displayName,
        CancellationToken cancellationToken = default)
    {
        ValidateName(displayName, nameof(displayName));
        return ExecuteWriteAsync(
            (connection, transaction, context) =>
            {
                using (SqliteCommand query = connection.CreateCommand())
                {
                    query.Transaction = transaction;
                    query.CommandText = $"""
                        SELECT {ListColumns}
                        FROM lists
                        WHERE is_default = 1 AND is_deleted = 0
                        ORDER BY id
                        LIMIT 1;
                        """;
                    using SqliteDataReader reader = query.ExecuteReader();
                    if (reader.Read())
                    {
                        return SqliteMapper.ReadList(reader);
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
                        completed_expanded, is_default, is_deleted, created_utc, modified_utc)
                    VALUES(
                        @uid, @displayName, @color, NULL, NULL,
                        @sortOrder, 0, 0, 1, 0, 1, 0, @createdUtc, @modifiedUtc)
                    RETURNING id;
                    """;
                insert.Parameters.AddWithValue("@uid", uid);
                insert.Parameters.AddWithValue("@displayName", displayName.Trim());
                insert.Parameters.AddWithValue("@color", (int)ListColor.Blue);
                insert.Parameters.AddWithValue("@sortOrder", sortOrder);
                insert.Parameters.AddWithValue("@createdUtc", SqliteMapper.FormatInstant(now));
                insert.Parameters.AddWithValue("@modifiedUtc", SqliteMapper.FormatInstant(now));
                long id = (long)(insert.ExecuteScalar()
                    ?? throw new InvalidOperationException("Default list insert did not return an id."));

                LogChange(connection, transaction, ListEntityType, uid, ChangeOperation.Create);
                context.AddList(id);
                return new TodoList
                {
                    Id = id,
                    Uid = uid,
                    DisplayName = displayName.Trim(),
                    Color = ListColor.Blue,
                    SortOrder = sortOrder,
                    ShowCompleted = true,
                    IsDefault = true,
                    Created = now,
                    Modified = now,
                };
            },
            cancellationToken);
    }

    public Task RenameListAsync(
        long listId,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        ValidateName(displayName, nameof(displayName));
        return UpdateListAsync(
            listId,
            "display_name = @value",
            displayName.Trim(),
            cancellationToken);
    }

    public Task SetListColorAsync(
        long listId,
        ListColor color,
        CancellationToken cancellationToken = default)
    {
        return UpdateListAsync(listId, "color = @value", (int)color, cancellationToken);
    }

    public Task SetListIconEmojiAsync(
        long listId,
        string? iconEmoji,
        CancellationToken cancellationToken = default)
    {
        if (!ListEmoji.TryNormalize(iconEmoji, out string? normalized))
        {
            throw new ArgumentException(
                "A list icon must contain one emoji.",
                nameof(iconEmoji));
        }

        return ExecuteWriteAsync(
            (connection, transaction, context) =>
            {
                string listUid = GetListUid(connection, transaction, listId);
                using (SqliteCommand guard = connection.CreateCommand())
                {
                    guard.Transaction = transaction;
                    guard.CommandText = "SELECT is_default FROM lists WHERE id = @listId;";
                    guard.Parameters.AddWithValue("@listId", listId);
                    if (Convert.ToInt64(
                            guard.ExecuteScalar() ?? 0L,
                            System.Globalization.CultureInfo.InvariantCulture) == 1)
                    {
                        throw new InvalidOperationException(
                            "The default list uses its fixed Home icon.");
                    }
                }

                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    UPDATE lists
                    SET icon_emoji = @iconEmoji, modified_utc = @modifiedUtc
                    WHERE id = @listId AND is_deleted = 0;
                    """;
                command.Parameters.AddWithValue("@listId", listId);
                AddParameter(command, "@iconEmoji", normalized);
                command.Parameters.AddWithValue(
                    "@modifiedUtc",
                    SqliteMapper.FormatInstant(_clock.UtcNow));
                if (command.ExecuteNonQuery() != 1)
                {
                    throw new KeyNotFoundException($"List {listId} was not found.");
                }

                LogChange(connection, transaction, ListEntityType, listUid, ChangeOperation.Update);
                context.AddList(listId);
            },
            cancellationToken);
    }

    public Task SetListBackgroundThemeAsync(
        long listId,
        string? backgroundTheme,
        CancellationToken cancellationToken = default)
    {
        if (backgroundTheme is { Length: > 32 })
        {
            throw new ArgumentOutOfRangeException(
                nameof(backgroundTheme),
                "List background theme identifiers cannot exceed 32 characters.");
        }

        return UpdateListAsync(
            listId,
            "background_theme = @value",
            string.IsNullOrWhiteSpace(backgroundTheme) ? null : backgroundTheme,
            cancellationToken);
    }

    public Task SetListSortAsync(
        long listId,
        SortMode sortMode,
        bool descending,
        CancellationToken cancellationToken = default)
    {
        return UpdateListAsync(
            listId,
            "sort_mode = @value, sort_descending = @secondaryValue",
            (int)sortMode,
            descending,
            cancellationToken);
    }

    public Task SetListShowCompletedAsync(
        long listId,
        bool showCompleted,
        CancellationToken cancellationToken = default)
    {
        return UpdateListAsync(
            listId,
            "show_completed = @value",
            showCompleted,
            cancellationToken);
    }

    public Task SetListCompletedExpandedAsync(
        long listId,
        bool completedExpanded,
        CancellationToken cancellationToken = default)
    {
        return UpdateListAsync(
            listId,
            "completed_expanded = @value",
            completedExpanded,
            cancellationToken);
    }

    public Task ReorderListAsync(
        long listId,
        double sortOrder,
        CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(sortOrder))
        {
            throw new ArgumentOutOfRangeException(nameof(sortOrder));
        }

        return UpdateListAsync(listId, "sort_order = @value", sortOrder, cancellationToken);
    }

    public Task MoveListToGroupAsync(
        long listId,
        long? groupId,
        CancellationToken cancellationToken = default)
    {
        return UpdateListAsync(listId, "group_id = @value", groupId, cancellationToken);
    }

    public Task DeleteListAsync(long listId, CancellationToken cancellationToken = default)
    {
        return ExecuteWriteAsync(
            (connection, transaction, context) =>
            {
                GuardMoveObligations(connection, transaction, listId: listId);
                string listUid = GetListUid(connection, transaction, listId);
                using (SqliteCommand guard = connection.CreateCommand())
                {
                    guard.Transaction = transaction;
                    guard.CommandText = "SELECT is_default FROM lists WHERE id = @listId;";
                    guard.Parameters.AddWithValue("@listId", listId);
                    if (Convert.ToInt64(
                        guard.ExecuteScalar() ?? 0L,
                        System.Globalization.CultureInfo.InvariantCulture) == 1)
                    {
                        throw new InvalidOperationException("The default list cannot be deleted.");
                    }
                }

                var taskUids = new List<string>();
                using (SqliteCommand selectTasks = connection.CreateCommand())
                {
                    selectTasks.Transaction = transaction;
                    selectTasks.CommandText = """
                        SELECT uid
                        FROM tasks
                        WHERE list_id = @listId AND is_deleted = 0;
                        """;
                    selectTasks.Parameters.AddWithValue("@listId", listId);
                    using SqliteDataReader reader = selectTasks.ExecuteReader();
                    while (reader.Read())
                    {
                        taskUids.Add(reader.GetString(0));
                    }
                }

                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = """
                        UPDATE lists
                        SET is_deleted = 1, modified_utc = @modifiedUtc
                        WHERE id = @listId AND is_deleted = 0;

                        INSERT OR IGNORE INTO list_deleted_tasks(list_id, task_uid)
                        SELECT @listId, uid FROM tasks WHERE list_id = @listId AND is_deleted = 0;

                        UPDATE tasks
                        SET is_deleted = 1, is_dirty = 1, remote_delete_state = 0,
                            modified_utc = @modifiedUtc, sequence = sequence + 1
                        WHERE list_id = @listId AND is_deleted = 0;
                        """;
                    command.Parameters.AddWithValue("@listId", listId);
                    command.Parameters.AddWithValue("@modifiedUtc", SqliteMapper.FormatInstant(_clock.UtcNow));
                    command.ExecuteNonQuery();
                }

                LogChange(connection, transaction, ListEntityType, listUid, ChangeOperation.Delete);
                foreach (string taskUid in taskUids)
                {
                    LogChange(connection, transaction, TaskEntityType, taskUid, ChangeOperation.Delete);
                    context.AddTask(listId, taskUid);
                }

                context.AddList(listId);
            },
            cancellationToken);
    }

    private Task UpdateListAsync(
        long listId,
        string assignment,
        object? value,
        CancellationToken cancellationToken)
    {
        return UpdateListAsync(
            listId,
            assignment,
            value,
            secondaryValue: null,
            cancellationToken);
    }

    private Task UpdateListAsync(
        long listId,
        string assignment,
        object? value,
        object? secondaryValue,
        CancellationToken cancellationToken)
    {
        return ExecuteWriteAsync(
            (connection, transaction, context) =>
            {
                string listUid = GetListUid(connection, transaction, listId);
                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = $"""
                    UPDATE lists
                    SET {assignment}, modified_utc = @modifiedUtc
                    WHERE id = @listId AND is_deleted = 0;
                    """;
                command.Parameters.AddWithValue("@listId", listId);
                AddParameter(command, "@value", value);
                AddParameter(command, "@secondaryValue", secondaryValue);
                command.Parameters.AddWithValue("@modifiedUtc", SqliteMapper.FormatInstant(_clock.UtcNow));
                if (command.ExecuteNonQuery() != 1)
                {
                    throw new KeyNotFoundException($"List {listId} was not found.");
                }

                LogChange(connection, transaction, ListEntityType, listUid, ChangeOperation.Update);
                context.AddList(listId);
            },
            cancellationToken);
    }
}
