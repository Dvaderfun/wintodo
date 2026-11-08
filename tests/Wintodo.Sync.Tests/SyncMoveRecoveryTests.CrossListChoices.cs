using Wintodo.Core.Models;
using Wintodo.Core.Queries;

namespace Wintodo.Sync.Tests;

public sealed partial class SyncMoveRecoveryTests
{
    [Theory]
    [InlineData(true, SyncConflictChoice.KeepMine)]
    [InlineData(true, SyncConflictChoice.KeepTheirs)]
    [InlineData(true, SyncConflictChoice.KeepBoth)]
    [InlineData(false, SyncConflictChoice.KeepMine)]
    [InlineData(false, SyncConflictChoice.KeepTheirs)]
    [InlineData(false, SyncConflictChoice.KeepBoth)]
    public async Task CrossListConflict_ChoicesConvergeWithCorrectMembershipAndVersionRetention(
        bool dirtySecondClient, SyncConflictChoice choice)
    {
        var scene = await CreateCrossListChoiceSceneAsync(dirtySecondClient);
        await using MoveTestFixture first = scene.First;
        await using MoveTestFixture second = scene.Second;
        var ct = TestContext.Current.CancellationToken;
        string knownLocalSummary = dirtySecondClient ? "Dirty second-client version" : "Original task";
        string chosenRemoteSummary = dirtySecondClient ? "Original task" : "Older replacement server version";
        TodoTask before = (await second.Store.GetTaskForSyncAsync(scene.Task.Uid, ct))!;
        Assert.Equal(second.Source.Id, before.ListId);
        Assert.Equal(knownLocalSummary, before.Summary);
        Assert.False(before.IsDeleted);

        await second.ResolveAsync(scene.Conflict, choice);
        await second.ReopenAsync();
        Assert.Equal(SyncRunStatus.Success, (await second.SyncAsync()).Status);
        Assert.Equal(SyncRunStatus.Success, (await second.SyncAsync()).Status);
        IReadOnlyList<TodoTask> source = first.Server.Tasks(first.Source.RemoteUrl!);
        IReadOnlyList<TodoTask> destination = first.Server.Tasks(first.Destination.RemoteUrl!);
        TodoTask current = (await second.Store.GetTaskForSyncAsync(scene.Task.Uid, ct))!;
        Assert.False(current.IsDeleted);
        Assert.False(current.IsDirty);
        Assert.Equal(source.Count + destination.Count,
            source.Concat(destination).Select(task => task.Uid).Distinct(StringComparer.Ordinal).Count());

        if (choice == SyncConflictChoice.KeepMine)
        {
            Assert.Empty(destination);
            TodoTask returned = Assert.Single(source);
            Assert.Equal(scene.Task.Uid, returned.Uid);
            Assert.Equal(knownLocalSummary, returned.Summary);
            Assert.Equal(second.Source.Id, current.ListId);
            Assert.Equal(knownLocalSummary, current.Summary);
            Assert.StartsWith(first.Source.RemoteUrl!, current.RemoteHref!, StringComparison.Ordinal);
        }
        else if (choice == SyncConflictChoice.KeepTheirs)
        {
            Assert.Empty(source);
            TodoTask retainedRemote = Assert.Single(destination);
            Assert.Equal(scene.Task.Uid, retainedRemote.Uid);
            Assert.Equal(chosenRemoteSummary, retainedRemote.Summary);
            Assert.Equal(second.Destination.Id, current.ListId);
            Assert.Equal(chosenRemoteSummary, current.Summary);
            Assert.StartsWith(first.Destination.RemoteUrl!, current.RemoteHref!, StringComparison.Ordinal);
        }
        else
        {
            TodoTask copiedLocal = Assert.Single(source);
            Assert.NotEqual(scene.Task.Uid, copiedLocal.Uid);
            Assert.Equal(knownLocalSummary, copiedLocal.Summary);
            TodoTask retainedRemote = Assert.Single(destination);
            Assert.Equal(scene.Task.Uid, retainedRemote.Uid);
            Assert.Equal(chosenRemoteSummary, retainedRemote.Summary);
            Assert.Equal(second.Destination.Id, current.ListId);
            Assert.Equal(chosenRemoteSummary, current.Summary);
            TodoTask storedCopy = (await second.Store.GetTaskForSyncAsync(copiedLocal.Uid, ct))!;
            Assert.Equal(second.Source.Id, storedCopy.ListId);
            Assert.Equal(knownLocalSummary, storedCopy.Summary);
            Assert.False(storedCopy.IsDirty);
        }

        IReadOnlyList<TodoTask> visible = await second.Store.GetTasksAsync(new ListQuery
        {
            Kind = ListQueryKind.All, IncludeCompleted = true,
        }, ct);
        Assert.Equal(choice == SyncConflictChoice.KeepBoth ? 2 : 1, visible.Count);
    }

    [Fact]
    public async Task StaleCrossListKeepTheirs_DoesNotOverwriteNewLocalEditOrChangeMembership()
    {
        var scene = await CreateCrossListChoiceSceneAsync(dirtySecondClient: true);
        await using MoveTestFixture first = scene.First;
        await using MoveTestFixture second = scene.Second;
        var ct = TestContext.Current.CancellationToken;
        TodoTask latest = (await second.Store.GetTaskForSyncAsync(scene.Task.Uid, ct))!;
        latest.Summary = "New edit after cross-list chooser opened";
        await second.Store.UpdateTaskAsync(latest, ct);
        int beforeWrites = first.Server.Requests.Count(request => request.Method is "PUT" or "DELETE");
        try { await second.ResolveAsync(scene.Conflict, SyncConflictChoice.KeepTheirs); }
        catch (InvalidOperationException) { }
        TodoTask preserved = (await second.Store.GetTaskForSyncAsync(scene.Task.Uid, ct))!;
        Assert.Equal(second.Source.Id, preserved.ListId);
        Assert.Equal(latest.Summary, preserved.Summary);
        Assert.False(preserved.IsDeleted);
        Assert.True(preserved.IsDirty);
        Assert.Equal(beforeWrites, first.Server.Requests.Count(request => request.Method is "PUT" or "DELETE"));
        Assert.Empty(first.Server.Tasks(first.Source.RemoteUrl!));
        Assert.Equal("Original task", Assert.Single(first.Server.Tasks(first.Destination.RemoteUrl!)).Summary);
    }

    private static async Task<(MoveTestFixture First, MoveTestFixture Second, TodoTask Task, SyncConflict Conflict)>
        CreateCrossListChoiceSceneAsync(bool dirtySecondClient)
    {
        MoveTestFixture first = await MoveTestFixture.CreateAsync();
        MoveTestFixture? second = null;
        try
        {
            var ct = TestContext.Current.CancellationToken;
            TodoTask task = await first.SeedAsync();
            second = await MoveTestFixture.CreateAsync(server: first.Server);
            Assert.Equal(SyncRunStatus.Success, (await second.SyncAsync()).Status);
            if (dirtySecondClient)
            {
                TodoTask edited = (await second.Store.GetTaskForSyncAsync(task.Uid, ct))!;
                edited.Summary = "Dirty second-client version";
                await second.Store.UpdateTaskAsync(edited, ct);
                await first.Store.MoveTaskAsync(task.Uid, first.Destination.Id, cancellationToken: ct);
                Assert.Equal(SyncRunStatus.Success, (await first.SyncAsync()).Status);
            }
            else
            {
                first.Server.Remove(first.SourceHref);
                task.Sequence--;
                task.Summary = "Older replacement server version";
                first.Server.Seed(first.Destination.RemoteUrl + "replacement.ics", first.Serialize(first.Destination, task));
            }
            SyncRunResult observed = await second.SyncAsync();
            Assert.Equal(SyncRunStatus.AttentionRequired, observed.Status);
            SyncConflict conflict = Assert.Single(observed.Conflicts,
                value => value.ListId == second.Destination.Id && value.RemoteTask?.Uid == task.Uid);
            return (first, second, task, conflict);
        }
        catch
        {
            if (second is not null) await second.DisposeAsync();
            await first.DisposeAsync();
            throw;
        }
    }
}
