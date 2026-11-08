using Microsoft.Data.Sqlite;

namespace Wintodo.Data.Tests;

internal static class TestSqliteConnections
{
    internal static SqliteConnection Create(string path, SqliteOpenMode mode = SqliteOpenMode.ReadWriteCreate)
    {
        return new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
            Pooling = false,
        }.ToString());
    }

    internal static void ClearStorePools(string path)
    {
        // Microsoft.Data.Sqlite keys pools by exact connection-string text. These
        // two keys mirror SqliteTaskStore's writer/reader builders (including order).
        // Keep them aligned if production store options change; fixture deletion
        // and pool-isolation regressions must still pass. Never clear other tests' pools.
        foreach (SqliteOpenMode mode in new[] { SqliteOpenMode.ReadWriteCreate, SqliteOpenMode.ReadOnly })
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = Path.GetFullPath(path),
                Mode = mode,
                Cache = SqliteCacheMode.Shared,
                Pooling = true,
            }.ToString());
            SqliteConnection.ClearPool(connection);
        }
    }
}
