using System.Net;
using Wintodo.Core.Models;

namespace Wintodo.Sync.Tests;

public sealed partial class SyncMoveRecoveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ParentDeleteBeforeOrAfterGroupAttempt_CleansRemoteStepsAndRestoreRequeuesThem(bool afterAttempt)
    {
        await using MoveTestFixture fixture = await MoveTestFixture.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        TodoTask parent = await fixture.SeedAsync();
        TodoTask child = await fixture.SeedAsync("child-task", parent.Uid, "child.ics");
        await fixture.Store.MoveTaskAsync(parent.Uid, fixture.Destination.Id, cancellationToken: ct);
        if (afterAttempt)
        {
            bool interrupted = false;
            fixture.Server.AfterWrite = (request, _) =>
            {
                if (!interrupted && request.Method == "PUT")
                {
                    interrupted = true;
                    throw new HttpRequestException("Synthetic lost group create response");
                }
                return Task.CompletedTask;
            };
            AssertPendingNetworkFault((await fixture.SyncAsync()).Status);
            Assert.True(interrupted);
        }
        await fixture.Store.DeleteTaskAsync(parent.Uid, ct);
        fixture.Server.AfterWrite = null;
        await fixture.ReopenAsync();
        Assert.Equal(SyncRunStatus.Success, (await fixture.SyncAsync()).Status);
        Assert.Equal(SyncRunStatus.Success, (await fixture.SyncAsync()).Status);
        Assert.Empty(fixture.Server.Tasks(fixture.Source.RemoteUrl!));
        Assert.Empty(fixture.Server.Tasks(fixture.Destination.RemoteUrl!));
        Assert.True((await fixture.Store.GetTaskForSyncAsync(parent.Uid, ct))!.IsDeleted);
        Assert.Empty(await fixture.Store.GetPendingTaskMovesAsync(cancellationToken: ct));

        await fixture.Store.RestoreTaskAsync(parent.Uid, ct);
        await fixture.ReopenAsync();
        Assert.Equal(SyncRunStatus.Success, (await fixture.SyncAsync()).Status);
        Assert.Equal(SyncRunStatus.Success, (await fixture.SyncAsync()).Status);
        Assert.Empty(fixture.Server.Tasks(fixture.Source.RemoteUrl!));
        IReadOnlyList<TodoTask> restored = fixture.Server.Tasks(fixture.Destination.RemoteUrl!);
        Assert.Equal(2, restored.Count);
        Assert.Equal(parent.Uid, Assert.Single(restored, value => value.Uid == child.Uid).ParentUid);
        Assert.False((await fixture.Store.GetTaskForSyncAsync(parent.Uid, ct))!.IsDeleted);
        Assert.Equal(fixture.Destination.Id, (await fixture.Store.GetTaskForSyncAsync(child.Uid, ct))!.ListId);
    }

    [Fact]
    public async Task DeleteBeforeMoveAttempt_RemovesSourceWithoutCreatingDestination()
    {
        await using MoveTestFixture fixture = await MoveTestFixture.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        TodoTask task = await fixture.SeedAsync();
        await fixture.Store.MoveTaskAsync(task.Uid, fixture.Destination.Id, cancellationToken: ct);
        await fixture.Store.DeleteTaskAsync(task.Uid, ct);
        Assert.Equal(SyncRunStatus.Success, (await fixture.SyncAsync()).Status);
        Assert.True((await fixture.Store.GetTaskForSyncAsync(task.Uid, ct))!.IsDeleted);
        Assert.Empty(fixture.Server.Tasks(fixture.Source.RemoteUrl!));
        Assert.Empty(fixture.Server.Tasks(fixture.Destination.RemoteUrl!));
        Assert.DoesNotContain(fixture.Server.Requests, request => request.Method == "PUT");
        Assert.Empty(await fixture.Store.GetPendingTaskMovesAsync(cancellationToken: ct));
    }

    [Fact]
    public async Task DeleteAfterAmbiguousCreate_RemovesBothCopiesThenRestoreCanRecreateDestination()
    {
        await using MoveTestFixture fixture = await MoveTestFixture.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        TodoTask task = await fixture.SeedAsync();
        await fixture.Store.MoveTaskAsync(task.Uid, fixture.Destination.Id, cancellationToken: ct);
        bool interrupted = false;
        fixture.Server.AfterWrite = (request, _) =>
        {
            if (!interrupted && request.Method == "PUT")
            {
                interrupted = true;
                throw new HttpRequestException("Synthetic lost create response");
            }
            return Task.CompletedTask;
        };
        AssertPendingNetworkFault((await fixture.SyncAsync()).Status);
        Assert.True(interrupted);
        await fixture.Store.DeleteTaskAsync(task.Uid, ct);
        fixture.Server.AfterWrite = null;
        await fixture.ReopenAsync();
        Assert.Equal(SyncRunStatus.Success, (await fixture.SyncAsync()).Status);
        Assert.Equal(SyncRunStatus.Success, (await fixture.SyncAsync()).Status);
        Assert.Empty(fixture.Server.Tasks(fixture.Source.RemoteUrl!));
        Assert.Empty(fixture.Server.Tasks(fixture.Destination.RemoteUrl!));
        Assert.True((await fixture.Store.GetTaskForSyncAsync(task.Uid, ct))!.IsDeleted);

        await fixture.Store.RestoreTaskAsync(task.Uid, ct);
        await fixture.ReopenAsync();
        Assert.Equal(SyncRunStatus.Success, (await fixture.SyncAsync()).Status);
        Assert.Empty(fixture.Server.Tasks(fixture.Source.RemoteUrl!));
        Assert.Equal(task.Uid, Assert.Single(fixture.Server.Tasks(fixture.Destination.RemoteUrl!)).Uid);
        Assert.False((await fixture.Store.GetTaskForSyncAsync(task.Uid, ct))!.IsDeleted);
        Assert.All(fixture.Server.Requests.Where(request => request.Method == "DELETE"), request => Assert.NotNull(request.IfMatch));
    }

    [Fact]
    public async Task DestinationCollision_ConditionalCreateNeverOverwritesUnrelatedTask()
    {
        await using MoveTestFixture fixture = await MoveTestFixture.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        TodoTask task = await fixture.SeedAsync();
        await fixture.Store.MoveTaskAsync(task.Uid, fixture.Destination.Id, cancellationToken: ct);
        bool collided = false;
        byte[] unrelated = fixture.Serialize(fixture.Destination, new TodoTask
        {
            Uid = "unrelated-task", Summary = "Unrelated remote contents", Created = fixture.Clock.UtcNow,
            LastModified = fixture.Clock.UtcNow,
        });
        fixture.Server.BeforeRequest = (request, _) =>
        {
            if (!collided && request.Method == "PUT")
            {
                collided = true;
                fixture.Server.Seed(request.Url, unrelated);
            }
            return Task.CompletedTask;
        };
        Assert.Equal(SyncRunStatus.AttentionRequired, (await fixture.SyncAsync()).Status);
        Assert.True(collided);
        Assert.True(fixture.Server.Contains(fixture.SourceHref));
        TodoTask retained = Assert.Single(fixture.Server.Tasks(fixture.Destination.RemoteUrl!));
        Assert.Equal("unrelated-task", retained.Uid);
        Assert.Equal("Unrelated remote contents", retained.Summary);
        Assert.DoesNotContain(fixture.Server.Requests, request => request.Method == "DELETE");
    }
}
