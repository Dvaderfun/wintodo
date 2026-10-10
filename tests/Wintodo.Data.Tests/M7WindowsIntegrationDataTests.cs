using Microsoft.Data.Sqlite;
using Wintodo.Core.Models;
using Wintodo.Core.Services;
using Wintodo.Data;

namespace Wintodo.Data.Tests;

public sealed class M7WindowsIntegrationDataTests
{
    [Fact]
    public async Task PendingReminder_ProvidesTaskContextAndTracksSnoozeWithoutChangeNoise()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        TodoList list = await fixture.Store.CreateListAsync("Inbox", cancellationToken: TestContext.Current.CancellationToken);
        TodoTask task = await fixture.Store.CreateTaskAsync(new TodoTask
        {
            ListId = list.Id,
            Summary = "M7 reminder",
        }, TestContext.Current.CancellationToken);
        TaskAlarm alarm = await fixture.Store.SetAlarmAsync(task.Uid, new TaskAlarm
        {
            Trigger = fixture.Clock.UtcNow.AddMinutes(2),
            Description = "M7 reminder",
        }, TestContext.Current.CancellationToken);
        int changes = 0;
        fixture.Store.StoreChanged += (_, _) => changes++;

        PendingReminder pending = Assert.Single(
            await fixture.Store.GetPendingRemindersAsync(
                fixture.Clock.UtcNow.AddMinutes(3),
                TestContext.Current.CancellationToken));
        Assert.Equal(task.Uid, pending.TaskUid);
        Assert.Equal(task.Summary, pending.TaskSummary);
        Assert.Equal(alarm.Uid, pending.Alarm.Uid);
        Assert.Equal(alarm.Trigger, pending.EffectiveTrigger);

        await fixture.Store.SetAlarmScheduledToastTagAsync(alarm.Uid, task.Uid, TestContext.Current.CancellationToken);
        Assert.Equal(0, changes);
        Assert.Equal(
            task.Uid,
            Assert.Single(
                Assert.IsType<TodoTask>(
                    await fixture.Store.GetTaskAsync(task.Uid, TestContext.Current.CancellationToken)).Alarms)
                .ScheduledToastTag);

        DateTimeOffset snoozedUntil = fixture.Clock.UtcNow.AddHours(1);
        await fixture.Store.SnoozeAlarmAsync(alarm.Uid, snoozedUntil, TestContext.Current.CancellationToken);
        Assert.Empty(await fixture.Store.GetPendingRemindersAsync(
            snoozedUntil.AddTicks(-1),
            TestContext.Current.CancellationToken));
        Assert.Equal(
            snoozedUntil,
            Assert.Single(await fixture.Store.GetPendingRemindersAsync(snoozedUntil, TestContext.Current.CancellationToken))
                .EffectiveTrigger);
    }

    [Fact]
    public async Task CompletedTask_IsRemovedFromPendingRemindersAndBadgeCount()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        TodoList list = await fixture.Store.CreateListAsync("Inbox", cancellationToken: TestContext.Current.CancellationToken);
        await fixture.Store.CreateTaskAsync(new TodoTask
        {
            ListId = list.Id,
            Summary = "Overdue",
            Due = fixture.Clock.UtcNow.AddDays(-1),
        }, TestContext.Current.CancellationToken);
        TodoTask today = await fixture.Store.CreateTaskAsync(new TodoTask
        {
            ListId = list.Id,
            Summary = "Today",
            Due = fixture.Clock.UtcNow.AddHours(1),
        }, TestContext.Current.CancellationToken);
        await fixture.Store.CreateTaskAsync(new TodoTask
        {
            ListId = list.Id,
            Summary = "Tomorrow",
            Due = fixture.Clock.UtcNow.AddDays(1),
        }, TestContext.Current.CancellationToken);
        TaskAlarm alarm = await fixture.Store.SetAlarmAsync(today.Uid, new TaskAlarm
        {
            Trigger = fixture.Clock.UtcNow.AddMinutes(2),
        }, TestContext.Current.CancellationToken);
        await fixture.Store.CompleteTaskAsync(today.Uid, TestContext.Current.CancellationToken);

        DateTimeOffset tomorrowStart = new(
            fixture.Clock.UtcNow.UtcDateTime.Date.AddDays(1),
            TimeSpan.Zero);
        Assert.Equal(
            1,
            await fixture.Store.GetActiveDueTaskCountAsync(tomorrowStart, TestContext.Current.CancellationToken));
        Assert.Empty(await fixture.Store.GetPendingRemindersAsync(
            fixture.Clock.UtcNow.AddHours(1),
            TestContext.Current.CancellationToken));

        await fixture.Store.UncompleteTaskAsync(today.Uid, TestContext.Current.CancellationToken);
        Assert.Equal(
            2,
            await fixture.Store.GetActiveDueTaskCountAsync(tomorrowStart, TestContext.Current.CancellationToken));
        Assert.Equal(
            alarm.Uid,
            Assert.Single(await fixture.Store.GetPendingRemindersAsync(
                fixture.Clock.UtcNow.AddHours(1),
                TestContext.Current.CancellationToken)).Alarm.Uid);
    }
}

public sealed class M7GateDatabaseTests
{
    [Fact]
    public async Task SeedRequestedGateDatabase()
    {
        string? databasePath =
            Environment.GetEnvironmentVariable("WINTODO_M7_GATE_DB_PATH");
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            return;
        }

        IClock clock = new SystemClock();
        await using var store = new SqliteTaskStore(databasePath, clock);
        await store.InitializeAsync(TestContext.Current.CancellationToken);
        TodoList list = await store.CreateListAsync("M7 Gate", ListColor.Teal, cancellationToken: TestContext.Current.CancellationToken);
        TodoTask reminderTask = await store.CreateTaskAsync(new TodoTask
        {
            ListId = list.Id,
            Summary = "M7 toast complete",
            Due = StartOfLocalDay(clock.Today, clock.LocalTimeZone),
            DueIsDateOnly = true,
        }, TestContext.Current.CancellationToken);
        await store.SetAlarmAsync(reminderTask.Uid, new TaskAlarm
        {
            Action = AlarmAction.Display,
            Trigger = clock.UtcNow.AddMinutes(2),
            Description = reminderTask.Summary,
        }, TestContext.Current.CancellationToken);
        await store.CreateTaskAsync(new TodoTask
        {
            ListId = list.Id,
            Summary = "M7 overdue badge",
            Due = StartOfLocalDay(clock.Today.AddDays(-1), clock.LocalTimeZone),
            DueIsDateOnly = true,
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RequestedGateDatabaseHasScheduleTagAndBadgeCount()
    {
        string? databasePath =
            Environment.GetEnvironmentVariable("WINTODO_M7_GATE_DB_PATH");
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            return;
        }

        await using var connection = new SqliteConnection(
            $"Data Source={Path.GetFullPath(databasePath)};Mode=ReadOnly");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using (SqliteCommand reminder = connection.CreateCommand())
        {
            reminder.CommandText = """
                SELECT a.scheduled_toast_tag, t.uid
                FROM alarms a
                JOIN tasks t ON t.id = a.task_id
                WHERE t.summary = 'M7 toast complete';
                """;
            await using SqliteDataReader reader = await reminder.ExecuteReaderAsync(TestContext.Current.CancellationToken);
            Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
            Assert.False(reader.IsDBNull(0));
            Assert.Equal(reader.GetString(1), reader.GetString(0));
        }

        await using SqliteCommand badge = connection.CreateCommand();
        badge.CommandText = """
            SELECT COUNT(*)
            FROM tasks t
            JOIN lists l ON l.id = t.list_id
            WHERE t.parent_uid IS NULL
              AND t.is_deleted = 0
              AND t.status IN (0, 1)
              AND t.due_utc IS NOT NULL
              AND l.is_deleted = 0;
            """;
        Assert.Equal(
            2L,
            Convert.ToInt64(
                await badge.ExecuteScalarAsync(TestContext.Current.CancellationToken),
                System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task RequestedGateToastCompletionPersisted()
    {
        string? databasePath =
            Environment.GetEnvironmentVariable("WINTODO_M7_GATE_DB_PATH");
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            return;
        }

        await using var connection = new SqliteConnection(
            $"Data Source={Path.GetFullPath(databasePath)};Mode=ReadOnly");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT status
            FROM tasks
            WHERE summary = 'M7 toast complete' AND is_deleted = 0;
            """;
        Assert.Equal(
            (int)Wintodo.Core.Models.TaskStatus.Completed,
            Convert.ToInt32(
                await command.ExecuteScalarAsync(TestContext.Current.CancellationToken),
                System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task RequestedGateToastSnoozePersisted()
    {
        string? databasePath =
            Environment.GetEnvironmentVariable("WINTODO_M7_GATE_DB_PATH");
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            return;
        }

        await using var connection = new SqliteConnection(
            $"Data Source={Path.GetFullPath(databasePath)};Mode=ReadOnly");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT a.snoozed_until_utc, a.scheduled_toast_tag, t.uid
            FROM alarms a
            JOIN tasks t ON t.id = a.task_id
            WHERE t.summary = 'M7 toast complete' AND t.is_deleted = 0;
            """;
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.False(reader.IsDBNull(0));
        Assert.False(reader.IsDBNull(1));
        DateTimeOffset snoozedUntil = DateTimeOffset.Parse(
            reader.GetString(0),
            System.Globalization.CultureInfo.InvariantCulture);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Assert.InRange(
            snoozedUntil,
            now.AddMinutes(8),
            now.AddMinutes(11));
        Assert.Equal(reader.GetString(2), reader.GetString(1));
    }

    private static DateTimeOffset StartOfLocalDay(
        DateOnly date,
        TimeZoneInfo timeZone)
    {
        DateTime local = DateTime.SpecifyKind(
            date.ToDateTime(TimeOnly.MinValue),
            DateTimeKind.Unspecified);
        return new DateTimeOffset(local, timeZone.GetUtcOffset(local));
    }
}
