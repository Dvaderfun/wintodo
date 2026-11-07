using Microsoft.Data.Sqlite;
using Wintodo.Core.Models;

namespace Wintodo.Sync.Tests;

public sealed partial class SyncPersistenceTests
{
    [Fact]
    public async Task Pull_ChecksPendingChangesEvenWhenTheDirtyBitIsClear()
    {
        var ct = TestContext.Current.CancellationToken;
        await using SyncFixture fixture = await SyncFixture.CreateAsync();
        (Account account, TodoList list) = await fixture.CreateMappedListAsync();
        TodoTask task = await SeedPullTaskAsync(fixture, list, ct);
        IReadOnlyList<RemoteTaskState> states = await fixture.Store.GetRemoteTaskStatesAsync(list.Id, ct);
        task.Summary = "Pending local value";
        await fixture.Store.UpdateTaskAsync(task, ct);
        await using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = fixture.DatabasePath }.ToString());
        await connection.OpenAsync(ct);
        using SqliteCommand clear = connection.CreateCommand();
        clear.CommandText = "UPDATE tasks SET is_dirty = 0, sequence = @sequence WHERE uid = @uid;";
        clear.Parameters.AddWithValue("@uid", task.Uid);
        clear.Parameters.AddWithValue("@sequence", states[0].Sequence);
        await clear.ExecuteNonQueryAsync(ct);

        RemotePullApplyResult result = await fixture.Store.ApplyRemotePullAsync(
            list.Id, [IncomingPullUpdate(task.Uid)], [], states, ct);

        Assert.Equal(0, result.AppliedCount);
        Assert.False(result.HasDeferredChanges);
        SyncConflict conflict = Assert.Single(result.Conflicts);
        Assert.Equal("Pending local value", conflict.LocalTask.Summary);
        Assert.False(conflict.LocalTask.IsDirty);
        Assert.Contains(await fixture.Store.GetPendingSyncChangesAsync(account.Id, ct),
            change => change.EntityUid == task.Uid);
        Assert.Equal("Pending local value", (await fixture.Store.GetTaskForSyncAsync(task.Uid, ct))!.Summary);
    }

    [Fact]
    public async Task Pull_ProtectsAnAcknowledgedOwnedCollectionRevision()
    {
        var ct = TestContext.Current.CancellationToken;
        await using SyncFixture fixture = await SyncFixture.CreateAsync();
        (Account account, TodoList list) = await fixture.CreateMappedListAsync();
        TodoTask task = await SeedPullTaskAsync(fixture, list, ct);
        IReadOnlyList<RemoteTaskState> states = await fixture.Store.GetRemoteTaskStatesAsync(list.Id, ct);
        await fixture.Store.SetAlarmAsync(task.Uid, new TaskAlarm
        {
            Uid = "new-reminder",
            Description = "Owned collection edit",
            Trigger = fixture.Clock.UtcNow.AddHours(1),
        }, ct);
        SyncChange change = (await fixture.Store.GetPendingSyncChangesAsync(account.Id, ct))
            .Where(value => value.EntityUid == task.Uid).MaxBy(value => value.Id)!;
        await fixture.Store.MarkTaskChangeSyncedAsync(change.Id, task.Uid, task.RemoteHref, task.RemoteETag, ct);

        RemotePullApplyResult result = await fixture.Store.ApplyRemotePullAsync(
            list.Id, [IncomingPullUpdate(task.Uid)], [], states, ct);

        Assert.Equal(0, result.AppliedCount);
        SyncConflict conflict = Assert.Single(result.Conflicts);
        Assert.False(conflict.LocalTask.IsDirty);
        Assert.Equal("Owned collection edit", Assert.Single(conflict.LocalTask.Alarms).Description);
        Assert.Equal("Owned collection edit",
            Assert.Single((await fixture.Store.GetTaskForSyncAsync(task.Uid, ct))!.Alarms).Description);
    }

    [Fact]
    public async Task Pull_DefersAnOriginalTaskThatNoLongerExists()
    {
        var ct = TestContext.Current.CancellationToken;
        await using SyncFixture fixture = await SyncFixture.CreateAsync();
        (_, TodoList list) = await fixture.CreateMappedListAsync();
        TodoTask task = await SeedPullTaskAsync(fixture, list, ct);
        IReadOnlyList<RemoteTaskState> states = await fixture.Store.GetRemoteTaskStatesAsync(list.Id, ct);
        await using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = fixture.DatabasePath }.ToString());
        await connection.OpenAsync(ct);
        using SqliteCommand remove = connection.CreateCommand();
        remove.CommandText = "DELETE FROM tasks WHERE uid = @uid;";
        remove.Parameters.AddWithValue("@uid", task.Uid);
        await remove.ExecuteNonQueryAsync(ct);

        RemotePullApplyResult result = await fixture.Store.ApplyRemotePullAsync(
            list.Id, [IncomingPullUpdate(task.Uid)], [task.RemoteHref!], states, ct);

        Assert.Equal(0, result.AppliedCount);
        Assert.True(result.HasDeferredChanges);
        Assert.Empty(result.Conflicts);
        Assert.Null(await fixture.Store.GetTaskForSyncAsync(task.Uid, ct));
    }

    [Fact]
    public async Task Pull_CountsOnlyCommittedUpdatesAndDeletes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using SyncFixture fixture = await SyncFixture.CreateAsync();
        (_, TodoList list) = await fixture.CreateMappedListAsync();
        TodoTask task = await SeedPullTaskAsync(fixture, list, ct);
        IReadOnlyList<RemoteTaskState> states = await fixture.Store.GetRemoteTaskStatesAsync(list.Id, ct);

        RemotePullApplyResult updated = await fixture.Store.ApplyRemotePullAsync(
            list.Id, [IncomingPullUpdate(task.Uid)], [], states, ct);

        Assert.Equal(1, updated.AppliedCount);
        Assert.Empty(updated.Conflicts);
        Assert.False(updated.HasDeferredChanges);
        Assert.Equal("Remote value", (await fixture.Store.GetTaskForSyncAsync(task.Uid, ct))!.Summary);
        states = await fixture.Store.GetRemoteTaskStatesAsync(list.Id, ct);
        RemotePullApplyResult deleted = await fixture.Store.ApplyRemotePullAsync(
            list.Id, [], [task.RemoteHref!, "https://dav.example/cal/tasks/absent.ics"], states, ct);
        Assert.Equal(1, deleted.AppliedCount);
        Assert.Empty(deleted.Conflicts);
        Assert.True((await fixture.Store.GetTaskForSyncAsync(task.Uid, ct))!.IsDeleted);
    }

    private static async Task<TodoTask> SeedPullTaskAsync(
        SyncFixture fixture,
        TodoList list,
        CancellationToken cancellationToken)
    {
        var task = new TodoTask
        {
            Uid = Guid.NewGuid().ToString("D"),
            Summary = "Original value",
        };
        await fixture.Store.ApplyRemoteChangesAsync(list.Id, [new RemoteTaskUpdate
        {
            Task = task,
            Href = "https://dav.example/cal/tasks/guarded.ics",
            ETag = "\"v1\"",
        }], [], cancellationToken);
        return (await fixture.Store.GetTaskForSyncAsync(task.Uid, cancellationToken))!;
    }

    private static RemoteTaskUpdate IncomingPullUpdate(string uid)
    {
        return new RemoteTaskUpdate
        {
            Task = new TodoTask { Uid = uid, Summary = "Remote value" },
            Href = "https://dav.example/cal/tasks/guarded.ics",
            ETag = "\"v2\"",
        };
    }
}
