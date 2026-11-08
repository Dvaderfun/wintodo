using Wintodo.Core.Models;
using Wintodo.Core.Services;

namespace Wintodo.Data.Tests;

public sealed partial class TaskMoveStoreTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ParentAndSteps_MoveOnceAndHaveSubtreeGroups()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        (TodoList source, TodoList target, Account account) = await ListsAsync(fixture.Store);
        TodoTask root = await SyncedTaskAsync(fixture.Store, source, account, "Root");
        TodoTask child = await fixture.Store.AddStepAsync(root.Uid, "Step", Token);
        await AcknowledgeAsync(fixture.Store, child, source, account);
        TodoTask unrelated = await SyncedTaskAsync(fixture.Store, source, account, "Unrelated");
        int childSequence = (await fixture.Store.GetTaskAsync(child.Uid, Token))!.Sequence;
        int notifications = 0;
        fixture.Store.StoreChanged += (_, _) => notifications++;
        await fixture.Store.ApplyTaskBatchAsync([child.Uid, root.Uid, unrelated.Uid], TaskBatchAction.Move,
            Token, destinationListId: target.Id);
        IReadOnlyList<TaskMoveIntent> moves = await fixture.Store.GetPendingTaskMovesAsync(cancellationToken: Token);
        Assert.Equal(3, moves.Count);
        Assert.Equal(1, notifications);
        TodoTask movedChild = (await fixture.Store.GetTaskAsync(child.Uid, Token))!;
        Assert.Equal(target.Id, movedChild.ListId);
        Assert.Equal(root.Uid, movedChild.ParentUid);
        Assert.Equal(childSequence + 1, movedChild.Sequence);
        Assert.Equal(moves.Single(move => move.TaskUid == root.Uid).GroupId,
            moves.Single(move => move.TaskUid == child.Uid).GroupId);
        Assert.NotEqual(moves.Single(move => move.TaskUid == root.Uid).GroupId,
            moves.Single(move => move.TaskUid == unrelated.Uid).GroupId);
        Assert.All(moves, move => Assert.Equal(account.Id, move.SourceAccountId));
        Assert.Empty(await fixture.Store.GetPendingSyncChangesAsync(account.Id, Token));
    }

    [Fact]
    public async Task LocalMove_HasNoRemoteIntentAndPromotesAnIndependentStep()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        TodoList source = await fixture.Store.CreateListAsync("Source", cancellationToken: Token);
        TodoList target = await fixture.Store.CreateListAsync("Target", cancellationToken: Token);
        TodoTask root = await fixture.Store.CreateTaskAsync(new TodoTask { ListId = source.Id, Summary = "Root" }, Token);
        TodoTask child = await fixture.Store.AddStepAsync(root.Uid, "Step", Token);
        await fixture.Store.MoveTaskAsync(child.Uid, target.Id, cancellationToken: Token);
        Assert.Null((await fixture.Store.GetTaskAsync(child.Uid, Token))!.ParentUid);
        Assert.Empty(await fixture.Store.GetPendingTaskMovesAsync(cancellationToken: Token));
    }

    [Fact]
    public async Task UndoBeforeAttempt_CancelsIntentAndRetainsSourceBinding()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        (TodoList source, TodoList target, Account account) = await ListsAsync(fixture.Store);
        TodoTask task = await SyncedTaskAsync(fixture.Store, source, account, "Task");
        IReadOnlyList<TaskUndoState> undo = await fixture.Store.CaptureTaskUndoAsync([task.Uid], TaskUndoFields.List, Token);
        await fixture.Store.MoveTaskAsync(task.Uid, target.Id, cancellationToken: Token);
        await fixture.Store.RestoreTaskFieldsAsync(undo, Token);
        TodoTask restored = (await fixture.Store.GetTaskAsync(task.Uid, Token))!;
        Assert.Equal(source.Id, restored.ListId);
        Assert.Equal(task.RemoteHref, restored.RemoteHref);
        Assert.Equal(task.RemoteETag, restored.RemoteETag);
        Assert.Empty(await fixture.Store.GetPendingTaskMovesAsync(cancellationToken: Token));
        Assert.True(restored.IsDirty);
    }

    [Fact]
    public async Task GroupPreparation_RechecksAllChildrenBeforeSavingAnyAttempt()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        (TodoList source, TodoList target, Account account) = await ListsAsync(fixture.Store);
        TodoTask root = await SyncedTaskAsync(fixture.Store, source, account, "Root");
        TodoTask child = await fixture.Store.AddStepAsync(root.Uid, "Step", Token);
        await AcknowledgeAsync(fixture.Store, child, source, account);
        await fixture.Store.MoveTaskAsync(root.Uid, target.Id, cancellationToken: Token);
        IReadOnlyList<TaskMoveIntent> moves = await fixture.Store.GetPendingTaskMovesAsync(cancellationToken: Token);
        var attempts = new List<TaskMoveAttempt>();
        foreach (TaskMoveIntent move in moves)
        {
            TodoTask current = (await fixture.Store.GetTaskAsync(move.TaskUid, Token))!;
            attempts.Add(new TaskMoveAttempt { MoveId = move.Id, ExpectedSequence = current.Sequence,
                DestinationHref = target.RemoteUrl + move.TaskUid + ".ics", Payload = [1, 2, 3] });
        }
        TodoTask edited = (await fixture.Store.GetTaskAsync(child.Uid, Token))!;
        edited.Summary = "Newer step";
        await fixture.Store.UpdateTaskAsync(edited, Token);
        Assert.Null(await fixture.Store.PrepareTaskMoveAttemptsAsync(attempts, Token));
        Assert.All(await fixture.Store.GetPendingTaskMovesAsync(cancellationToken: Token),
            move => Assert.Null(move.AttemptedPayload));
        attempts.Single(attempt => attempt.MoveId == moves.Single(move => move.TaskUid == child.Uid).Id).ExpectedSequence
            = (await fixture.Store.GetTaskAsync(child.Uid, Token))!.Sequence;
        Assert.Equal(2, (await fixture.Store.PrepareTaskMoveAttemptsAsync(attempts, Token))!.Count);
    }

    [Fact]
    public async Task Completion_PreservesNewerEditsAndTheirChangeBoundary()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        (TodoList source, TodoList target, Account account) = await ListsAsync(fixture.Store);
        TodoTask task = await SyncedTaskAsync(fixture.Store, source, account, "Task");
        await fixture.Store.MoveTaskAsync(task.Uid, target.Id, cancellationToken: Token);
        TaskMoveIntent move = (await fixture.Store.GetPendingTaskMovesAsync(cancellationToken: Token)).Single();
        TodoTask moved = (await fixture.Store.GetTaskAsync(task.Uid, Token))!;
        move = (await fixture.Store.PrepareTaskMoveAttemptAsync(move.Id, moved.Sequence,
            target.RemoteUrl + task.Uid + ".ics", [1, 2], Token))!;
        moved.Summary = "Newer local title";
        await fixture.Store.UpdateTaskAsync(moved, Token);
        await FinishAsync(fixture.Store, move);
        TodoTask after = (await fixture.Store.GetTaskAsync(task.Uid, Token))!;
        Assert.Equal("Newer local title", after.Summary);
        Assert.True(after.IsDirty);
        Assert.Equal(target.RemoteUrl + task.Uid + ".ics", after.RemoteHref);
        Assert.Equal("\"destination\"", after.RemoteETag);
        Assert.Single(await fixture.Store.GetPendingSyncChangesAsync(account.Id, Token));
    }

    [Fact]
    public async Task AttemptsAreImmutable_AndStalePhaseUpdatesCannotCommit()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        (TodoList source, TodoList target, Account account) = await ListsAsync(fixture.Store);
        TodoTask task = await SyncedTaskAsync(fixture.Store, source, account, "Task");
        await fixture.Store.MoveTaskAsync(task.Uid, target.Id, cancellationToken: Token);
        TaskMoveIntent move = (await fixture.Store.GetPendingTaskMovesAsync(cancellationToken: Token)).Single();
        move = (await fixture.Store.PrepareTaskMoveAttemptAsync(move.Id,
            (await fixture.Store.GetTaskAsync(task.Uid, Token))!.Sequence, target.RemoteUrl + "task.ics", [1, 2], Token))!;
        int version = move.Version;
        move.AttemptedPayload = [3];
        Assert.False(await fixture.Store.UpdateTaskMoveAsync(move, version, Token));
        move = (await fixture.Store.GetTaskMoveAsync(move.Id, Token))!;
        move.Phase = TaskMovePhase.SourceRemoved;
        Assert.False(await fixture.Store.UpdateTaskMoveAsync(move, version, Token));
        move = (await fixture.Store.GetTaskMoveAsync(move.Id, Token))!;
        move.Phase = TaskMovePhase.DestinationConfirmed;
        move.DestinationETag = "\"new\"";
        Assert.True(await fixture.Store.UpdateTaskMoveAsync(move, version, Token));
        Assert.False(await fixture.Store.UpdateTaskMoveAsync(move, version, Token));
    }

    [Fact]
    public async Task RepeatedAttemptedMove_QueuesSuccessorWithConfirmedSourceBinding()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        (TodoList source, TodoList target, Account account) = await ListsAsync(fixture.Store);
        TodoList later = await fixture.Store.CreateListAsync("Later", cancellationToken: Token);
        await fixture.Store.SetListSyncMetadataAsync(later.Id, account.Id, "https://sync.example.test/later/", null, null,
            cancellationToken: Token);
        TodoTask task = await SyncedTaskAsync(fixture.Store, source, account, "Task");
        await fixture.Store.MoveTaskAsync(task.Uid, target.Id, cancellationToken: Token);
        TaskMoveIntent first = (await fixture.Store.GetPendingTaskMovesAsync(cancellationToken: Token)).Single();
        first = (await fixture.Store.PrepareTaskMoveAttemptAsync(first.Id,
            (await fixture.Store.GetTaskAsync(task.Uid, Token))!.Sequence, target.RemoteUrl + "task.ics", [1], Token))!;
        await fixture.Store.MoveTaskAsync(task.Uid, later.Id, cancellationToken: Token);
        Assert.Equal(2, (await fixture.Store.GetPendingTaskMovesAsync(cancellationToken: Token)).Count);
        await FinishAsync(fixture.Store, first);
        TaskMoveIntent successor = (await fixture.Store.GetPendingTaskMovesAsync(cancellationToken: Token)).Single();
        Assert.Null(successor.PredecessorId);
        Assert.Equal(target.RemoteUrl + "task.ics", successor.SourceHref);
        Assert.Equal("\"destination\"", successor.SourceETag);
        Assert.Equal(later.Id, (await fixture.Store.GetTaskAsync(task.Uid, Token))!.ListId);
    }

    [Fact]
    public async Task PendingObligations_BlockAccountIdentityAndListDeletion()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        (TodoList source, TodoList target, Account account) = await ListsAsync(fixture.Store);
        TodoTask task = await SyncedTaskAsync(fixture.Store, source, account, "Task");
        await fixture.Store.MoveTaskAsync(task.Uid, target.Id, cancellationToken: Token);
        await Assert.ThrowsAsync<PendingTaskMoveException>(() => fixture.Store.DeleteAccountAsync(account.Id, Token));
        await Assert.ThrowsAsync<PendingTaskMoveException>(() => fixture.Store.DeleteListAsync(source.Id, Token));
        account.ServerUrl = "https://other.example.test/";
        await Assert.ThrowsAsync<PendingTaskMoveException>(() => fixture.Store.SaveAccountAsync(account, Token));
        Assert.Single(await fixture.Store.GetPendingTaskMovesAsync(cancellationToken: Token));
    }

    [Fact]
    public async Task OldSourcePullAndPushAcknowledgement_CannotEraseMovedTask()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        (TodoList source, TodoList target, Account account) = await ListsAsync(fixture.Store);
        TodoTask task = await SyncedTaskAsync(fixture.Store, source, account, "Task");
        IReadOnlyList<RemoteTaskState> observed = await fixture.Store.GetRemoteTaskStatesAsync(source.Id, Token);
        await fixture.Store.MoveTaskAsync(task.Uid, target.Id, cancellationToken: Token);
        await fixture.Store.MarkTaskChangeSyncedAsync(long.MaxValue, task.Uid, task.RemoteHref, "\"stale\"", Token);
        RemotePullApplyResult result = await fixture.Store.ApplyRemotePullAsync(source.Id,
            [new RemoteTaskUpdate { Task = task, Href = task.RemoteHref!, ETag = "\"stale\"" }],
            [task.RemoteHref!], observed, Token);
        Assert.True(result.HasDeferredChanges);
        TodoTask after = (await fixture.Store.GetTaskAsync(task.Uid, Token))!;
        Assert.Equal(target.Id, after.ListId);
        Assert.True(after.IsDirty);
        Assert.Null(after.RemoteHref);
        Assert.Single(await fixture.Store.GetPendingTaskMovesAsync(cancellationToken: Token));
    }

    private static async Task<(TodoList Source, TodoList Target, Account Account)> ListsAsync(Wintodo.Data.SqliteTaskStore store)
    {
        Account account = await store.SaveAccountAsync(new Account { DisplayName = "Synthetic account",
            ServerUrl = "https://sync.example.test/", Username = "synthetic", CredentialKey = "synthetic" }, Token);
        TodoList source = await store.CreateListAsync("Source", cancellationToken: Token);
        TodoList target = await store.CreateListAsync("Target", cancellationToken: Token);
        source.RemoteUrl = "https://sync.example.test/source/";
        target.RemoteUrl = "https://sync.example.test/target/";
        foreach (TodoList list in new[] { source, target })
            await store.SetListSyncMetadataAsync(list.Id, account.Id, list.RemoteUrl, null, null,
                markListChangesSynced: true, cancellationToken: Token);
        return (source, target, account);
    }

    private static async Task<TodoTask> SyncedTaskAsync(Wintodo.Data.SqliteTaskStore store, TodoList list, Account account, string title)
    {
        TodoTask task = await store.CreateTaskAsync(new TodoTask { ListId = list.Id, Summary = title }, Token);
        await AcknowledgeAsync(store, task, list, account);
        return (await store.GetTaskAsync(task.Uid, Token))!;
    }

    private static async Task AcknowledgeAsync(Wintodo.Data.SqliteTaskStore store, TodoTask task, TodoList list, Account account)
    {
        SyncChange change = (await store.GetPendingSyncChangesAsync(account.Id, Token)).Single(change => change.EntityUid == task.Uid);
        await store.MarkTaskChangeSyncedAsync(change.Id, task.Uid, list.RemoteUrl + task.Uid + ".ics", "\"source\"", Token);
    }

    private static async Task FinishAsync(Wintodo.Data.SqliteTaskStore store, TaskMoveIntent move)
    {
        move.Phase = TaskMovePhase.DestinationConfirmed;
        move.DestinationETag = "\"destination\"";
        Assert.True(await store.UpdateTaskMoveAsync(move, move.Version, Token));
        move.Phase = TaskMovePhase.SourceRemoved;
        Assert.True(await store.UpdateTaskMoveAsync(move, move.Version, Token));
        Assert.True(await store.CompleteTaskMoveAsync(move.Id, move.Version, Token));
    }
}
