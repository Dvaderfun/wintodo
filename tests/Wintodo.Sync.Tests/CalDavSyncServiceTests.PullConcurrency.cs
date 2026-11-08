using Wintodo.Core.Models;
using Wintodo.Core.Ical;
using Wintodo.Core.Queries;
using Wintodo.Core.Services;
using Wintodo.Sync;

namespace Wintodo.Sync.Tests;

public sealed partial class CalDavSyncServiceTests
{
    public enum PullEdit
    {
        Fields,
        Collections,
        Delete,
        DeleteAndRestore,
        Move,
        EditAndAcknowledge,
        Step,
    }

    [Theory]
    [InlineData(false, PullEdit.Fields, SyncConflictChoice.KeepMine)]
    [InlineData(true, PullEdit.Fields, SyncConflictChoice.KeepMine)]
    [InlineData(false, PullEdit.Collections, SyncConflictChoice.KeepMine)]
    [InlineData(true, PullEdit.Collections, SyncConflictChoice.KeepMine)]
    [InlineData(false, PullEdit.Delete, SyncConflictChoice.KeepMine)]
    [InlineData(true, PullEdit.Delete, SyncConflictChoice.KeepMine)]
    [InlineData(false, PullEdit.DeleteAndRestore, SyncConflictChoice.KeepMine)]
    [InlineData(true, PullEdit.DeleteAndRestore, SyncConflictChoice.KeepMine)]
    [InlineData(false, PullEdit.Move, SyncConflictChoice.KeepMine)]
    [InlineData(true, PullEdit.Move, SyncConflictChoice.KeepMine)]
    [InlineData(false, PullEdit.EditAndAcknowledge, SyncConflictChoice.KeepMine)]
    [InlineData(true, PullEdit.EditAndAcknowledge, SyncConflictChoice.KeepMine)]
    [InlineData(false, PullEdit.EditAndAcknowledge, SyncConflictChoice.KeepTheirs)]
    [InlineData(true, PullEdit.EditAndAcknowledge, SyncConflictChoice.KeepTheirs)]
    [InlineData(false, PullEdit.EditAndAcknowledge, SyncConflictChoice.KeepBoth)]
    [InlineData(true, PullEdit.EditAndAcknowledge, SyncConflictChoice.KeepBoth)]
    [InlineData(true, PullEdit.Step, SyncConflictChoice.KeepMine)]
    [InlineData(true, PullEdit.Step, SyncConflictChoice.KeepTheirs)]
    [InlineData(true, PullEdit.Step, SyncConflictChoice.KeepBoth)]
    public async Task Pull_PreservesAnEditCommittedAfterTheCleanRead(
        bool remoteDeleted,
        PullEdit edit,
        SyncConflictChoice choice)
    {
        var ct = TestContext.Current.CancellationToken;
        using var server = new InMemoryCalDavHandler();
        await using SyncFixture first = await SyncFixture.CreateAsync();
        await using SyncFixture second = await SyncFixture.CreateAsync();
        (Account firstAccount, TodoList firstList) = await first.CreateMappedListAsync();
        (Account secondAccount, TodoList secondList) = await second.CreateMappedListAsync();
        using var firstHttp = new HttpClient(server, disposeHandler: false);
        using var secondHttp = new HttpClient(server, disposeHandler: false);
        using var firstSync = new CalDavSyncService(first.Store, new FakeCredentialStore(), first.Clock, firstHttp);
        using var secondSync = new CalDavSyncService(second.Store, new FakeCredentialStore(), second.Clock, secondHttp);

        TodoTask task = await first.Store.CreateTaskAsync(new TodoTask
        {
            ListId = firstList.Id,
            Summary = "Initial task",
        }, ct);
        TodoTask blocker = await first.Store.CreateTaskAsync(new TodoTask
        {
            ListId = firstList.Id,
            Summary = "Second resource",
        }, ct);
        Assert.Equal(SyncRunStatus.Success, (await firstSync.SyncAccountAsync(firstAccount, ct)).Status);
        Assert.Equal(SyncRunStatus.Success, (await secondSync.SyncAccountAsync(secondAccount, ct)).Status);
        TodoTask local = (await second.Store.GetTaskForSyncAsync(task.Uid, ct))!;
        string? originalToken = Assert.Single(await second.Store.GetSyncListsAsync(
            secondAccount.Id, cancellationToken: ct)).SyncToken;

        if (remoteDeleted)
        {
            await first.Store.DeleteTaskAsync(task.Uid, ct);
        }
        else
        {
            TodoTask changed = (await first.Store.GetTaskForSyncAsync(task.Uid, ct))!;
            changed.Summary = "Remote update";
            await first.Store.UpdateTaskAsync(changed, ct);
        }

        TodoTask changedBlocker = (await first.Store.GetTaskForSyncAsync(blocker.Uid, ct))!;
        changedBlocker.Summary = "Second remote update";
        await first.Store.UpdateTaskAsync(changedBlocker, ct);
        Assert.Equal(SyncRunStatus.Success, (await firstSync.SyncAccountAsync(firstAccount, ct)).Status);

        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.BeforeGetAsync = async (path, token) =>
        {
            if (path.EndsWith(blocker.Uid + ".ics", StringComparison.Ordinal))
            {
                reached.TrySetResult();
                await resume.Task.WaitAsync(token);
            }
        };
        server.Writes.Clear();
        Task<SyncRunResult> pulling = secondSync.SyncAccountAsync(secondAccount, ct);
        await reached.Task.WaitAsync(ct);
        try
        {
            await CommitPullEditAsync(second, secondAccount, local, edit, ct);
        }
        finally
        {
            resume.TrySetResult();
        }

        SyncRunResult result = await pulling;
        TodoTask committed = (await second.Store.GetTaskForSyncAsync(task.Uid, ct))!;
        Assert.Equal(SyncRunStatus.AttentionRequired, result.Status);
        SyncConflict? conflict = null;
        if (edit == PullEdit.Move)
        {
            Assert.Empty(result.Conflicts);
            TaskMoveIntent move = Assert.Single(await second.Store.GetPendingTaskMovesAsync(cancellationToken: ct));
            Assert.Equal(task.Uid, move.TaskUid);
            Assert.Equal(secondAccount.Id, move.SourceAccountId);
            Assert.Equal(secondList.Id, move.SourceListId);
            Assert.Equal(local.RemoteHref, move.SourceHref);
            Assert.Equal(local.RemoteETag, move.SourceETag);
            Assert.Equal(committed.ListId, move.DestinationListId);
            Assert.Equal(committed.Sequence, move.LocalSequence);
            Assert.Equal(TaskMovePhase.Planned, move.Phase);
        }
        else
        {
            conflict = Assert.Single(result.Conflicts);
            Assert.Equal(task.Uid, conflict.LocalTask.Uid);
            Assert.Equal(remoteDeleted, conflict.RemoteDeleted);
        }
        Assert.Equal(1, result.PulledCount);
        Assert.Equal(0, result.PushedCount);
        Assert.Empty(server.Writes);
        Assert.Equal(local.Sequence, committed.Sequence);
        Assert.Equal(local.Summary, committed.Summary);
        Assert.Equal(local.ListId, committed.ListId);
        Assert.Equal(edit == PullEdit.Delete, committed.IsDeleted);
        Assert.Equal(edit != PullEdit.EditAndAcknowledge && edit != PullEdit.Step, committed.IsDirty);
        if (conflict is not null)
        {
            Assert.Equal(committed.Sequence, conflict.LocalTask.Sequence);
            Assert.Equal(committed.IsDeleted, conflict.LocalTask.IsDeleted);
        }
        if (edit == PullEdit.Collections)
        {
            Assert.NotNull(conflict);
            Assert.Equal("Local category", Assert.Single(committed.Categories));
            Assert.Equal("Local reminder", Assert.Single(committed.Alarms).Description);
            Assert.Equal("local.txt", Assert.Single(committed.Attachments).FileName);
            Assert.Single(conflict.LocalTask.Categories);
            Assert.Single(conflict.LocalTask.Alarms);
            Assert.Single(conflict.LocalTask.Attachments);
        }

        IReadOnlyList<SyncChange> pending = await second.Store.GetPendingSyncChangesAsync(secondAccount.Id, ct);
        if (edit == PullEdit.EditAndAcknowledge)
        {
            Assert.DoesNotContain(pending, change => change.EntityUid == task.Uid);
        }
        else if (edit == PullEdit.Step)
        {
            SyncChange stepChange = Assert.Single(pending, change => change.Task?.ParentUid == task.Uid);
            Assert.Equal("Local step", stepChange.Task!.Summary);
            Assert.Equal("Local step", Assert.Single(await second.Store.GetChildrenAsync(task.Uid, ct)).Summary);
        }
        else if (edit != PullEdit.Move)
        {
            Assert.Contains(pending, change => change.EntityUid == task.Uid);
        }

        TodoList afterPull = Assert.Single(await second.Store.GetSyncListsAsync(secondAccount.Id, cancellationToken: ct));
        Assert.Equal(originalToken, afterPull.SyncToken);

        if (edit == PullEdit.Fields || edit == PullEdit.Collections
            || edit == PullEdit.DeleteAndRestore || edit == PullEdit.Step)
        {
            server.BeforeGetAsync = null;
            SyncRunResult retry = await secondSync.SyncAccountAsync(secondAccount, ct);
            Assert.Equal(SyncRunStatus.AttentionRequired, retry.Status);
            Assert.Single(retry.Conflicts);
            Assert.Equal(edit != PullEdit.Step, (await second.Store.GetTaskForSyncAsync(task.Uid, ct))!.IsDirty);
        }

        if (edit == PullEdit.Step || edit == PullEdit.EditAndAcknowledge)
        {
            Assert.NotNull(conflict);
            server.BeforeGetAsync = null;
            TodoTask? stepBefore = edit == PullEdit.Step
                ? Assert.Single(await second.Store.GetChildrenAsync(task.Uid, ct))
                : null;
            if (stepBefore is not null)
            {
                stepBefore = (await second.Store.GetTaskForSyncAsync(stepBefore.Uid, ct))!;
            }
            using var restarted = new CalDavSyncService(
                second.Store, new FakeCredentialStore(), second.Clock, secondHttp);
            await restarted.ResolveConflictAsync(conflict, choice, ct);
            Assert.Equal(SyncRunStatus.Success, (await restarted.SyncAccountAsync(secondAccount, ct)).Status);
            Assert.Equal(SyncRunStatus.Success, (await firstSync.SyncAccountAsync(firstAccount, ct)).Status);
            TodoTask afterChoice = (await second.Store.GetTaskForSyncAsync(task.Uid, ct))!;
            Assert.Equal(remoteDeleted && choice != SyncConflictChoice.KeepMine, afterChoice.IsDeleted);
            Assert.False(afterChoice.IsDirty);
            Assert.DoesNotContain(await second.Store.GetPendingSyncChangesAsync(secondAccount.Id, ct),
                change => change.EntityUid == task.Uid);
            if (choice == SyncConflictChoice.KeepMine)
            {
                Assert.False((await first.Store.GetTaskForSyncAsync(task.Uid, ct))!.IsDeleted);
                Assert.Equal(local.Summary, (await first.Store.GetTaskForSyncAsync(task.Uid, ct))!.Summary);
            }

            if (choice == SyncConflictChoice.KeepBoth)
            {
                TodoTask copy = Assert.Single(await second.Store.GetTasksAsync(new ListQuery
                {
                    Kind = ListQueryKind.UserList,
                    ListId = secondList.Id,
                    IncludeCompleted = true,
                }, ct), value => value.Uid != task.Uid && value.Summary == local.Summary);
                Assert.False(copy.IsDirty);
                Assert.NotNull(await first.Store.GetTaskForSyncAsync(copy.Uid, ct));
            }

            if (stepBefore is not null)
            {
                TodoTask storedStep = (await second.Store.GetTaskForSyncAsync(stepBefore.Uid, ct))!;
                if (choice == SyncConflictChoice.KeepTheirs)
                {
                    Assert.True(storedStep.IsDeleted);
                    Assert.False(storedStep.IsDirty);
                    Assert.Null(await first.Store.GetTaskForSyncAsync(stepBefore.Uid, ct));
                }
                else
                {
                    Assert.False(storedStep.IsDeleted);
                    Assert.False(storedStep.IsDirty);
                    Assert.Equal(choice == SyncConflictChoice.KeepMine, storedStep.ParentUid == task.Uid);
                    Assert.Equal(stepBefore.Categories, storedStep.Categories);
                    Assert.Equal(Assert.Single(stepBefore.Alarms).Description, Assert.Single(storedStep.Alarms).Description);
                    Assert.Equal(Assert.Single(stepBefore.Attachments).RelativePath, Assert.Single(storedStep.Attachments).RelativePath);
                    Assert.Equal(stepBefore.IcalUnknown, storedStep.IcalUnknown);
                    TodoTask remoteStep = (await first.Store.GetTaskForSyncAsync(stepBefore.Uid, ct))!;
                    Assert.Equal(storedStep.ParentUid, remoteStep.ParentUid);
                    Assert.Equal("Local step", remoteStep.Summary);
                }
            }
        }
    }

    private static async Task CommitPullEditAsync(
        SyncFixture fixture,
        Account account,
        TodoTask task,
        PullEdit edit,
        CancellationToken cancellationToken)
    {
        if (edit == PullEdit.Step)
        {
            TodoTask step = await fixture.Store.AddStepAsync(task.Uid, "Local step", cancellationToken);
            step.Categories.Add("Step category");
            step.Alarms.Add(new TaskAlarm
            {
                Uid = "step-reminder",
                Trigger = fixture.Clock.UtcNow.AddHours(1),
                Description = "Step reminder",
            });
            step.Attachments.Add(new TaskAttachment
            {
                FileName = "step.txt",
                RelativePath = "attachments/step.txt",
                SizeBytes = 5,
            });
            step.IcalUnknown = new IcalReader(TimeZoneInfo.Utc).Read($"""
                BEGIN:VCALENDAR
                VERSION:2.0
                BEGIN:VTODO
                UID:step-unknown
                SUMMARY:Local step
                RELATED-TO;RELTYPE=PARENT:{task.Uid}
                SEQUENCE:0
                X-STEP-CUSTOM:preserve
                END:VTODO
                END:VCALENDAR

                """).Tasks[0].IcalUnknown;
            await fixture.Store.UpdateTaskAsync(step, cancellationToken);
            return;
        }

        if (edit == PullEdit.Delete || edit == PullEdit.DeleteAndRestore)
        {
            await fixture.Store.DeleteTaskAsync(task.Uid, cancellationToken);
            if (edit == PullEdit.DeleteAndRestore)
            {
                await fixture.Store.RestoreTaskAsync(task.Uid, cancellationToken);
            }

            TodoTask changed = (await fixture.Store.GetTaskForSyncAsync(task.Uid, cancellationToken))!;
            task.Sequence = changed.Sequence;
            return;
        }

        if (edit == PullEdit.Move)
        {
            TodoList destination = await fixture.Store.CreateListAsync("Local destination", cancellationToken: cancellationToken);
            await fixture.Store.MoveTaskAsync(task.Uid, destination.Id, cancellationToken: cancellationToken);
            TodoTask moved = (await fixture.Store.GetTaskForSyncAsync(task.Uid, cancellationToken))!;
            task.ListId = moved.ListId;
            task.Sequence = moved.Sequence;
            return;
        }

        task.Summary = "Local update";
        if (edit == PullEdit.Collections)
        {
            task.Categories.Add("Local category");
            task.Alarms.Add(new TaskAlarm
            {
                Uid = "local-reminder",
                Trigger = fixture.Clock.UtcNow.AddHours(1),
                Description = "Local reminder",
            });
            task.Attachments.Add(new TaskAttachment
            {
                FileName = "local.txt",
                RelativePath = "attachments/local.txt",
                SizeBytes = 5,
            });
        }

        await fixture.Store.UpdateTaskAsync(task, cancellationToken);
        if (edit == PullEdit.EditAndAcknowledge)
        {
            SyncChange change = (await fixture.Store.GetPendingSyncChangesAsync(account.Id, cancellationToken))
                .Where(value => value.EntityUid == task.Uid).MaxBy(value => value.Id)!;
            await fixture.Store.MarkTaskChangeSyncedAsync(
                change.Id, task.Uid, task.RemoteHref, task.RemoteETag, cancellationToken);
        }
    }
}
