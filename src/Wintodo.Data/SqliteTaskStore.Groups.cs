using Microsoft.Data.Sqlite;
using Wintodo.Core.Models;
using Wintodo.Data.Mapping;

namespace Wintodo.Data;

public sealed partial class SqliteTaskStore
{
    public Task<IReadOnlyList<ListGroup>> GetGroupsAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteReadAsync<IReadOnlyList<ListGroup>>(
            connection =>
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = """
                    SELECT id, name, sort_order, is_expanded, created_utc
                    FROM list_groups
                    ORDER BY sort_order, id;
                    """;
                using SqliteDataReader reader = command.ExecuteReader();
                var groups = new List<ListGroup>();
                while (reader.Read())
                {
                    groups.Add(SqliteMapper.ReadGroup(reader));
                }

                return groups;
            },
            cancellationToken);
    }

    public Task<ListGroup> CreateGroupAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        ValidateName(name, nameof(name));
        return ExecuteWriteAsync(
            (connection, transaction, context) =>
            {
                double sortOrder = GetNextSortOrder(connection, transaction, "list_groups");
                DateTimeOffset now = _clock.UtcNow;
                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO list_groups(name, sort_order, is_expanded, created_utc)
                    VALUES (@name, @sortOrder, 1, @createdUtc)
                    RETURNING id;
                    """;
                command.Parameters.AddWithValue("@name", name.Trim());
                command.Parameters.AddWithValue("@sortOrder", sortOrder);
                command.Parameters.AddWithValue("@createdUtc", SqliteMapper.FormatInstant(now));
                long id = (long)(command.ExecuteScalar()
                    ?? throw new InvalidOperationException("Group insert did not return an id."));
                return new ListGroup
                {
                    Id = id,
                    Name = name.Trim(),
                    SortOrder = sortOrder,
                    IsExpanded = true,
                    Created = now,
                };
            },
            cancellationToken);
    }

    public Task RenameGroupAsync(
        long groupId,
        string name,
        CancellationToken cancellationToken = default)
    {
        ValidateName(name, nameof(name));
        return ExecuteWriteAsync(
            (connection, transaction, context) =>
            {
                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    UPDATE list_groups
                    SET name = @name
                    WHERE id = @groupId;
                    """;
                command.Parameters.AddWithValue("@name", name.Trim());
                command.Parameters.AddWithValue("@groupId", groupId);
                if (command.ExecuteNonQuery() != 1)
                {
                    throw new KeyNotFoundException($"Group {groupId} was not found.");
                }
            },
            cancellationToken);
    }

    public Task DeleteGroupAsync(long groupId, CancellationToken cancellationToken = default)
    {
        return ExecuteWriteAsync(
            (connection, transaction, context) =>
            {
                using SqliteCommand selectLists = connection.CreateCommand();
                selectLists.Transaction = transaction;
                selectLists.CommandText = """
                    SELECT id
                    FROM lists
                    WHERE group_id = @groupId AND is_deleted = 0;
                    """;
                selectLists.Parameters.AddWithValue("@groupId", groupId);
                using (SqliteDataReader reader = selectLists.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        context.AddList(reader.GetInt64(0));
                    }
                }

                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    UPDATE lists
                    SET group_id = NULL
                    WHERE group_id = @groupId;

                    DELETE FROM list_groups
                    WHERE id = @groupId;
                    """;
                command.Parameters.AddWithValue("@groupId", groupId);
                command.ExecuteNonQuery();
            },
            cancellationToken);
    }
}
