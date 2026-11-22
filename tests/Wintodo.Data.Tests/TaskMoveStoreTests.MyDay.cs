using Wintodo.Core.Models;
using Wintodo.Core.Presentation;
using Wintodo.Core.Services;

namespace Wintodo.Data.Tests;

public sealed partial class TaskMoveStoreTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemoteReplacementPreservesDismissalMadeAfterRemoteStateWasObserved(bool crossListMove)
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        (TodoList source, TodoList target, Account account) = await ListsAsync(fixture.Store);
        TodoTask observed = await SyncedDueTodayAsync(fixture, source, account);
        RemoteTaskState remoteState = Assert.Single(await fixture.Store.GetRemoteTaskStatesAsync(source.Id, Token));
        await fixture.Store.ApplyTaskBatchAsync([observed.Uid], TaskBatchAction.RemoveFromMyDay, Token);
        var incoming = new TodoTask
        {
            Uid = observed.Uid, Summary = "Renamed on server", Due = observed.Due,
            DueIsDateOnly = true, Sequence = observed.Sequence + 1,
        };
        TodoList destination = crossListMove ? target : source;
        string href = destination.RemoteUrl + incoming.Uid + ".ics";
        if (crossListMove)
        {
            var result = await fixture.Store.ApplyRemoteTaskMoveAsync(destination.Id,
                new RemoteTaskUpdate { Task = incoming, Href = href, ETag = "\"remote\"" }, remoteState, Token);
            Assert.Equal(1, result.AppliedCount);
        }
        else
        {
            Assert.True(await fixture.Store.ResolveRemoteConflictAsync(new SyncConflict
            {
                LocalTask = observed, RemoteTask = incoming, ListId = destination.Id,
                RemoteHref = href, RemoteETag = "\"remote\"",
            }, SyncConflictChoice.KeepTheirs, Token));
        }

        TodoTask stored = (await fixture.Store.GetTaskAsync(observed.Uid, Token))!;
        Assert.Equal(destination.Id, stored.ListId);
        Assert.Equal(incoming.Summary, stored.Summary);
        Assert.Equal(fixture.Clock.Today, stored.MyDayExcludedDate);
        Assert.False(TaskDateLabels.IsInMyDay(stored, fixture.Clock));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DismissalAndUndoOnlyQueuePushIfTodayPinChanges(bool manuallyPinned)
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        (TodoList source, _, Account account) = await ListsAsync(fixture.Store);
        TodoTask task = await SyncedDueTodayAsync(fixture, source, account, manuallyPinned);
        var notifications = new List<StoreChangedEventArgs>();
        fixture.Store.StoreChanged += (_, args) => notifications.Add(args);
        var undo = await fixture.Store.CaptureTaskUndoAsync([task.Uid], TaskUndoFields.MyDay, Token);
        fixture.Clock.UtcNow = fixture.Clock.UtcNow.AddMinutes(1);
        await fixture.Store.ApplyTaskBatchAsync([task.Uid], TaskBatchAction.RemoveFromMyDay, Token);
        await AssertMembershipAsync(inMyDay: false, syncSteps: manuallyPinned ? 1 : 0);
        await fixture.Store.RestoreTaskFieldsAsync(undo, Token);
        await AssertMembershipAsync(inMyDay: true, syncSteps: manuallyPinned ? 2 : 0);
        Assert.Equal(2, notifications.Count);
        Assert.All(notifications, change => Assert.Equal(task.Uid, Assert.Single(change.TaskUids)));

        async Task AssertMembershipAsync(bool inMyDay, int syncSteps)
        {
            TodoTask current = (await fixture.Store.GetTaskAsync(task.Uid, Token))!;
            Assert.Equal(inMyDay, TaskDateLabels.IsInMyDay(current, fixture.Clock));
            Assert.Equal(task.Sequence + syncSteps, current.Sequence);
            Assert.Equal(syncSteps > 0, current.IsDirty);
            if (syncSteps == 0) Assert.Equal(task.LastModified, current.LastModified);
            var pending = await fixture.Store.GetPendingSyncChangesAsync(account.Id, Token);
            if (syncSteps == 0) Assert.Empty(pending);
            else Assert.Contains(pending, change => change.EntityUid == task.Uid);
        }
    }

    private static async Task<TodoTask> SyncedDueTodayAsync(StoreFixture fixture, TodoList list,
        Account account, bool manuallyPinned = false)
    {
        DateOnly today = fixture.Clock.Today;
        TodoTask task = await fixture.Store.CreateTaskAsync(new TodoTask
        {
            ListId = list.Id, Summary = "Due today", DueIsDateOnly = true,
            Due = new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            MyDayDate = manuallyPinned ? today : today.AddDays(-1),
        }, Token);
        await AcknowledgeAsync(fixture.Store, task, list, account);
        return (await fixture.Store.GetTaskAsync(task.Uid, Token))!;
    }
}
