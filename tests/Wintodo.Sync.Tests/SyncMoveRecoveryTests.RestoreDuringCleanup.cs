using System.Net;
using Wintodo.Core.Models;

namespace Wintodo.Sync.Tests;

public sealed partial class SyncMoveRecoveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ParentRestoredDuringSourcePreflight_ReconfirmsLiveDestinationAndRetainsStepBindings(bool afterCommittedPut)
    {
        await using MoveTestFixture fixture = await MoveTestFixture.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        TodoTask parent = await fixture.SeedAsync();
        TodoTask child = await fixture.SeedAsync("child-task", parent.Uid, "child.ics");
        parent.Summary = "Latest parent before deletion";
        child.Summary = "Latest step before deletion";
        await fixture.Store.UpdateTaskAsync(parent, ct);
        await fixture.Store.UpdateTaskAsync(child, ct);
        await fixture.Store.MoveTaskAsync(parent.Uid, fixture.Destination.Id, cancellationToken: ct);
        if (afterCommittedPut)
        {
            bool interrupted = false;
            fixture.Server.AfterWrite = (request, _) =>
            {
                if (!interrupted && request.Method == "PUT")
                {
                    interrupted = true;
                    throw new HttpRequestException("Synthetic lost destination response before deletion");
                }
                return Task.CompletedTask;
            };
            AssertPendingNetworkFault((await fixture.SyncAsync()).Status);
            Assert.True(interrupted);
        }
        await fixture.Store.DeleteTaskAsync(parent.Uid, ct);
        fixture.Server.AfterWrite = null;
        bool restored = false;
        int sourceDeletes = 0;
        fixture.Server.BeforeRequest = async (request, token) =>
        {
            if (!restored && request.Method == "GET" && request.Url.StartsWith(fixture.Source.RemoteUrl!, StringComparison.Ordinal))
            {
                IReadOnlyList<TaskMoveIntent> pending = await fixture.Store.GetPendingTaskMovesAsync(cancellationToken: token);
                TaskMoveIntent[] members = pending.Where(move => move.TaskUid == parent.Uid || move.TaskUid == child.Uid).ToArray();
                if (members.Length == 2 && members.All(move => move.Phase >= TaskMovePhase.DestinationAttempted)
                    && fixture.Server.Tasks(fixture.Destination.RemoteUrl!).Count == 0)
                {
                    restored = true;
                    await fixture.Store.RestoreTaskAsync(parent.Uid, token);
                }
            }
            if (request.Method == "DELETE" && request.Url.StartsWith(fixture.Source.RemoteUrl!, StringComparison.Ordinal))
            {
                Assert.True(restored);
                IReadOnlyList<TodoTask> retained = fixture.Server.Tasks(fixture.Destination.RemoteUrl!);
                Assert.Equal(2, retained.Count);
                TodoTask retainedParent = Assert.Single(retained, task => task.Uid == parent.Uid);
                TodoTask retainedChild = Assert.Single(retained, task => task.Uid == child.Uid);
                Assert.Equal(parent.Summary, retainedParent.Summary);
                Assert.Equal(child.Summary, retainedChild.Summary);
                Assert.Equal(parent.Uid, retainedChild.ParentUid);
                sourceDeletes++;
            }
        };
        await fixture.SyncAsync();
        Assert.True(restored);
        await fixture.ReopenAsync();
        Assert.Equal(SyncRunStatus.Success, (await fixture.SyncAsync()).Status);
        Assert.Equal(SyncRunStatus.Success, (await fixture.SyncAsync()).Status);
        Assert.True(sourceDeletes > 0);
        Assert.Empty(fixture.Server.Tasks(fixture.Source.RemoteUrl!));
        IReadOnlyList<TodoTask> destination = fixture.Server.Tasks(fixture.Destination.RemoteUrl!);
        Assert.Equal(2, destination.Count);
        TodoTask storedParent = (await fixture.Store.GetTaskForSyncAsync(parent.Uid, ct))!;
        TodoTask storedChild = (await fixture.Store.GetTaskForSyncAsync(child.Uid, ct))!;
        Assert.False(storedParent.IsDeleted);
        Assert.False(storedParent.IsDirty);
        Assert.False(storedChild.IsDirty);
        Assert.Equal(parent.Summary, storedParent.Summary);
        Assert.Equal(child.Summary, storedChild.Summary);
        Assert.Equal(fixture.Destination.Id, storedChild.ListId);
        Assert.Equal(parent.Uid, storedChild.ParentUid);
        Assert.StartsWith(fixture.Destination.RemoteUrl!, storedParent.RemoteHref!, StringComparison.Ordinal);
        Assert.StartsWith(fixture.Destination.RemoteUrl!, storedChild.RemoteHref!, StringComparison.Ordinal);
        Assert.Equal(storedChild.Summary, Assert.Single(destination, task => task.Uid == child.Uid).Summary);
        Assert.Empty(await fixture.Store.GetPendingTaskMovesAsync(cancellationToken: ct));
    }
}
