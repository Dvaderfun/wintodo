using Wintodo.Core.Models;
using Wintodo.Core.Services;
using Wintodo.Data;

namespace Wintodo.Data.Tests;

public sealed class NotificationSettingsGateTests
{
    [Fact]
    public async Task SeedRequestedDatabase()
    {
        string? databasePath = Environment.GetEnvironmentVariable("WINTODO_NOTIFICATION_SETTINGS_GATE_DB_PATH");
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            return;
        }

        var clock = new SystemClock();
        await using var store = new SqliteTaskStore(databasePath, clock);
        var ct = TestContext.Current.CancellationToken;
        await store.InitializeAsync(ct);
        TodoList list = await store.EnsureDefaultListAsync("Tasks", ct);
        TodoTask task = await store.CreateTaskAsync(new TodoTask
        {
            ListId = list.Id,
            Summary = "Notification settings reminder",
        }, ct);
        await store.SetAlarmAsync(task.Uid, new TaskAlarm
        {
            Action = AlarmAction.Display,
            Trigger = clock.UtcNow.AddHours(1),
            Description = task.Summary,
        }, ct);
    }

    [Fact]
    public async Task StoreStats_ReportsResolvedDatabasePathAndSize()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        StoreStats stats = await fixture.Store.GetStatsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(Path.GetFullPath(fixture.DatabasePath), stats.DatabasePath);
        Assert.Equal(new FileInfo(fixture.DatabasePath).Length, stats.DatabaseSizeBytes);
        Assert.True(stats.DatabaseSizeBytes > 0);
    }
}
