using System.Globalization;
using Microsoft.Data.Sqlite;
using Wintodo.Core.Services;

namespace Wintodo.Data;

public sealed partial class SqliteTaskStore
{
    public Task VacuumAsync(CancellationToken cancellationToken = default)
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
                    using SqliteCommand command = writer.CreateCommand();
                    command.CommandText = """
                        PRAGMA wal_checkpoint(TRUNCATE);
                        VACUUM;
                        """;
                    command.ExecuteNonQuery();
                }
                finally
                {
                    _writeGate.Release();
                }
            },
            cancellationToken);
    }

    public Task<StoreStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteReadAsync(
            connection => new StoreStats
            {
                DatabasePath = _databasePath,
                SchemaVersion = checked((int)ReadInt64(
                    connection,
                    "SELECT COALESCE(MAX(version), 0) FROM schema_version;")),
                ListCount = ReadInt64(
                    connection,
                    "SELECT COUNT(*) FROM lists WHERE is_deleted = 0;"),
                TaskCount = ReadInt64(
                    connection,
                    "SELECT COUNT(*) FROM tasks WHERE is_deleted = 0;"),
                PendingChangeCount = ReadInt64(
                    connection,
                    "SELECT COUNT(*) FROM change_log WHERE synced = 0;"),
                DatabaseSizeBytes = File.Exists(_databasePath)
                    ? new FileInfo(_databasePath).Length
                    : 0,
                ActiveReadConnections = Math.Max(
                    0,
                    Volatile.Read(ref _activeReadConnections) - 1),
            },
            cancellationToken);
    }

    private static long ReadInt64(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }
}
