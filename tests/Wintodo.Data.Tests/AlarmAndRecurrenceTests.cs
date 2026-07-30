using Microsoft.Data.Sqlite;
using Wintodo.Core.Models;

namespace Wintodo.Data.Tests;

public sealed class AlarmAndRecurrenceTests
{
    [Fact]
    public async Task AlarmLifecycle_HandlesAbsoluteRelativeSnoozeAndAcknowledge()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        TodoList list = await fixture.Store.CreateListAsync("Inbox");
        TodoTask task = await fixture.Store.CreateTaskAsync(new TodoTask
        {
            ListId = list.Id,
            Summary = "Meeting",
            Start = fixture.Clock.UtcNow.AddHours(4),
            Due = fixture.Clock.UtcNow.AddHours(5),
        });
        TaskAlarm absolute = await fixture.Store.SetAlarmAsync(task.Uid, new TaskAlarm
        {
            Trigger = fixture.Clock.UtcNow.AddHours(1),
            Description = "Absolute",
        });
        TaskAlarm relative = await fixture.Store.SetAlarmAsync(task.Uid, new TaskAlarm
        {
            TriggerOffsetSeconds = -3600,
            TriggerRelated = AlarmRelation.End,
            Description = "Relative",
        });

        Assert.Equal(
            absolute.Uid,
            Assert.Single(await fixture.Store.GetPendingAlarmsAsync(
                fixture.Clock.UtcNow.AddHours(2))).Uid);
        Assert.Equal(
            2,
            (await fixture.Store.GetPendingAlarmsAsync(
                fixture.Clock.UtcNow.AddHours(4))).Count);

        await fixture.Store.SnoozeAlarmAsync(absolute.Uid, fixture.Clock.UtcNow.AddHours(6));
        Assert.Equal(
            relative.Uid,
            Assert.Single(await fixture.Store.GetPendingAlarmsAsync(
                fixture.Clock.UtcNow.AddHours(4))).Uid);

        await fixture.Store.AcknowledgeAlarmAsync(relative.Uid);
        Assert.Empty(await fixture.Store.GetPendingAlarmsAsync(fixture.Clock.UtcNow.AddHours(4)));

        TodoTask stored = Assert.IsType<TodoTask>(await fixture.Store.GetTaskAsync(task.Uid));
        Assert.Equal(2, stored.Alarms.Count);
        Assert.Contains(stored.Alarms, alarm => alarm.IsAcknowledged);
        Assert.Contains(stored.Alarms, alarm => alarm.SnoozedUntil is not null);
    }

    [Fact]
    public async Task SetAlarm_WithSameUid_UpdatesExistingRow()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        TodoList list = await fixture.Store.CreateListAsync("Inbox");
        TodoTask task = await fixture.Store.CreateTaskAsync(new TodoTask
        {
            ListId = list.Id,
            Summary = "Update alarm",
        });
        TaskAlarm alarm = await fixture.Store.SetAlarmAsync(task.Uid, new TaskAlarm
        {
            Trigger = fixture.Clock.UtcNow.AddHours(1),
        });
        alarm.Trigger = fixture.Clock.UtcNow.AddHours(2);

        TaskAlarm updated = await fixture.Store.SetAlarmAsync(task.Uid, alarm);

        Assert.Equal(alarm.Id, updated.Id);
        Assert.Equal(
            alarm.Trigger,
            Assert.Single(
                Assert.IsType<TodoTask>(await fixture.Store.GetTaskAsync(task.Uid)).Alarms).Trigger);
    }

    [Fact]
    public async Task CompleteRecurringTask_AdvancesOccurrenceThenExhaustsCount()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        TodoList list = await fixture.Store.CreateListAsync("Inbox");
        DateTimeOffset firstDue = fixture.Clock.UtcNow.AddDays(1);
        TodoTask task = await fixture.Store.CreateTaskAsync(new TodoTask
        {
            ListId = list.Id,
            Summary = "Daily medicine",
            Start = firstDue.AddHours(-1),
            Due = firstDue,
            RRule = "FREQ=DAILY;COUNT=2",
        });
        TaskAlarm alarm = await fixture.Store.SetAlarmAsync(task.Uid, new TaskAlarm
        {
            Trigger = firstDue.AddMinutes(-15),
        });

        await fixture.Store.CompleteTaskAsync(task.Uid);
        TodoTask advanced = Assert.IsType<TodoTask>(await fixture.Store.GetTaskAsync(task.Uid));
        Assert.Equal(firstDue.AddDays(1), advanced.Due);
        Assert.Equal(task.Start!.Value.AddDays(1), advanced.Start);
        Assert.Equal(Wintodo.Core.Models.TaskStatus.NeedsAction, advanced.Status);
        Assert.Null(advanced.CompletedAt);
        Assert.Equal(alarm.Trigger!.Value.AddDays(1), Assert.Single(advanced.Alarms).Trigger);

        fixture.Clock.UtcNow = fixture.Clock.UtcNow.AddDays(1);
        await fixture.Store.CompleteTaskAsync(task.Uid);
        TodoTask exhausted = Assert.IsType<TodoTask>(await fixture.Store.GetTaskAsync(task.Uid));
        Assert.Equal(Wintodo.Core.Models.TaskStatus.Completed, exhausted.Status);
        Assert.Equal(100, exhausted.PercentComplete);
        Assert.Equal(fixture.Clock.UtcNow, exhausted.CompletedAt);

        await using var connection = new SqliteConnection($"Data Source={fixture.DatabasePath}");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM completion_history WHERE task_uid = @uid;";
        command.Parameters.AddWithValue("@uid", task.Uid);
        Assert.Equal(
            2L,
            Convert.ToInt64(
                await command.ExecuteScalarAsync(),
                System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Alarm_RequiresExactlyOneTriggerKind()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        TodoList list = await fixture.Store.CreateListAsync("Inbox");
        TodoTask task = await fixture.Store.CreateTaskAsync(new TodoTask
        {
            ListId = list.Id,
            Summary = "Invalid alarm",
        });

        await Assert.ThrowsAsync<ArgumentException>(
            () => fixture.Store.SetAlarmAsync(task.Uid, new TaskAlarm()));
        await Assert.ThrowsAsync<ArgumentException>(
            () => fixture.Store.SetAlarmAsync(task.Uid, new TaskAlarm
            {
                Trigger = fixture.Clock.UtcNow,
                TriggerOffsetSeconds = -60,
            }));
    }

    [Fact]
    public async Task StatsAndVacuum_ReportLiveStoreState()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        TodoList list = await fixture.Store.CreateListAsync("Inbox");
        await fixture.Store.CreateTaskAsync(new TodoTask
        {
            ListId = list.Id,
            Summary = "Stored",
        });

        await fixture.Store.VacuumAsync();
        Wintodo.Core.Services.StoreStats stats = await fixture.Store.GetStatsAsync();

        Assert.Equal(1, stats.ListCount);
        Assert.Equal(1, stats.TaskCount);
        Assert.Equal(2, stats.PendingChangeCount);
        Assert.Equal(0, stats.ActiveReadConnections);
        Assert.True(stats.DatabaseSizeBytes > 0);
    }
}
