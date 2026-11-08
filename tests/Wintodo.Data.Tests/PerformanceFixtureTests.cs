using Microsoft.Data.Sqlite;
using Wintodo.Data;

namespace Wintodo.Data.Tests;

public sealed class PerformanceFixtureTests
{
    [Fact]
    public async Task SeedRequestedPerformanceDatabase()
    {
        string? requestedPath =
            Environment.GetEnvironmentVariable("WINTODO_PERF_DB_PATH");
        bool externalRequest = !string.IsNullOrWhiteSpace(requestedPath);
        string databasePath = externalRequest
            ? Path.GetFullPath(requestedPath!)
            : Path.Combine(
                Path.GetTempPath(),
                $"wintodo-perf-{Guid.NewGuid():N}.db");
        int taskCount = ParseNonNegativeEnvironmentValue(
            "WINTODO_PERF_TASK_COUNT",
            defaultValue: 100);
        int listCount = ParseNonNegativeEnvironmentValue(
            "WINTODO_PERF_LIST_COUNT",
            defaultValue: 4);

        Directory.CreateDirectory(
            Path.GetDirectoryName(databasePath)
                ?? throw new InvalidOperationException("Fixture path needs a parent directory."));
        DeleteDatabaseFiles(databasePath);

        try
        {
            var clock = new FakeClock
            {
                UtcNow = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            };
            await using (var store = new SqliteTaskStore(databasePath, clock))
            {
                await store.InitializeAsync(TestContext.Current.CancellationToken);
            }

            string repositoryRoot = FindRepositoryRoot();
            string sql = await File.ReadAllTextAsync(
                Path.Combine(repositoryRoot, "tests", "fixtures", "perf", "seed.sql"),
                TestContext.Current.CancellationToken);
            await using (var connection = TestSqliteConnections.Create(databasePath))
            {
                await connection.OpenAsync(TestContext.Current.CancellationToken);
                await using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText = sql;
                    command.Parameters.AddWithValue("@taskCount", taskCount);
                    command.Parameters.AddWithValue("@listCount", listCount);
                    await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
                }

                await using SqliteCommand verify = connection.CreateCommand();
                verify.CommandText = "SELECT COUNT(*) FROM tasks;";
                Assert.Equal(taskCount, Convert.ToInt32(await verify.ExecuteScalarAsync(TestContext.Current.CancellationToken)));
                verify.CommandText =
                    "SELECT COUNT(*) FROM lists WHERE uid LIKE 'perf-list-%';";
                Assert.Equal(listCount, Convert.ToInt32(await verify.ExecuteScalarAsync(TestContext.Current.CancellationToken)));
            }
        }
        finally
        {
            if (!externalRequest)
            {
                TestSqliteConnections.ClearStorePools(databasePath);
                DeleteDatabaseFiles(databasePath);
            }
        }
    }

    private static int ParseNonNegativeEnvironmentValue(
        string name,
        int defaultValue)
    {
        string? text = Environment.GetEnvironmentVariable(name);
        return int.TryParse(
            text,
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out int value)
            && value >= 0
                ? value
                : defaultValue;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Wintodo.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Wintodo.sln.");
    }

    private static void DeleteDatabaseFiles(string databasePath)
    {
        File.Delete(databasePath);
        File.Delete(databasePath + "-wal");
        File.Delete(databasePath + "-shm");
    }
}
