using Microsoft.Data.Sqlite;

namespace Wintodo.Data;

public sealed partial class SqliteTaskStore
{
    private const string UpsertSettingSql = """
        INSERT INTO settings(key, value) VALUES (@key, @value)
        ON CONFLICT(key) DO UPDATE SET value = excluded.value;
        """;

    public Task<IReadOnlyDictionary<string, string>> GetSettingsAsync(
        IReadOnlyList<string> keys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        string[] requested = keys.ToArray();
        foreach (string key in requested) ValidateSettingKey(key);
        return ExecuteReadAsync<IReadOnlyDictionary<string, string>>(connection =>
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            if (requested.Length == 0) return values;
            using SqliteCommand command = connection.CreateCommand();
            var names = new string[requested.Length];
            for (int i = 0; i < requested.Length; i++)
            {
                names[i] = "@k" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                command.Parameters.AddWithValue(names[i], requested[i]);
            }
            // Only generated parameter names enter the SQL; the keys themselves are bound.
            command.CommandText = $"SELECT key, value FROM settings WHERE key IN ({string.Join(',', names)});";
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read()) values.Add(reader.GetString(0), reader.GetString(1));
            return values;
        }, cancellationToken);
    }

    public Task SetSettingsAsync(IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var entries = settings.ToArray();
        foreach ((string key, string value) in entries)
        {
            ValidateSettingKey(key);
            ArgumentNullException.ThrowIfNull(value);
        }
        return ExecuteWriteAsync((connection, transaction, _) =>
        {
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = UpsertSettingSql;
            SqliteParameter keyParameter = command.Parameters.Add("@key", SqliteType.Text);
            SqliteParameter valueParameter = command.Parameters.Add("@value", SqliteType.Text);
            foreach ((string key, string value) in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                keyParameter.Value = key;
                valueParameter.Value = value;
                command.ExecuteNonQuery();
            }
        }, cancellationToken);
    }

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
                command.CommandText = UpsertSettingSql;
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
