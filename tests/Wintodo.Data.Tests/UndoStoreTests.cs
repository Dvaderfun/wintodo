using Microsoft.Data.Sqlite;
using Wintodo.Core.Models;
using Wintodo.Core.Queries;
using Wintodo.Core.Services;

namespace Wintodo.Data.Tests;

public sealed class UndoStoreTests
{
    [Fact]
    public async Task VersionTwoDatabase_UpgradesWithoutChangingTasksAndCreatesBackup()
    {
        await using StoreFixture f = await StoreFixture.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        TodoList list = await f.Store.CreateListAsync("Version two", cancellationToken: ct);
        TodoTask task = await f.Store.CreateTaskAsync(new TodoTask { ListId = list.Id, Summary = "Preserved" }, ct);
        await f.Store.DisposeAsync();
        await using (var connection = new SqliteConnection($"Data Source={f.DatabasePath}"))
        {
            await connection.OpenAsync(ct);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "DROP TABLE import_links; DROP TABLE import_sources; DROP TABLE list_deleted_tasks; DELETE FROM schema_version WHERE version >= 3;";
            await command.ExecuteNonQueryAsync(ct);
        }
        await using var reopened = new SqliteTaskStore(f.DatabasePath, f.Clock);
        await reopened.InitializeAsync(ct);
        Assert.Equal(4, (await reopened.GetStatsAsync(ct)).SchemaVersion);
        Assert.Equal(task.Summary, (await reopened.GetTaskAsync(task.Uid, ct))!.Summary);
        Assert.True(File.Exists(f.DatabasePath + ".bak-3"));
        await reopened.InitializeAsync(ct);
        Assert.Single(await reopened.GetTasksAsync(ListQuery.ForList(list.Id), ct));
    }

    [Fact]
    public async Task ListRestore_RestoresOnlyItsOwnDeletionsAndIsIdempotent()
    {
        await using StoreFixture f = await StoreFixture.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        TodoList list = await f.Store.CreateListAsync("Restore", ListColor.Red, cancellationToken: ct);
        TodoTask task = await f.Store.CreateTaskAsync(new TodoTask { ListId = list.Id, Summary = "Keep", IcalUnknown = "X-TEST:value" }, ct);
        TodoTask step = await f.Store.AddStepAsync(task.Uid, "Child", ct);
        TodoTask old = await f.Store.CreateTaskAsync(new TodoTask { ListId = list.Id, Summary = "Earlier deletion" }, ct);
        await f.Store.DeleteTaskAsync(old.Uid, ct);
        await f.Store.DeleteListAsync(list.Id, ct);
        long before = (await f.Store.GetStatsAsync(ct)).PendingChangeCount;
        var events = new List<StoreChangedEventArgs>();
        f.Store.StoreChanged += (_, args) => events.Add(args);
        await f.Store.RestoreListAsync(list.Id, ct);
        Assert.Equal(list.Id, Assert.Single(await f.Store.GetListsAsync(ct)).Id);
        Assert.Equal("X-TEST:value", (await f.Store.GetTaskAsync(task.Uid, ct))!.IcalUnknown);
        Assert.Equal(step.Uid, Assert.Single(await f.Store.GetChildrenAsync(task.Uid, ct)).Uid);
        Assert.Null(await f.Store.GetTaskAsync(old.Uid, ct));
        Assert.Equal(before + 3, (await f.Store.GetStatsAsync(ct)).PendingChangeCount);
        Assert.Equal(2, Assert.Single(events).TaskUids.Count);
        await f.Store.RestoreListAsync(list.Id, ct);
        Assert.Single(events);
    }

    [Fact]
    public async Task FieldRestore_PreservesNewerTextAndRemoteVersionAndRollsBackInvalidBatch()
    {
        await using StoreFixture f = await StoreFixture.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        TodoList list = await f.Store.CreateListAsync("Fields", cancellationToken: ct);
        TodoTask task = await f.Store.CreateTaskAsync(new TodoTask { ListId = list.Id, Summary = "Before", Due = f.Clock.UtcNow }, ct);
        var state = await f.Store.CaptureTaskUndoAsync([task.Uid], TaskUndoFields.Due, ct);
        task.Due = null;
        task.Summary = "Newer title";
        await f.Store.UpdateTaskAsync(task, ct);
        await f.Store.SetTaskRemoteVersionAsync(task.Uid, "https://dav.example/task.ics", "new-etag", ct);
        await f.Store.RestoreTaskFieldsAsync(state, ct);
        TodoTask restored = (await f.Store.GetTaskAsync(task.Uid, ct))!;
        Assert.Equal("Newer title", restored.Summary);
        Assert.Equal("new-etag", restored.RemoteETag);
        Assert.Equal(f.Clock.UtcNow, restored.Due);
        restored.Due = null;
        await f.Store.UpdateTaskAsync(restored, ct);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => f.Store.RestoreTaskFieldsAsync(
            [state[0], new TaskUndoState(new TodoTask { Uid = "missing" }, TaskUndoFields.Due, 0)], ct));
        Assert.Null((await f.Store.GetTaskAsync(task.Uid, ct))!.Due);
    }

    [Fact]
    public async Task RecurringCompletionUndo_RestoresOccurrenceAlarmAndCount()
    {
        await using StoreFixture f = await StoreFixture.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        TodoList list = await f.Store.CreateListAsync("Recurring", cancellationToken: ct);
        var task = new TodoTask { ListId = list.Id, Summary = "Daily", Due = f.Clock.UtcNow, Start = f.Clock.UtcNow.AddHours(-1), RRule = "FREQ=DAILY;COUNT=2" };
        task.Alarms.Add(new TaskAlarm { Trigger = f.Clock.UtcNow.AddMinutes(-10) });
        task = await f.Store.CreateTaskAsync(task, ct);
        var states = await f.Store.CaptureTaskUndoAsync([task.Uid], TaskUndoFields.Completion, ct);
        await f.Store.CompleteTaskAsync(task.Uid, ct);
        TodoTask advanced = (await f.Store.GetTaskAsync(task.Uid, ct))!;
        Assert.Equal(task.Due!.Value.AddDays(1), advanced.Due);
        Assert.Equal(task.Start!.Value.AddDays(1), advanced.Start);
        Assert.Equal(task.Alarms[0].Trigger!.Value.AddDays(1), advanced.Alarms[0].Trigger);
        Assert.Equal(1, Assert.Single(await f.Store.CaptureTaskUndoAsync([task.Uid], TaskUndoFields.Completion, ct)).CompletionCount);
        advanced.Summary = "Newer recurring title";
        advanced.Description = "Newer recurring note";
        await f.Store.UpdateTaskAsync(advanced, ct);
        await f.Store.RestoreTaskFieldsAsync(states, ct);
        TodoTask restored = (await f.Store.GetTaskAsync(task.Uid, ct))!;
        Assert.Equal(task.Due, restored.Due);
        Assert.Equal(task.Start, restored.Start);
        Assert.Equal("Newer recurring title", restored.Summary);
        Assert.Equal("Newer recurring note", restored.Description);
        Assert.Equal(task.Alarms[0].Trigger, restored.Alarms[0].Trigger);
        Assert.Equal(0, Assert.Single(await f.Store.CaptureTaskUndoAsync([task.Uid], TaskUndoFields.Completion, ct)).CompletionCount);
        await f.Store.CompleteTaskAsync(task.Uid, ct);
        Assert.Equal(Wintodo.Core.Models.TaskStatus.NeedsAction, (await f.Store.GetTaskAsync(task.Uid, ct))!.Status);
    }

    [Fact]
    public async Task RemoteStoreEvents_AreDistinguishedFromLocalEditsAndPushAcknowledgements()
    {
        await using StoreFixture f = await StoreFixture.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        TodoList list = await f.Store.CreateListAsync("Remote", cancellationToken: ct);
        var events = new List<StoreChangedEventArgs>();
        f.Store.StoreChanged += (_, args) => events.Add(args);
        await f.Store.ApplyRemoteChangesAsync(list.Id,
            [new RemoteTaskUpdate { Task = new TodoTask { Uid = "remote", Summary = "Pull" }, Href = "https://dav.example/remote.ics", ETag = "v1" }], [], ct);
        Assert.True(Assert.Single(events).IsRemoteChange);
        events.Clear();
        await f.Store.DeleteTaskAsync("remote", ct);
        Assert.False(Assert.Single(events).IsRemoteChange);
    }

    [Fact]
    public async Task NonRecurringCompletionUndo_RetainsLaterDueAndReminderChanges()
    {
        await using StoreFixture f = await StoreFixture.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        TodoList list = await f.Store.CreateListAsync("Completion", cancellationToken: ct);
        TodoTask task = await f.Store.CreateTaskAsync(new TodoTask { ListId = list.Id, Summary = "Keep later fields" }, ct);
        var states = await f.Store.CaptureTaskUndoAsync([task.Uid], TaskUndoFields.Completion, ct);
        await f.Store.CompleteTaskAsync(task.Uid, ct);
        task = (await f.Store.GetTaskAsync(task.Uid, ct))!;
        task.Due = f.Clock.UtcNow.AddDays(3);
        task.Alarms.Add(new TaskAlarm { Trigger = f.Clock.UtcNow.AddDays(2) });
        await f.Store.UpdateTaskAsync(task, ct);
        await f.Store.RestoreTaskFieldsAsync(states, ct);
        TodoTask restored = (await f.Store.GetTaskAsync(task.Uid, ct))!;
        Assert.Equal(task.Due, restored.Due);
        Assert.Equal(task.Alarms[0].Trigger, Assert.Single(restored.Alarms).Trigger);
        Assert.Equal(Wintodo.Core.Models.TaskStatus.NeedsAction, restored.Status);
    }
}
