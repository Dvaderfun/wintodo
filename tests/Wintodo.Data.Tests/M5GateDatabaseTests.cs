using System.Globalization;
using Wintodo.Core.Models;
using Wintodo.Core.Services;
using Wintodo.Data;

namespace Wintodo.Data.Tests;

public sealed class M5GateDatabaseTests
{
    [Fact]
    public async Task SeedRequestedUiGateDatabase()
    {
        string? requestedPath =
            Environment.GetEnvironmentVariable("WINTODO_M5_GATE_DB_PATH");
        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            return;
        }

        string databasePath = Path.GetFullPath(requestedPath);
        Directory.CreateDirectory(
            Path.GetDirectoryName(databasePath)
                ?? throw new InvalidOperationException("Gate database needs a parent directory."));
        DeleteIfExists(databasePath);
        DeleteIfExists($"{databasePath}-wal");
        DeleteIfExists($"{databasePath}-shm");

        var clock = new GateClock();
        await using var store = new SqliteTaskStore(databasePath, clock);
        await store.InitializeAsync(TestContext.Current.CancellationToken);
        TodoList alpha = await store.CreateListAsync("Alpha", cancellationToken: TestContext.Current.CancellationToken);
        TodoList beta = await store.CreateListAsync("Beta", cancellationToken: TestContext.Current.CancellationToken);

        await store.CreateTaskAsync(new TodoTask
        {
            ListId = alpha.Id,
            Summary = "Midnight focus",
            MyDayDate = clock.Today,
            SortOrder = 1024,
        }, TestContext.Current.CancellationToken);
        await store.CreateTaskAsync(new TodoTask
        {
            ListId = alpha.Id,
            Summary = "Needle alpha",
            Description = "Search match in the first list",
            SortOrder = 2048,
        }, TestContext.Current.CancellationToken);
        await store.CreateTaskAsync(new TodoTask
        {
            ListId = alpha.Id,
            Summary = "Overdue alpha",
            Due = clock.UtcNow.AddDays(-1),
            SortOrder = 3072,
        }, TestContext.Current.CancellationToken);
        TodoTask completed = await store.CreateTaskAsync(new TodoTask
        {
            ListId = alpha.Id,
            Summary = "Completed alpha",
            SortOrder = 4096,
        }, TestContext.Current.CancellationToken);
        await store.CompleteTaskAsync(completed.Uid, TestContext.Current.CancellationToken);

        await store.CreateTaskAsync(new TodoTask
        {
            ListId = beta.Id,
            Summary = "Needle beta",
            Description = "Search match in the second list",
            SortOrder = 1024,
        }, TestContext.Current.CancellationToken);
        await store.CreateTaskAsync(new TodoTask
        {
            ListId = beta.Id,
            Summary = "Beta later",
            Due = clock.UtcNow.AddDays(14),
            SortOrder = 2048,
        }, TestContext.Current.CancellationToken);
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private sealed class GateClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = DateTimeOffset.Parse(
            "2026-07-31T09:00:00Z",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal);

        public TimeZoneInfo LocalTimeZone => TimeZoneInfo.Local;

        public DateOnly Today => DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(UtcNow, LocalTimeZone).DateTime);
    }
}
