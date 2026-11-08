using System.Net;
using Wintodo.Core.Models;
using Wintodo.Core.Services;

namespace Wintodo.Sync.Tests;

public sealed partial class SyncMoveRecoveryTests
{
    [Fact]
    public async Task ApprovedKeepBoth_LostSourceCopyResponseThenLocalEditRetainsApprovedVersionOnce()
    {
        await using MoveTestFixture fixture = await MoveTestFixture.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        TodoTask task = await fixture.SeedAsync();
        await fixture.Store.MoveTaskAsync(task.Uid, fixture.Destination.Id, cancellationToken: ct);
        task.Summary = "Approved concurrent source version";
        task.Sequence += 2;
        fixture.Server.Seed(fixture.SourceHref, fixture.Serialize(fixture.Source, task));
        SyncConflict conflict = Assert.Single((await fixture.SyncAsync()).Conflicts);
        bool interrupted = false;
        fixture.Server.AfterWrite = (request, _) =>
        {
            if (!interrupted && request.Method == "PUT"
                && request.Url.StartsWith(fixture.Source.RemoteUrl!, StringComparison.Ordinal)
                && request.Url != fixture.SourceHref)
            {
                interrupted = true;
                throw new HttpRequestException("Synthetic lost approved source-copy response");
            }
            return Task.CompletedTask;
        };
        try { await fixture.ResolveAsync(conflict, SyncConflictChoice.KeepBoth); }
        catch (HttpRequestException) { }
        if (!interrupted) await fixture.SyncAsync();
        Assert.True(interrupted);
        TodoTask local = (await fixture.Store.GetTaskForSyncAsync(task.Uid, ct))!;
        local.Summary = "Newer edit after KeepBoth approval";
        await fixture.Store.UpdateTaskAsync(local, ct);
        fixture.Server.AfterWrite = null;
        await fixture.ReopenAsync();
        Assert.Equal(SyncRunStatus.Success, (await fixture.SyncAsync()).Status);
        Assert.Equal(SyncRunStatus.Success, (await fixture.SyncAsync()).Status);
        TodoTask sourceCopy = Assert.Single(fixture.Server.Tasks(fixture.Source.RemoteUrl!));
        Assert.NotEqual(task.Uid, sourceCopy.Uid);
        Assert.Equal("Approved concurrent source version", sourceCopy.Summary);
        TodoTask destination = Assert.Single(fixture.Server.Tasks(fixture.Destination.RemoteUrl!));
        Assert.Equal(task.Uid, destination.Uid);
        Assert.Equal(local.Summary, destination.Summary);
        Assert.Equal(local.Summary, (await fixture.Store.GetTaskForSyncAsync(task.Uid, ct))!.Summary);
    }

    [Fact]
    public async Task ApprovedKeepTheirs_InterruptedCompensationAndNewerEditRequiresFreshChoice()
    {
        await using MoveTestFixture fixture = await MoveTestFixture.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        TodoTask task = await fixture.SeedAsync();
        await fixture.Store.MoveTaskAsync(task.Uid, fixture.Destination.Id, cancellationToken: ct);
        task.Summary = "Approved source value before compensation";
        task.Sequence += 2;
        fixture.Server.Seed(fixture.SourceHref, fixture.Serialize(fixture.Source, task));
        SyncConflict conflict = Assert.Single((await fixture.SyncAsync()).Conflicts);
        bool interrupted = false;
        fixture.Server.AfterWrite = (request, _) =>
        {
            if (!interrupted && request.Method == "DELETE"
                && request.Url.StartsWith(fixture.Destination.RemoteUrl!, StringComparison.Ordinal))
            {
                interrupted = true;
                throw new HttpRequestException("Synthetic lost approved destination-removal response");
            }
            return Task.CompletedTask;
        };
        try { await fixture.ResolveAsync(conflict, SyncConflictChoice.KeepTheirs); }
        catch (HttpRequestException) { }
        if (!interrupted) await fixture.SyncAsync();
        Assert.True(interrupted);
        TodoTask local = (await fixture.Store.GetTaskForSyncAsync(task.Uid, ct))!;
        local.Summary = "Newer edit after KeepTheirs approval";
        await fixture.Store.UpdateTaskAsync(local, ct);
        fixture.Server.AfterWrite = null;
        await fixture.ReopenAsync();
        SyncRunResult resumed = await fixture.SyncAsync();
        Assert.Equal(SyncRunStatus.AttentionRequired, resumed.Status);
        TodoTask retained = (await fixture.Store.GetTaskForSyncAsync(task.Uid, ct))!;
        Assert.Equal(local.Summary, retained.Summary);
        Assert.Equal(fixture.Destination.Id, retained.ListId);
        Assert.True(fixture.Server.Contains(fixture.SourceHref));
        SyncConflict fresh = Assert.Single(resumed.Conflicts, value => value.LocalTask.Uid == task.Uid);
        await fixture.ResolveAsync(fresh, SyncConflictChoice.KeepMine);
        Assert.Equal(SyncRunStatus.Success, (await fixture.SyncAsync()).Status);
        Assert.Equal(SyncRunStatus.Success, (await fixture.SyncAsync()).Status);
        Assert.Empty(fixture.Server.Tasks(fixture.Source.RemoteUrl!));
        TodoTask destination = Assert.Single(fixture.Server.Tasks(fixture.Destination.RemoteUrl!));
        Assert.Equal(task.Uid, destination.Uid);
        Assert.Equal(local.Summary, destination.Summary);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CleanSecondClient_RejectsOlderOrChangedSameSequenceReplacement(
        bool sameSequenceChangedDescription, bool reverse)
    {
        await using MoveTestFixture first = await MoveTestFixture.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        TodoTask task = await first.SeedAsync();
        await using MoveTestFixture second = await MoveTestFixture.CreateAsync(server: first.Server, reverseCreation: reverse);
        Assert.Equal(SyncRunStatus.Success, (await second.SyncAsync()).Status);
        first.Server.Remove(first.SourceHref);
        if (sameSequenceChangedDescription) task.Description = "Altered target at same sequence";
        else task.Sequence--;
        first.Server.Seed(first.Destination.RemoteUrl + "replacement.ics", first.Serialize(first.Destination, task));

        SyncRunResult result = await second.SyncAsync();
        Assert.Equal(SyncRunStatus.AttentionRequired, result.Status);
        Assert.Contains(result.Conflicts, conflict => conflict.LocalTask.Uid == task.Uid);
        TodoTask retained = (await second.Store.GetTaskForSyncAsync(task.Uid, ct))!;
        Assert.Equal(second.Source.Id, retained.ListId);
        Assert.Equal("Retained description", retained.Description);
        Assert.False(retained.IsDeleted);
        Assert.Empty(first.Server.Tasks(first.Source.RemoteUrl!));
        Assert.Equal(task.Sequence, Assert.Single(first.Server.Tasks(first.Destination.RemoteUrl!)).Sequence);
    }

    [Fact]
    public async Task MissingSourceCredential_RetainsPendingMoveAndNeverUsesDestinationCredential()
    {
        await using MoveTestFixture fixture = await MoveTestFixture.CreateAsync(crossAccount: true);
        var ct = TestContext.Current.CancellationToken;
        TodoTask task = await fixture.SeedAsync();
        await fixture.Store.MoveTaskAsync(task.Uid, fixture.Destination.Id, cancellationToken: ct);
        fixture.Credentials.Secrets.Remove(fixture.SourceAccount.CredentialKey);
        Assert.NotEqual(SyncRunStatus.Success, (await fixture.SyncAsync()).Status);
        Assert.True(fixture.Server.Contains(fixture.SourceHref));
        Assert.Contains(await fixture.Store.GetPendingTaskMovesAsync(cancellationToken: ct), move => move.TaskUid == task.Uid);
        Assert.DoesNotContain(fixture.Server.Requests, request => request.Method == "DELETE");
        fixture.Credentials.Secrets[fixture.SourceAccount.CredentialKey] = "source-secret";
        await fixture.ReopenAsync();
        Assert.Equal(SyncRunStatus.Success, (await fixture.SyncAsync()).Status);
        Assert.False(fixture.Server.Contains(fixture.SourceHref));
    }

    [Fact]
    public async Task MoveDuringSourcePull_PreservesNewMembershipAndDurableCleanup()
    {
        await using MoveTestFixture fixture = await MoveTestFixture.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        TodoTask task = await fixture.SeedAsync();
        task.Summary = "Concurrent source update";
        fixture.Server.Seed(fixture.SourceHref, fixture.Serialize(fixture.Source, task));
        bool moved = false;
        fixture.Server.BeforeRequest = async (request, token) =>
        {
            if (!moved && request.Method == "GET" && request.Url == fixture.SourceHref)
            {
                moved = true;
                await fixture.Store.MoveTaskAsync(task.Uid, fixture.Destination.Id, cancellationToken: token);
            }
        };
        Assert.Equal(SyncRunStatus.AttentionRequired, (await fixture.SyncAsync()).Status);
        Assert.True(moved);
        TodoTask stored = (await fixture.Store.GetTaskForSyncAsync(task.Uid, ct))!;
        Assert.Equal(fixture.Destination.Id, stored.ListId);
        Assert.Equal("Original task", stored.Summary);
        Assert.True(fixture.Server.Contains(fixture.SourceHref));
        Assert.Contains(await fixture.Store.GetPendingTaskMovesAsync(cancellationToken: ct), move => move.TaskUid == task.Uid);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CleanSecondClient_AdoptsConfirmedMoveRegardlessOfCollectionOrAccountOrder(bool crossAccount, bool reverse)
    {
        await using MoveTestFixture first = await MoveTestFixture.CreateAsync(crossAccount);
        var ct = TestContext.Current.CancellationToken;
        TodoTask task = await first.SeedAsync();
        await using MoveTestFixture second = await MoveTestFixture.CreateAsync(crossAccount, first.Server, reverse);
        first.Server.ReverseCollections = reverse;
        Assert.Equal(SyncRunStatus.Success, (await second.SyncAsync()).Status);
        TodoTask before = (await second.Store.GetTaskForSyncAsync(task.Uid, ct))!;
        Assert.NotNull(before);
        Assert.Equal(second.Source.Id, before.ListId);
        Assert.False(before.IsDirty);

        await first.Store.MoveTaskAsync(task.Uid, first.Destination.Id, cancellationToken: ct);
        Assert.Equal(SyncRunStatus.Success, (await first.SyncAsync()).Status);
        Assert.Equal(SyncRunStatus.Success, (await second.SyncAsync()).Status);
        Assert.Equal(SyncRunStatus.Success, (await second.SyncAsync()).Status);
        TodoTask after = (await second.Store.GetTaskForSyncAsync(task.Uid, ct))!;
        Assert.Equal(second.Destination.Id, after.ListId);
        Assert.Equal(task.Summary, after.Summary);
        Assert.False(after.IsDeleted);
        Assert.False(after.IsDirty);
        Assert.Empty(first.Server.Tasks(first.Source.RemoteUrl!));
        Assert.Equal(task.Uid, Assert.Single(first.Server.Tasks(first.Destination.RemoteUrl!)).Uid);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DirtySecondClient_PreservesItsEditAsConflictInsteadOfOverwritingMovedVersion(bool reverse)
    {
        await using MoveTestFixture first = await MoveTestFixture.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        TodoTask task = await first.SeedAsync();
        await using MoveTestFixture second = await MoveTestFixture.CreateAsync(server: first.Server, reverseCreation: reverse);
        Assert.Equal(SyncRunStatus.Success, (await second.SyncAsync()).Status);
        TodoTask edited = (await second.Store.GetTaskForSyncAsync(task.Uid, ct))!;
        edited.Summary = "Second client unsynced edit";
        await second.Store.UpdateTaskAsync(edited, ct);
        await first.Store.MoveTaskAsync(task.Uid, first.Destination.Id, cancellationToken: ct);
        Assert.Equal(SyncRunStatus.Success, (await first.SyncAsync()).Status);

        SyncRunResult result = await second.SyncAsync();
        Assert.Equal(SyncRunStatus.AttentionRequired, result.Status);
        Assert.Contains(result.Conflicts, conflict => conflict.LocalTask.Uid == task.Uid);
        TodoTask retained = (await second.Store.GetTaskForSyncAsync(task.Uid, ct))!;
        Assert.Equal("Second client unsynced edit", retained.Summary);
        Assert.True(retained.IsDirty);
        Assert.False(retained.IsDeleted);
        Assert.Equal("Original task", Assert.Single(first.Server.Tasks(first.Destination.RemoteUrl!)).Summary);
        Assert.Empty(first.Server.Tasks(first.Source.RemoteUrl!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnconfirmedDuplicate_DoesNotMoveCleanSecondClientBackOrForwardSilently(bool reverse)
    {
        await using MoveTestFixture first = await MoveTestFixture.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        TodoTask task = await first.SeedAsync();
        await using MoveTestFixture second = await MoveTestFixture.CreateAsync(server: first.Server, reverseCreation: reverse);
        Assert.Equal(SyncRunStatus.Success, (await second.SyncAsync()).Status);
        await first.Store.MoveTaskAsync(task.Uid, first.Destination.Id, cancellationToken: ct);
        first.Server.BeforeRequest = (request, _) => request.Method == "DELETE"
            ? throw new HttpRequestException("Synthetic source offline") : Task.CompletedTask;
        AssertPendingNetworkFault((await first.SyncAsync()).Status);
        Assert.Contains(first.Server.Requests, request => request.Method == "DELETE" && request.Url == first.SourceHref);
        Assert.Equal(task.Uid, Assert.Single(first.Server.Tasks(first.Source.RemoteUrl!)).Uid);
        Assert.Equal(task.Uid, Assert.Single(first.Server.Tasks(first.Destination.RemoteUrl!)).Uid);

        Assert.Equal(SyncRunStatus.AttentionRequired, (await second.SyncAsync()).Status);
        TodoTask retained = (await second.Store.GetTaskForSyncAsync(task.Uid, ct))!;
        Assert.Equal(second.Source.Id, retained.ListId);
        Assert.Equal(second.SourceHref, retained.RemoteHref);
        Assert.False(retained.IsDeleted);
        first.Server.BeforeRequest = null;
        Assert.Equal(SyncRunStatus.Success, (await first.SyncAsync()).Status);
        Assert.Equal(SyncRunStatus.Success, (await second.SyncAsync()).Status);
        Assert.Equal(second.Destination.Id, (await second.Store.GetTaskForSyncAsync(task.Uid, ct))!.ListId);
    }

    [Fact]
    public async Task RepeatedMoveAfterDestinationAttempt_ProcessesSuccessorAndRemovesIntermediateCopy()
    {
        await using MoveTestFixture fixture = await MoveTestFixture.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        TodoTask task = await fixture.SeedAsync();
        TodoList third = await fixture.CreateMappedListAsync(fixture.SourceAccount, "c", "Final destination");
        await fixture.Store.MoveTaskAsync(task.Uid, fixture.Destination.Id, cancellationToken: ct);
        bool repeated = false;
        fixture.Server.AfterWrite = async (request, token) =>
        {
            if (!repeated && request.Method == "PUT")
            {
                repeated = true;
                await fixture.Store.MoveTaskAsync(task.Uid, third.Id, cancellationToken: token);
            }
        };
        await fixture.SyncAsync();
        fixture.Server.AfterWrite = null;
        await fixture.ReopenAsync();
        Assert.Equal(SyncRunStatus.Success, (await fixture.SyncAsync()).Status);
        Assert.Equal(SyncRunStatus.Success, (await fixture.SyncAsync()).Status);
        Assert.True(repeated);
        Assert.Empty(fixture.Server.Tasks(fixture.Source.RemoteUrl!));
        Assert.Empty(fixture.Server.Tasks(fixture.Destination.RemoteUrl!));
        Assert.Equal(task.Uid, Assert.Single(fixture.Server.Tasks(third.RemoteUrl!)).Uid);
        Assert.Equal(third.Id, (await fixture.Store.GetTaskForSyncAsync(task.Uid, ct))!.ListId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IndependentlyMovingStepBeforeOrAfterItsGroupAttempt_PreservesPromotion(bool afterAttempt)
    {
        await using MoveTestFixture fixture = await MoveTestFixture.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        TodoTask parent = await fixture.SeedAsync();
        TodoTask child = await fixture.SeedAsync("child-task", parent.Uid, "child.ics");
        TodoList third = await fixture.CreateMappedListAsync(fixture.SourceAccount, "c", "Promoted step destination");
        await fixture.Store.MoveTaskAsync(parent.Uid, fixture.Destination.Id, cancellationToken: ct);
        bool promoted = false;
        if (!afterAttempt)
        {
            await fixture.Store.MoveTaskAsync(child.Uid, third.Id, cancellationToken: ct);
            promoted = true;
            parent.Summary = "Concurrent source parent after step promotion";
            fixture.Server.Seed(fixture.SourceHref, fixture.Serialize(fixture.Source, parent));
        }
        else
        {
            fixture.Server.AfterWrite = async (request, token) =>
            {
                if (!promoted && request.Method == "PUT" && request.Payload is not null
                    && new Wintodo.Core.Ical.IcalReader(TimeZoneInfo.Utc).Read(request.Payload).Tasks.Single().Uid == child.Uid)
                {
                    promoted = true;
                    await fixture.Store.MoveTaskAsync(child.Uid, third.Id, cancellationToken: token);
                }
            };
        }
        SyncRunResult initial = await fixture.SyncAsync();
        if (!afterAttempt)
        {
            Assert.Equal(SyncRunStatus.AttentionRequired, initial.Status);
            SyncConflict conflict = Assert.Single(initial.Conflicts, value => value.LocalTask.Uid == parent.Uid);
            await fixture.ResolveAsync(conflict, SyncConflictChoice.KeepTheirs);
        }
        fixture.Server.AfterWrite = null;
        await fixture.ReopenAsync();
        Assert.Equal(SyncRunStatus.Success, (await fixture.SyncAsync()).Status);
        Assert.Equal(SyncRunStatus.Success, (await fixture.SyncAsync()).Status);
        Assert.True(promoted);
        if (afterAttempt)
        {
            Assert.Empty(fixture.Server.Tasks(fixture.Source.RemoteUrl!));
            Assert.Equal(parent.Uid, Assert.Single(fixture.Server.Tasks(fixture.Destination.RemoteUrl!)).Uid);
        }
        else
        {
            Assert.Equal(parent.Uid, Assert.Single(fixture.Server.Tasks(fixture.Source.RemoteUrl!)).Uid);
            Assert.Empty(fixture.Server.Tasks(fixture.Destination.RemoteUrl!));
        }
        TodoTask remoteChild = Assert.Single(fixture.Server.Tasks(third.RemoteUrl!));
        Assert.Equal(child.Uid, remoteChild.Uid);
        Assert.Null(remoteChild.ParentUid);
        TodoTask stored = (await fixture.Store.GetTaskForSyncAsync(child.Uid, ct))!;
        Assert.Equal(third.Id, stored.ListId);
        Assert.Null(stored.ParentUid);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UndoMoveBeforeOrAfterAttempt_RestoresMembershipWithoutOrphanedCopies(bool afterAttempt)
    {
        await using MoveTestFixture fixture = await MoveTestFixture.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        TodoTask task = await fixture.SeedAsync();
        IReadOnlyList<TaskUndoState> undo = await fixture.Store.CaptureTaskUndoAsync([task.Uid], TaskUndoFields.List, ct);
        await fixture.Store.MoveTaskAsync(task.Uid, fixture.Destination.Id, cancellationToken: ct);
        if (!afterAttempt) await fixture.Store.RestoreTaskFieldsAsync(undo, ct);
        else
        {
            bool restored = false;
            fixture.Server.AfterWrite = async (request, token) =>
            {
                if (!restored && request.Method == "PUT")
                {
                    restored = true;
                    await fixture.Store.RestoreTaskFieldsAsync(undo, token);
                }
            };
        }
        await fixture.SyncAsync();
        fixture.Server.AfterWrite = null;
        await fixture.ReopenAsync();
        Assert.Equal(SyncRunStatus.Success, (await fixture.SyncAsync()).Status);
        Assert.Equal(SyncRunStatus.Success, (await fixture.SyncAsync()).Status);
        Assert.Empty(fixture.Server.Tasks(fixture.Destination.RemoteUrl!));
        Assert.Equal(task.Uid, Assert.Single(fixture.Server.Tasks(fixture.Source.RemoteUrl!)).Uid);
        Assert.Equal(fixture.Source.Id, (await fixture.Store.GetTaskForSyncAsync(task.Uid, ct))!.ListId);
    }

    [Fact]
    public async Task StaleConflictChoice_CannotDisposeNewerSourceOrLocalRevision()
    {
        await using MoveTestFixture fixture = await MoveTestFixture.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        TodoTask task = await fixture.SeedAsync();
        await fixture.Store.MoveTaskAsync(task.Uid, fixture.Destination.Id, cancellationToken: ct);
        task.Summary = "Concurrent source value";
        fixture.Server.Seed(fixture.SourceHref, fixture.Serialize(fixture.Source, task));
        SyncConflict conflict = Assert.Single((await fixture.SyncAsync()).Conflicts);
        TodoTask local = (await fixture.Store.GetTaskForSyncAsync(task.Uid, ct))!;
        local.Summary = "Newer local edit after chooser opened";
        await fixture.Store.UpdateTaskAsync(local, ct);
        task.Summary = "Newer remote edit after chooser opened";
        fixture.Server.Seed(fixture.SourceHref, fixture.Serialize(fixture.Source, task));
        try { await fixture.ResolveAsync(conflict, SyncConflictChoice.KeepMine); }
        catch (InvalidOperationException) { }
        Assert.True(fixture.Server.Contains(fixture.SourceHref));
        Assert.Equal("Newer remote edit after chooser opened", Assert.Single(fixture.Server.Tasks(fixture.Source.RemoteUrl!)).Summary);
        Assert.Equal(local.Summary, (await fixture.Store.GetTaskForSyncAsync(task.Uid, ct))!.Summary);
    }

    [Theory]
    [InlineData("disable")]
    [InlineData("disconnect")]
    [InlineData("delete-list")]
    public async Task PendingMove_AccountOrListChangesRetainIntentOrAreExplicitlyBlocked(string operation)
    {
        await using MoveTestFixture fixture = await MoveTestFixture.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        TodoTask task = await fixture.SeedAsync();
        await fixture.Store.MoveTaskAsync(task.Uid, fixture.Destination.Id, cancellationToken: ct);
        bool blocked = false;
        try
        {
            if (operation == "disable") await fixture.Store.SetListSyncEnabledAsync(fixture.Source.Id, false, ct);
            else if (operation == "delete-list") await fixture.Store.DeleteListAsync(fixture.Source.Id, ct);
            else
            {
                using var http = new HttpClient(fixture.Server, false);
                using var sync = new CalDavSyncService(fixture.Store, fixture.Credentials, fixture.Clock, http);
                await sync.DisconnectAsync(fixture.SourceAccount, ct);
            }
        }
        catch (InvalidOperationException) { blocked = true; }
        Assert.NotEmpty(await fixture.Store.GetPendingTaskMovesAsync(cancellationToken: ct));
        Assert.True(fixture.Server.Contains(fixture.SourceHref));
        Assert.Equal(fixture.Destination.Id, (await fixture.Store.GetTaskForSyncAsync(task.Uid, ct))!.ListId);
        if (blocked) Assert.True(fixture.Credentials.Secrets.ContainsKey(fixture.SourceAccount.CredentialKey));
    }

    [Theory]
    [InlineData(SyncConflictChoice.KeepMine)]
    [InlineData(SyncConflictChoice.KeepTheirs)]
    [InlineData(SyncConflictChoice.KeepBoth)]
    public async Task ParentMoveConflict_ChoiceKeepsStepMembershipAndRelationshipsConsistent(SyncConflictChoice choice)
    {
        await using MoveTestFixture fixture = await MoveTestFixture.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        TodoTask parent = await fixture.SeedAsync();
        TodoTask child = await fixture.SeedAsync("child-task", parent.Uid, "child.ics");
        await fixture.Store.MoveTaskAsync(parent.Uid, fixture.Destination.Id, cancellationToken: ct);
        parent.Summary = "Concurrent source parent";
        fixture.Server.Seed(fixture.SourceHref, fixture.Serialize(fixture.Source, parent));
        SyncRunResult result = await fixture.SyncAsync();
        Assert.Equal(SyncRunStatus.AttentionRequired, result.Status);
        SyncConflict conflict = Assert.Single(result.Conflicts, value => value.LocalTask.Uid == parent.Uid);
        await fixture.ResolveAsync(conflict, choice);
        Assert.Equal(SyncRunStatus.Success, (await fixture.SyncAsync()).Status);
        Assert.Equal(SyncRunStatus.Success, (await fixture.SyncAsync()).Status);
        TodoTask storedParent = (await fixture.Store.GetTaskForSyncAsync(parent.Uid, ct))!;
        TodoTask storedChild = (await fixture.Store.GetTaskForSyncAsync(child.Uid, ct))!;
        Assert.Equal(storedParent.ListId, storedChild.ListId);
        Assert.Equal(parent.Uid, storedChild.ParentUid);
        IReadOnlyList<TodoTask> source = fixture.Server.Tasks(fixture.Source.RemoteUrl!);
        IReadOnlyList<TodoTask> destination = fixture.Server.Tasks(fixture.Destination.RemoteUrl!);
        if (choice == SyncConflictChoice.KeepTheirs)
        {
            Assert.Equal(fixture.Source.Id, storedParent.ListId);
            Assert.Empty(destination);
            Assert.Equal(2, source.Count);
            Assert.Equal(parent.Uid, Assert.Single(source, value => value.Uid == child.Uid).ParentUid);
        }
        else
        {
            Assert.Equal(fixture.Destination.Id, storedParent.ListId);
            Assert.Equal(2, destination.Count);
            Assert.Equal(parent.Uid, Assert.Single(destination, value => value.Uid == child.Uid).ParentUid);
            if (choice == SyncConflictChoice.KeepMine) Assert.Empty(source);
            else
            {
                Assert.Equal(2, source.Count);
                TodoTask copiedParent = Assert.Single(source, value => value.ParentUid is null);
                Assert.NotEqual(parent.Uid, copiedParent.Uid);
                Assert.Equal("Concurrent source parent", copiedParent.Summary);
                Assert.Equal(copiedParent.Uid, Assert.Single(source, value => value.ParentUid is not null).ParentUid);
            }
        }
    }
}
