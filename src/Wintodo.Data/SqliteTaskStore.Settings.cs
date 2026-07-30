using Microsoft.Data.Sqlite;

namespace Wintodo.Data;

public sealed partial class SqliteTaskStore
{
    public Task<string?> GetSettingAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        ValidateSettingKey(key);
        return ExecuteReadAsync(
            connection =>
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "SELECT value FROM settings WHERE key = @key;";
                command.Parameters.AddWithValue("@key", key);
                return command.ExecuteScalar() as string;
            },
            cancellationToken);
    }

    public Task SetSettingAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default)
    {
        ValidateSettingKey(key);
        ArgumentNullException.ThrowIfNull(value);
        return ExecuteWriteAsync(
            (connection, transaction, _) =>
            {
                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO settings(key, value)
                    VALUES (@key, @value)
                    ON CONFLICT(key) DO UPDATE SET value = excluded.value;
                    """;
                command.Parameters.AddWithValue("@key", key);
                command.Parameters.AddWithValue("@value", value);
                command.ExecuteNonQuery();
            },
            cancellationToken);
    }

    public Task DeleteSettingAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        ValidateSettingKey(key);
        return ExecuteWriteAsync(
            (connection, transaction, _) =>
            {
                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM settings WHERE key = @key;";
                command.Parameters.AddWithValue("@key", key);
                command.ExecuteNonQuery();
            },
            cancellationToken);
    }

    private static void ValidateSettingKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (key.Length > 128)
        {
            throw new ArgumentOutOfRangeException(nameof(key), "Setting keys cannot exceed 128 characters.");
        }
    }
}
