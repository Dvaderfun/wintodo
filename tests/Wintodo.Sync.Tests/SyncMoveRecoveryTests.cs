using System.Net;
using Wintodo.Core.Models;
using Wintodo.Core.Services;

namespace Wintodo.Sync.Tests;

public sealed partial class SyncMoveRecoveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OfflineEditBeforeMove_DoesNotMistakeLocalSnapshotForUnchangedRemoteSource(bool editAlarm)
    {
        await using MoveTestFixture fixture = await MoveTestFixture.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        TodoTask task = await fixture.SeedAsync();
        if (editAlarm)
            await fixture.Store.SetAlarmAsync(task.Uid, new TaskAlarm
            {
                Uid = "offline-alarm", Action = AlarmAction.Display, Trigger = fixture.Clock.UtcNow.AddMinutes(5),
                Description = "Retained offline alarm",
            }, ct);
        else
        {
            task.Summary = "Offline edit before moving";
            await fixture.Store.UpdateTaskAsync(task, ct);
        }
        await fixture.Store.MoveTaskAsync(task.Uid, fixture.Destination.Id, cancellationToken: ct);
        await fixture.ReopenAsync();
        Assert.Equal(SyncRunStatus.Success, (await fixture.SyncAsync()).Status);
        Assert.Empty(fixture.Server.Tasks(fixture.Source.RemoteUrl!));
        TodoTask destination = Assert.Single(fixture.Server.Tasks(fixture.Destination.RemoteUrl!));
        Assert.Equal(task.Uid, destination.Uid);
        if (editAlarm)
        {
            TaskAlarm alarm = Assert.Single(destination.Alarms);
            Assert.Equal("offline-alarm", alarm.Uid);
            Assert.Equal("Retained offline alarm", alarm.Description);
        }
        else Assert.Equal("Offline edit before moving", destination.Summary);
    }

    [Fact]
    public async Task MinimalRemoteVtodo_MoveAuthorsRevisionAndPreservesUnknownSiblingRelation()
    {
        await using MoveTestFixture fixture = await MoveTestFixture.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        const string wire = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nBEGIN:VTODO\r\nUID:minimal-task\r\n"
            + "SUMMARY:Minimal original\r\nDTSTAMP:20261108T120000Z\r\n"
            + "RELATED-TO;RELTYPE=SIBLING:opaque-relative\r\nX-SYNTHETIC-UNKNOWN:retained\r\n"
            + "END:VTODO\r\nEND:VCALENDAR\r\n";
        byte[] source = System.Text.Encoding.UTF8.GetBytes(wire);
        TodoTask task = new Wintodo.Core.Ical.IcalReader(TimeZoneInfo.Utc).Read(source).Tasks.Single();
        string etag = fixture.Server.Seed(fixture.SourceHref, source);
        await fixture.Store.ApplyRemoteChangesAsync(fixture.Source.Id,
            [new RemoteTaskUpdate { Task = task, Href = fixture.SourceHref, ETag = etag }], [], ct);
        await fixture.Store.MoveTaskAsync(task.Uid, fixture.Destination.Id, cancellationToken: ct);
        TodoTask moved = (await fixture.Store.GetTaskForSyncAsync(task.Uid, ct))!;
        Assert.Equal(SyncRunStatus.Success, (await fixture.SyncAsync()).Status);
        Assert.False(fixture.Server.Contains(fixture.SourceHref));
        TodoTask destination = Assert.Single(fixture.Server.Tasks(fixture.Destination.RemoteUrl!));
        Assert.Equal(moved.Sequence, destination.Sequence);
        Assert.True(destination.Sequence > task.Sequence);
        MoveServerRequest write = Assert.Single(fixture.Server.Requests, request => request.Method == "PUT");
        string payload = System.Text.Encoding.UTF8.GetString(write.Payload!);
        Assert.Contains("SEQUENCE:", payload, StringComparison.Ordinal);
        Assert.Contains("X-APPLE-SORT-ORDER:", payload, StringComparison.Ordinal);
        Assert.Contains("RELATED-TO;RELTYPE=SIBLING:opaque-relative", payload, StringComparison.Ordinal);
        Assert.Contains("X-SYNTHETIC-UNKNOWN:retained", payload, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task SyncedDestinationWithoutRemoteUrl_CreatesCollectionAndRetainsMoveAcrossCreationFailure(
        bool failOnce, bool acknowledgeBeforeCreation)
    {
        await using MoveTestFixture fixture = await MoveTestFixture.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        TodoTask task = await fixture.SeedAsync();
        TodoList destination = await fixture.Store.CreateListAsync("Prospective synced collection", cancellationToken: ct);
        await fixture.Store.SetListSyncMetadataAsync(destination.Id, fixture.SourceAccount.Id,
            null, null, null, acknowledgeBeforeCreation, ct);
        await fixture.Store.MoveTaskAsync(task.Uid, destination.Id, cancellationToken: ct);
        if (failOnce)
        {
            fixture.Server.BeforeRequest = (request, _) => request.Method == "MKCALENDAR"
                ? throw new HttpRequestException("Synthetic collection creation failure") : Task.CompletedTask;
            AssertPendingNetworkFault((await fixture.SyncAsync()).Status);
            Assert.Contains(fixture.Server.Requests, request => request.Method == "MKCALENDAR");
            Assert.True(fixture.Server.Contains(fixture.SourceHref));
            Assert.Contains(await fixture.Store.GetPendingTaskMovesAsync(cancellationToken: ct), move => move.TaskUid == task.Uid);
            fixture.Server.BeforeRequest = null;
            await fixture.ReopenAsync();
        }
        Assert.Equal(SyncRunStatus.Success, (await fixture.SyncAsync()).Status);
        Assert.False(fixture.Server.Contains(fixture.SourceHref));
        string remoteUrl = fixture.SourceAccount.HomeSetUrl + Uri.EscapeDataString(destination.Uid) + "/";
        Assert.Equal(task.Uid, Assert.Single(fixture.Server.Tasks(remoteUrl)).Uid);
        Assert.Contains(fixture.Server.Requests, request => request.Method == "MKCALENDAR" && request.Url == remoteUrl);
        Assert.Equal(destination.Id, (await fixture.Store.GetTaskForSyncAsync(task.Uid, ct))!.ListId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SingleAndBulkMoves_ConfirmDestinationBeforeRemovingSource(bool bulk)
    {
        await using MoveTestFixture fixture = await MoveTestFixture.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        TodoTask task = await fixture.SeedAsync();
        string? sourceETag = task.RemoteETag;
        if (bulk) await fixture.Store.ApplyTaskBatchAsync([task.Uid], TaskBatchAction.Move, ct, destinationListId: fixture.Destination.Id);
        else await fixture.Store.MoveTaskAsync(task.Uid, fixture.Destination.Id, cancellationToken: ct);

        Assert.Equal(SyncRunStatus.Success, (await fixture.SyncAsync()).Status);
        Assert.Equal(SyncRunStatus.Success, (await fixture.SyncAsync()).Status);
        Assert.Empty(fixture.Server.Tasks(fixture.Source.RemoteUrl!));
        TodoTask destination = Assert.Single(fixture.Server.Tasks(fixture.Destination.RemoteUrl!));
        Assert.Equal(task.Uid, destination.Uid);
        Assert.Equal(task.Description, destination.Description);
        Assert.Equal(task.Categories, destination.Categories);
        Assert.Contains("X-SYNTHETIC-PRESERVE", destination.IcalUnknown, StringComparison.Ordinal);
        MoveServerRequest create = fixture.Server.Requests.First(request => request.Method == "PUT");
        MoveServerRequest remove = fixture.Server.Requests.First(request => request.Method == "DELETE");
        Assert.StartsWith(fixture.Destination.RemoteUrl!, create.Url, StringComparison.Ordinal);
        Assert.Equal("*", create.IfNoneMatch);
        Assert.Equal(fixture.SourceHref, remove.Url);
        Assert.Equal(sourceETag, remove.IfMatch);
        Assert.True(fixture.Server.Requests.IndexOf(create) < fixture.Server.Requests.IndexOf(remove));
        Assert.Equal(fixture.Destination.Id, (await fixture.Store.GetTaskForSyncAsync(task.Uid, ct))!.ListId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LocalOrCrossAccountDestination_RetainsSourceCleanupAcrossRestart(bool crossAccount)
    {
        await using MoveTestFixture fixture = await MoveTestFixture.CreateAsync(crossAccount);
        var ct = TestContext.Current.CancellationToken;
        TodoTask task = await fixture.SeedAsync();
        TodoList target = crossAccount ? fixture.Destination
            : await fixture.Store.CreateListAsync("Local destination", cancellationToken: ct);
        await fixture.Store.MoveTaskAsync(task.Uid, target.Id, cancellationToken: ct);
        await fixture.ReopenAsync();

        Assert.Equal(SyncRunStatus.Success, (await fixture.SyncAsync()).Status);
        Assert.False(fixture.Server.Contains(fixture.SourceHref));
        Assert.Equal(target.Id, (await fixture.Store.GetTaskForSyncAsync(task.Uid, ct))!.ListId);
        if (crossAccount) Assert.Equal(task.Uid, Assert.Single(fixture.Server.Tasks(target.RemoteUrl!)).Uid);
        Assert.Contains(fixture.Server.Requests, request => request.Method == "DELETE" && request.Url == fixture.SourceHref);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ParentMove_CarriesStepsAndBulkSelectionVisitsEachTaskOnce(bool bulk)
    {
        await using MoveTestFixture fixture = await MoveTestFixture.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        TodoTask parent = await fixture.SeedAsync();
        TodoTask child = await fixture.SeedAsync("child-task", parent.Uid, "child.ics");
        if (bulk) await fixture.Store.ApplyTaskBatchAsync([child.Uid, parent.Uid, child.Uid], TaskBatchAction.Move, ct,
            destinationListId: fixture.Destination.Id);
        else await fixture.Store.MoveTaskAsync(parent.Uid, fixture.Destination.Id, cancellationToken: ct);

        TodoTask storedChild = (await fixture.Store.GetTaskForSyncAsync(child.Uid, ct))!;
        Assert.Equal(fixture.Destination.Id, storedChild.ListId);
        Assert.Equal(parent.Uid, storedChild.ParentUid);
        Assert.Equal(child.Sequence + 1, storedChild.Sequence);
        Assert.Equal(SyncRunStatus.Success, (await fixture.SyncAsync()).Status);
        Assert.Empty(fixture.Server.Tasks(fixture.Source.RemoteUrl!));
        IReadOnlyList<TodoTask> destination = fixture.Server.Tasks(fixture.Destination.RemoteUrl!);
        Assert.Equal(2, destination.Count);
        Assert.Equal(parent.Uid, Assert.Single(destination, task => task.Uid == child.Uid).ParentUid);
    }

    [Fact]
    public async Task RestartAfterSyncedToLocalMove_RemovesRemoteSourceAndKeepsLocalContent()
    {
        await using MoveTestFixture fixture = await MoveTestFixture.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        TodoTask task = await fixture.SeedAsync();
        TodoList local = await fixture.Store.CreateListAsync("Local destination", cancellationToken: ct);
        await fixture.Store.MoveTaskAsync(task.Uid, local.Id, cancellationToken: ct);
        await fixture.ReopenAsync();
        Assert.Equal(SyncRunStatus.Success, (await fixture.SyncAsync()).Status);
        Assert.False(fixture.Server.Contains(fixture.SourceHref));
        TodoTask retained = (await fixture.Store.GetTaskForSyncAsync(task.Uid, ct))!;
        Assert.Equal(local.Id, retained.ListId);
        Assert.Equal(task.Summary, retained.Summary);
        Assert.Null(retained.RemoteHref);
    }

    [Fact]
    public async Task MovingOnlyAStep_PromotesItAndKeepsParentInSource()
    {
        await using MoveTestFixture fixture = await MoveTestFixture.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        TodoTask parent = await fixture.SeedAsync();
        TodoTask child = await fixture.SeedAsync("child-task", parent.Uid, "child.ics");
        await fixture.Store.MoveTaskAsync(child.Uid, fixture.Destination.Id, cancellationToken: ct);
        Assert.Equal(SyncRunStatus.Success, (await fixture.SyncAsync()).Status);
        Assert.Equal(parent.Uid, Assert.Single(fixture.Server.Tasks(fixture.Source.RemoteUrl!)).Uid);
        TodoTask moved = Assert.Single(fixture.Server.Tasks(fixture.Destination.RemoteUrl!));
        Assert.Equal(child.Uid, moved.Uid);
        Assert.Null(moved.ParentUid);
    }

    [Fact]
    public async Task LocalToSyncedMove_CreatesDestinationWithoutSourceDelete()
    {
        await using MoveTestFixture fixture = await MoveTestFixture.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        TodoList local = await fixture.Store.CreateListAsync("Local source", cancellationToken: ct);
        TodoTask task = await fixture.Store.CreateTaskAsync(new TodoTask { ListId = local.Id, Summary = "Local original" }, ct);
        await fixture.Store.MoveTaskAsync(task.Uid, fixture.Destination.Id, cancellationToken: ct);
        Assert.Equal(SyncRunStatus.Success, (await fixture.SyncAsync()).Status);
        Assert.Equal(task.Uid, Assert.Single(fixture.Server.Tasks(fixture.Destination.RemoteUrl!)).Uid);
        Assert.DoesNotContain(fixture.Server.Requests, request => request.Method == "DELETE");
        Assert.Equal("*", Assert.Single(fixture.Server.Requests, request => request.Method == "PUT").IfNoneMatch);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task LostResponseAfterCommittedRemoteWrite_RestartConvergesWithoutTaskLoss(string lostMethod)
    {
        await using MoveTestFixture fixture = await MoveTestFixture.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        TodoTask task = await fixture.SeedAsync();
        await fixture.Store.MoveTaskAsync(task.Uid, fixture.Destination.Id, cancellationToken: ct);
        bool lost = false;
        fixture.Server.AfterWrite = (request, _) =>
        {
            if (!lost && request.Method == lostMethod)
            {
                lost = true;
                throw new HttpRequestException("Synthetic lost response", null, HttpStatusCode.ServiceUnavailable);
            }
            return Task.CompletedTask;
        };
        AssertPendingNetworkFault((await fixture.SyncAsync()).Status);
        Assert.True(lost);
        Assert.Equal(task.Uid, Assert.Single(fixture.Server.Tasks(fixture.Destination.RemoteUrl!)).Uid);
        if (lostMethod == "PUT") Assert.True(fixture.Server.Contains(fixture.SourceHref));
        await fixture.ReopenAsync();
        fixture.Server.AfterWrite = null;
        Assert.Equal(SyncRunStatus.Success, (await fixture.SyncAsync()).Status);
        Assert.Equal(SyncRunStatus.Success, (await fixture.SyncAsync()).Status);
        Assert.Empty(fixture.Server.Tasks(fixture.Source.RemoteUrl!));
        Assert.Equal(task.Uid, Assert.Single(fixture.Server.Tasks(fixture.Destination.RemoteUrl!)).Uid);
        Assert.All(fixture.Server.Requests.Where(request => request.Method == "PUT"), request => Assert.Equal("*", request.IfNoneMatch));
        Assert.False((await fixture.Store.GetTaskForSyncAsync(task.Uid, ct))!.IsDirty);
    }

    [Fact]
    public async Task DestinationFailure_NeverDeletesSourceAndRetrySucceeds()
    {
        await using MoveTestFixture fixture = await MoveTestFixture.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        TodoTask task = await fixture.SeedAsync();
        await fixture.Store.MoveTaskAsync(task.Uid, fixture.Destination.Id, cancellationToken: ct);
        fixture.Server.BeforeRequest = (request, _) => request.Method == "PUT"
            ? throw new HttpRequestException("Synthetic destination unavailable") : Task.CompletedTask;
        AssertPendingNetworkFault((await fixture.SyncAsync()).Status);
        Assert.True(fixture.Server.Contains(fixture.SourceHref));
        Assert.Empty(fixture.Server.Tasks(fixture.Destination.RemoteUrl!));
        Assert.Contains(fixture.Server.Requests, request => request.Method == "PUT");
        Assert.DoesNotContain(fixture.Server.Requests, request => request.Method == "DELETE");
        await fixture.ReopenAsync();
        fixture.Server.BeforeRequest = null;
        Assert.Equal(SyncRunStatus.Success, (await fixture.SyncAsync()).Status);
        Assert.False(fixture.Server.Contains(fixture.SourceHref));
    }

    [Fact]
    public async Task AmbiguousCreateWithAlteredDestination_DoesNotAcceptSameUidAsProof()
    {
        await using MoveTestFixture fixture = await MoveTestFixture.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        TodoTask task = await fixture.SeedAsync();
        await fixture.Store.MoveTaskAsync(task.Uid, fixture.Destination.Id, cancellationToken: ct);
        string? attemptedHref = null;
        fixture.Server.AfterWrite = (request, _) =>
        {
            if (request.Method == "PUT")
            {
                attemptedHref = request.Url;
                throw new HttpRequestException("Synthetic lost create response");
            }
            return Task.CompletedTask;
        };
        AssertPendingNetworkFault((await fixture.SyncAsync()).Status);
        Assert.NotNull(attemptedHref);
        task.Summary = "Different remote version";
        task.Sequence += 20;
        fixture.Server.Seed(attemptedHref, fixture.Serialize(fixture.Destination, task));
        fixture.Server.AfterWrite = null;
        await fixture.ReopenAsync();
        Assert.Equal(SyncRunStatus.AttentionRequired, (await fixture.SyncAsync()).Status);
        Assert.True(fixture.Server.Contains(fixture.SourceHref));
        Assert.Equal("Different remote version", Assert.Single(fixture.Server.Tasks(fixture.Destination.RemoteUrl!)).Summary);
        Assert.DoesNotContain(fixture.Server.Requests, request => request.Method == "DELETE");
    }

    [Theory]
    [InlineData("category")]
    [InlineData("unknown")]
    [InlineData("description")]
    [InlineData("sort")]
    public async Task AmbiguousCreate_RequiresCompleteSemanticsEvenWhenUidAndSequenceMatch(string field)
    {
        await using MoveTestFixture fixture = await MoveTestFixture.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        TodoTask task = await fixture.SeedAsync();
        await fixture.Store.MoveTaskAsync(task.Uid, fixture.Destination.Id, cancellationToken: ct);
        string? attemptedHref = null;
        fixture.Server.AfterWrite = (request, _) =>
        {
            if (request.Method == "PUT")
            {
                attemptedHref = request.Url;
                throw new HttpRequestException("Synthetic lost create response");
            }
            return Task.CompletedTask;
        };
        AssertPendingNetworkFault((await fixture.SyncAsync()).Status);
        TodoTask altered = Assert.Single(fixture.Server.Tasks(fixture.Destination.RemoteUrl!));
        byte[] replacement;
        if (field is "category" or "sort")
        {
            // The writer preserves imported CATEGORIES and projects sort order.
            // Change the received bytes so serialization cannot undo the
            // simulated remote mutation.
            string content = System.Text.Encoding.UTF8.GetString(fixture.Server.Content(attemptedHref!));
            string original = field == "category" ? "CATEGORIES:retained-category\r\n" : "X-APPLE-SORT-ORDER:1\r\n";
            string changed = field == "category" ? "CATEGORIES:altered-category\r\n" : "X-APPLE-SORT-ORDER:100\r\n";
            Assert.Contains(original, content, StringComparison.Ordinal);
            replacement = System.Text.Encoding.UTF8.GetBytes(content.Replace(original, changed, StringComparison.Ordinal));
            if (field == "sort")
            {
                var reader = new Wintodo.Core.Ical.IcalReader(TimeZoneInfo.Utc);
                Assert.NotEqual(reader.Read(fixture.Server.Content(attemptedHref!)).Tasks.Single().SortOrder,
                    reader.Read(replacement).Tasks.Single().SortOrder);
            }
        }
        else
        {
            if (field == "unknown") altered.IcalUnknown = null;
            else altered.Description = "Different description with same identity";
            replacement = fixture.Serialize(fixture.Destination, altered);
        }
        fixture.Server.Seed(attemptedHref!, replacement);
        fixture.Server.AfterWrite = null;
        await fixture.ReopenAsync();
        Assert.Equal(SyncRunStatus.AttentionRequired, (await fixture.SyncAsync()).Status);
        Assert.True(fixture.Server.Contains(fixture.SourceHref));
        Assert.DoesNotContain(fixture.Server.Requests, request => request.Method == "DELETE");
    }

    [Theory]
    [InlineData(SyncConflictChoice.KeepMine)]
    [InlineData(SyncConflictChoice.KeepTheirs)]
    [InlineData(SyncConflictChoice.KeepBoth)]
    public async Task ChangedSource_ExplicitConflictChoicesPreserveChosenVersions(SyncConflictChoice choice)
    {
        await using MoveTestFixture fixture = await MoveTestFixture.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        TodoTask task = await fixture.SeedAsync();
        await fixture.Store.MoveTaskAsync(task.Uid, fixture.Destination.Id, cancellationToken: ct);
        task.Summary = "Concurrent source edit";
        task.Sequence += 2;
        fixture.Server.Seed(fixture.SourceHref, fixture.Serialize(fixture.Source, task));

        SyncRunResult result = await fixture.SyncAsync();
        Assert.Equal(SyncRunStatus.AttentionRequired, result.Status);
        SyncConflict conflict = Assert.Single(result.Conflicts);
        Assert.Equal("Concurrent source edit", conflict.RemoteTask!.Summary);
        Assert.True(fixture.Server.Contains(fixture.SourceHref));
        await fixture.ResolveAsync(conflict, choice);
        Assert.Equal(SyncRunStatus.Success, (await fixture.SyncAsync()).Status);
        Assert.Equal(SyncRunStatus.Success, (await fixture.SyncAsync()).Status);
        IReadOnlyList<TodoTask> source = fixture.Server.Tasks(fixture.Source.RemoteUrl!);
        IReadOnlyList<TodoTask> destination = fixture.Server.Tasks(fixture.Destination.RemoteUrl!);
        if (choice == SyncConflictChoice.KeepMine)
        {
            Assert.Empty(source);
            Assert.Equal("Original task", Assert.Single(destination).Summary);
        }
        else if (choice == SyncConflictChoice.KeepTheirs)
        {
            Assert.Empty(destination);
            Assert.Equal(task.Uid, Assert.Single(source).Uid);
            Assert.Equal("Concurrent source edit", source[0].Summary);
            Assert.Equal(fixture.Source.Id, (await fixture.Store.GetTaskForSyncAsync(task.Uid, ct))!.ListId);
        }
        else
        {
            Assert.Equal(task.Uid, Assert.Single(destination).Uid);
            Assert.Equal("Original task", destination[0].Summary);
            Assert.NotEqual(task.Uid, Assert.Single(source).Uid);
            Assert.Equal("Concurrent source edit", source[0].Summary);
        }
    }

    [Fact]
    public async Task MissingSourceETag_RetainsBothCopiesInsteadOfDeletingUnconditionally()
    {
        await using MoveTestFixture fixture = await MoveTestFixture.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        TodoTask task = await fixture.SeedAsync();
        await fixture.Store.SetTaskRemoteVersionAsync(task.Uid, fixture.SourceHref, null, ct);
        fixture.Server.OmitETags = true;
        await fixture.Store.MoveTaskAsync(task.Uid, fixture.Destination.Id, cancellationToken: ct);
        Assert.NotEqual(SyncRunStatus.Success, (await fixture.SyncAsync()).Status);
        Assert.True(fixture.Server.Contains(fixture.SourceHref));
        Assert.DoesNotContain(fixture.Server.Requests, request => request.Method == "DELETE" && request.IfMatch is null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NewerEditOrDeleteDuringDestinationCreate_SurvivesMoveAcknowledgement(bool delete)
    {
        await using MoveTestFixture fixture = await MoveTestFixture.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        TodoTask task = await fixture.SeedAsync();
        await fixture.Store.MoveTaskAsync(task.Uid, fixture.Destination.Id, cancellationToken: ct);
        bool changed = false;
        fixture.Server.AfterWrite = async (request, token) =>
        {
            if (changed || request.Method != "PUT") return;
            changed = true;
            if (delete) await fixture.Store.DeleteTaskAsync(task.Uid, token);
            else
            {
                TodoTask current = (await fixture.Store.GetTaskForSyncAsync(task.Uid, token))!;
                current.Summary = "Newer edit during remote create";
                await fixture.Store.UpdateTaskAsync(current, token);
            }
        };
        await fixture.SyncAsync();
        fixture.Server.AfterWrite = null;
        Assert.Equal(SyncRunStatus.Success, (await fixture.SyncAsync()).Status);
        TodoTask stored = (await fixture.Store.GetTaskForSyncAsync(task.Uid, ct))!;
        Assert.True(changed);
        Assert.Empty(fixture.Server.Tasks(fixture.Source.RemoteUrl!));
        if (delete)
        {
            Assert.True(stored.IsDeleted);
            Assert.Empty(fixture.Server.Tasks(fixture.Destination.RemoteUrl!));
        }
        else
        {
            Assert.Equal("Newer edit during remote create", stored.Summary);
            Assert.Equal(stored.Summary, Assert.Single(fixture.Server.Tasks(fixture.Destination.RemoteUrl!)).Summary);
        }
    }

    private static void AssertPendingNetworkFault(SyncRunStatus status) =>
        Assert.True(status is SyncRunStatus.Offline or SyncRunStatus.AttentionRequired,
            $"Expected an offline/pending result for the injected network failure, received {status}.");
}
