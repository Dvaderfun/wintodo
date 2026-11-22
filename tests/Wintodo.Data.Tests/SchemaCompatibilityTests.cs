using Microsoft.Data.Sqlite;
using Wintodo.Core.Models;
using Wintodo.Data;

namespace Wintodo.Data.Tests;

public sealed class SchemaCompatibilityTests
{
    private static readonly string[] MigrationResources =
    [
        "Wintodo.Data.Migrations.001_init.sql",
        "Wintodo.Data.Migrations.002_list_icon_emoji.sql",
        "Wintodo.Data.Migrations.003_list_delete_membership.sql",
        "Wintodo.Data.Migrations.004_import_links.sql",
        "Wintodo.Data.Migrations.005_task_move_outbox.sql",
        "Wintodo.Data.Migrations.006_my_day_exclusion.sql",
    ];

    [Theory]
    [InlineData(7L)]
    [InlineData(2147483648L)]
    [InlineData(long.MaxValue)]
    public async Task NewerSchema_RejectsBeforeChangingDatabase(long version)
    {
        string directory = CreateDirectory();
        string path = Path.Combine(directory, "newer.db");
        try
        {
            await SeedDatabaseAsync(path, 4);
            await AddNewerSchemaAsync(path, version);
            byte[] before = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);

            await using (var store = new SqliteTaskStore(path, new FakeClock()))
            {
                int notifications = 0;
                store.StoreChanged += (_, _) => notifications++;
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    UnsupportedDatabaseSchemaException exception =
                        await Assert.ThrowsAsync<UnsupportedDatabaseSchemaException>(
                            () => store.InitializeAsync(TestContext.Current.CancellationToken));
                    Assert.Equal(version, exception.DatabaseVersion);
                    Assert.Equal(6, exception.SupportedVersion);
                }

                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => store.SetSettingAsync("must-not-write", "blocked", TestContext.Current.CancellationToken));
                Assert.Equal(0, notifications);
            }

            Assert.Equal(before, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.bak-*"));
            await using SqliteConnection connection = await OpenConnectionAsync(path, SqliteOpenMode.ReadOnly);
            Assert.Equal("delete", await ScalarStringAsync(connection, "PRAGMA journal_mode;"));
            Assert.Equal(version, await SchemaVersionAsync(connection));
            Assert.Equal("future-marker", await ScalarStringAsync(connection, "SELECT value FROM future_data;"));
            Assert.Equal("future-only", await ScalarStringAsync(connection, "SELECT value FROM future_column_marker;"));
            await AssertSeedRowsAsync(connection);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task FailedInitialization_CanRetryAfterSupportedBackupIsRestored()
    {
        string directory = CreateDirectory();
        string path = Path.Combine(directory, "retry.db");
        try
        {
            await SeedDatabaseAsync(path, 4);
            string supportedCopy = Path.Combine(directory, "supported.db");
            File.Copy(path, supportedCopy);
            await AddNewerSchemaAsync(path, 7);

            await using var store = new SqliteTaskStore(path, new FakeClock());
            await Assert.ThrowsAsync<UnsupportedDatabaseSchemaException>(
                () => store.InitializeAsync(TestContext.Current.CancellationToken));
            File.Copy(supportedCopy, path, overwrite: true);
            await store.InitializeAsync(TestContext.Current.CancellationToken);
            await store.SetSettingAsync("recovered", "yes", TestContext.Current.CancellationToken);
            Assert.Equal("yes", await store.GetSettingAsync("recovered", TestContext.Current.CancellationToken));
            Assert.Equal("legacy-task", (await store.GetTaskAsync("legacy-task", TestContext.Current.CancellationToken))?.Uid);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task SupportedSchema_UpgradesPreserveRowsAndBackupsCanBeRestored(int version)
    {
        string directory = CreateDirectory();
        string path = Path.Combine(directory, "supported.db");
        try
        {
            await SeedDatabaseAsync(path, version);
            await AssertStoreRowsAsync(path);

            var backupBytes = new Dictionary<string, byte[]>();
            for (int nextVersion = version + 1; nextVersion <= 6; nextVersion++)
            {
                string backupPath = path + ".bak-" + nextVersion;
                backupBytes.Add(backupPath, await File.ReadAllBytesAsync(backupPath, TestContext.Current.CancellationToken));
                await using SqliteConnection backup = await OpenConnectionAsync(backupPath, SqliteOpenMode.ReadOnly);
                Assert.Equal(nextVersion - 1, await SchemaVersionAsync(backup));
                await AssertSeedRowsAsync(backup);
            }

            await AssertStoreRowsAsync(path);
            foreach ((string backupPath, byte[] bytes) in backupBytes)
            {
                Assert.Equal(bytes, await File.ReadAllBytesAsync(backupPath, TestContext.Current.CancellationToken));
            }

            if (version < 6)
            {
                string restoredPath = Path.Combine(directory, "restored.db");
                File.Copy(path + ".bak-" + (version + 1), restoredPath);
                await using (SqliteConnection restored = await OpenConnectionAsync(restoredPath, SqliteOpenMode.ReadOnly))
                {
                    Assert.Equal(version, await SchemaVersionAsync(restored));
                    await AssertSeedRowsAsync(restored);
                }

                await AssertStoreRowsAsync(restoredPath);
            }
            else
            {
                Assert.Empty(Directory.EnumerateFiles(directory, "*.bak-*"));
            }
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task FreshDatabase_InitializesAndReopensAtSupportedVersion()
    {
        string directory = CreateDirectory();
        string path = Path.Combine(directory, "fresh.db");
        try
        {
            await using (var store = new SqliteTaskStore(path, new FakeClock()))
            {
                await store.InitializeAsync(TestContext.Current.CancellationToken);
                await store.InitializeAsync(TestContext.Current.CancellationToken);
                Assert.Equal(6, (await store.GetStatsAsync(TestContext.Current.CancellationToken)).SchemaVersion);
                await store.SetSettingAsync("kept", "value", TestContext.Current.CancellationToken);
            }

            await using var reopened = new SqliteTaskStore(path, new FakeClock());
            await reopened.InitializeAsync(TestContext.Current.CancellationToken);
            Assert.Equal("value", await reopened.GetSettingAsync("kept", TestContext.Current.CancellationToken));
            Assert.Equal(6, (await reopened.GetStatsAsync(TestContext.Current.CancellationToken)).SchemaVersion);
            Assert.Empty(Directory.EnumerateFiles(directory, "*.bak-*"));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    private static async Task AssertStoreRowsAsync(string path)
    {
        await using var store = new SqliteTaskStore(path, new FakeClock());
        await store.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.Equal(6, (await store.GetStatsAsync(TestContext.Current.CancellationToken)).SchemaVersion);
        Assert.Equal("Legacy", Assert.Single(await store.GetListsAsync(TestContext.Current.CancellationToken)).DisplayName);
        TodoTask task = Assert.IsType<TodoTask>(await store.GetTaskAsync("legacy-task", TestContext.Current.CancellationToken));
        Assert.Equal("Preserved task", task.Summary);
        Assert.Equal("Preserved note", task.Description);
        Assert.Equal("unknown-property-marker", task.IcalUnknown);
        Assert.Equal("legacy-tag", Assert.Single(task.Categories));
        Assert.Equal("legacy-alarm", Assert.Single(task.Alarms).Uid);
        Assert.Equal("legacy.txt", Assert.Single(task.Attachments).FileName);
        Assert.Equal("kept", await store.GetSettingAsync("legacy-setting", TestContext.Current.CancellationToken));
    }

    private static async Task SeedDatabaseAsync(string path, int version)
    {
        // Initialize the native SQLite provider before constructing legacy databases directly.
        await using var provider = new SqliteTaskStore(path, new FakeClock());
        await using SqliteConnection connection = await OpenConnectionAsync(path, SqliteOpenMode.ReadWriteCreate);
        foreach (string resource in MigrationResources.Take(version))
        {
            await using Stream stream = typeof(SqliteTaskStore).Assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException("Migration fixture is missing.");
            using var reader = new StreamReader(stream);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await using SqliteCommand seed = connection.CreateCommand();
        seed.CommandText = """
            INSERT INTO lists(id, uid, display_name, sort_order, created_utc, modified_utc)
            VALUES(1, 'legacy-list', 'Legacy', 1024,
                '2026-01-05T12:00:00.0000000Z', '2026-01-05T12:00:00.0000000Z');
            INSERT INTO tasks(id, uid, list_id, summary, description, sort_order, created_utc, modified_utc, ical_unknown)
            VALUES(1, 'legacy-task', 1, 'Preserved task', 'Preserved note', 1024,
                '2026-01-05T12:00:00.0000000Z', '2026-01-05T12:00:00.0000000Z', 'unknown-property-marker');
            INSERT INTO settings(key, value) VALUES('legacy-setting', 'kept');
            INSERT INTO tags(id, name) VALUES(1, 'legacy-tag');
            INSERT INTO task_tags(task_id, tag_id) VALUES(1, 1);
            INSERT INTO alarms(task_id, uid) VALUES(1, 'legacy-alarm');
            INSERT INTO attachments(task_id, file_name, relative_path, size_bytes, added_utc)
            VALUES(1, 'legacy.txt', 'attachments/legacy-task/legacy.txt', 4, '2026-01-05T12:00:00.0000000Z');
            """;
        await seed.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task AddNewerSchemaAsync(string path, long version)
    {
        await using SqliteConnection connection = await OpenConnectionAsync(path, SqliteOpenMode.ReadWrite);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO schema_version(version, applied_utc) VALUES(@version, '2026-01-05T12:00:00.0000000Z');
            CREATE TABLE future_data(value TEXT NOT NULL);
            INSERT INTO future_data(value) VALUES('future-marker');
            CREATE VIEW future_column_marker AS SELECT 'future-only' AS value;
            """;
        command.Parameters.AddWithValue("@version", version);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task AssertSeedRowsAsync(SqliteConnection connection)
    {
        Assert.Equal("Preserved task", await ScalarStringAsync(connection, "SELECT summary FROM tasks WHERE uid = 'legacy-task';"));
        Assert.Equal("unknown-property-marker", await ScalarStringAsync(connection, "SELECT ical_unknown FROM tasks WHERE uid = 'legacy-task';"));
        Assert.Equal("kept", await ScalarStringAsync(connection, "SELECT value FROM settings WHERE key = 'legacy-setting';"));
        Assert.Equal("legacy-alarm", await ScalarStringAsync(connection, "SELECT uid FROM alarms;"));
        Assert.Equal("legacy.txt", await ScalarStringAsync(connection, "SELECT file_name FROM attachments;"));
    }

    private static async Task<SqliteConnection> OpenConnectionAsync(string path, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
            Pooling = false,
        }.ToString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        return connection;
    }

    private static async Task<long> SchemaVersionAsync(SqliteConnection connection)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT MAX(version) FROM schema_version;";
        return Convert.ToInt64(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<string> ScalarStringAsync(SqliteConnection connection, string sql)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return (string)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("Query returned no value."));
    }

    private static string CreateDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "Wintodo.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteDirectory(string directory)
    {
        foreach (string path in Directory.EnumerateFiles(directory, "*.db"))
        {
            TestSqliteConnections.ClearStorePools(path);
        }
        Directory.Delete(directory, recursive: true);
    }
}
