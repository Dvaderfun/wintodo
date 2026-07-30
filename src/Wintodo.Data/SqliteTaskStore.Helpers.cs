using Microsoft.Data.Sqlite;
using Wintodo.Core.Models;
using Wintodo.Data.Mapping;

namespace Wintodo.Data;

public sealed partial class SqliteTaskStore
{
    private const int TaskEntityType = 0;
    private const int ListEntityType = 1;

    private static void AddParameter(SqliteCommand command, string name, object? value)
    {
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    private void LogChange(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int entityType,
        string entityUid,
        ChangeOperation operation)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO change_log(entity_type, entity_uid, operation, changed_utc, synced)
            VALUES (@entityType, @entityUid, @operation, @changedUtc, 0);
            """;
        command.Parameters.AddWithValue("@entityType", entityType);
        command.Parameters.AddWithValue("@entityUid", entityUid);
        command.Parameters.AddWithValue("@operation", (int)operation);
        command.Parameters.AddWithValue("@changedUtc", SqliteMapper.FormatInstant(_clock.UtcNow));
        command.ExecuteNonQuery();
    }

    private static string GetListUid(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long listId,
        bool includeDeleted = false)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = includeDeleted
            ? "SELECT uid FROM lists WHERE id = @id;"
            : "SELECT uid FROM lists WHERE id = @id AND is_deleted = 0;";
        command.Parameters.AddWithValue("@id", listId);
        return command.ExecuteScalar() as string
            ?? throw new KeyNotFoundException($"List {listId} was not found.");
    }

    private static TaskIdentity GetTaskIdentity(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string uid,
        bool includeDeleted = false)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = includeDeleted
            ? "SELECT id, list_id FROM tasks WHERE uid = @uid;"
            : "SELECT id, list_id FROM tasks WHERE uid = @uid AND is_deleted = 0;";
        command.Parameters.AddWithValue("@uid", uid);
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new KeyNotFoundException($"Task '{uid}' was not found.");
        }

        return new TaskIdentity(reader.GetInt64(0), reader.GetInt64(1));
    }

    private static double GetNextSortOrder(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string? predicate = null,
        string? parameterName = null,
        object? parameterValue = null)
    {
        string sql = table switch
        {
            "lists" => "SELECT COALESCE(MAX(sort_order), 0) + 1 FROM lists WHERE is_deleted = 0",
            "list_groups" => "SELECT COALESCE(MAX(sort_order), 0) + 1 FROM list_groups",
            "tasks" => "SELECT COALESCE(MAX(sort_order), 0) + 1 FROM tasks WHERE is_deleted = 0",
            _ => throw new ArgumentOutOfRangeException(nameof(table)),
        };

        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = predicate is null ? sql + ";" : sql + " AND " + predicate + ";";
        if (parameterName is not null)
        {
            AddParameter(command, parameterName, parameterValue);
        }

        return Convert.ToDouble(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void ValidateTask(TodoTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (string.IsNullOrWhiteSpace(task.Summary))
        {
            throw new ArgumentException("Task summary is required.", nameof(task));
        }

        if (task.Priority is < 0 or > 9)
        {
            throw new ArgumentOutOfRangeException(nameof(task), "Task priority must be between 0 and 9.");
        }

        if (task.PercentComplete is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(task), "Percent complete must be between 0 and 100.");
        }
    }

    private static void ValidateName(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty name is required.", parameterName);
        }
    }

    private readonly struct TaskIdentity
    {
        internal TaskIdentity(long id, long listId)
        {
            Id = id;
            ListId = listId;
        }

        internal long Id { get; }

        internal long ListId { get; }
    }
}
