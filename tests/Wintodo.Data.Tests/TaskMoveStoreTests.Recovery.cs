using Wintodo.Core.Ical;
using Wintodo.Core.Models;

namespace Wintodo.Data.Tests;

public sealed partial class TaskMoveStoreTests
{
    [Fact]
    public async Task LocalSourceSnapshot_RetainsUidOriginalSortAndStepRelationship()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        (_, TodoList target, _) = await ListsAsync(fixture.Store);
        TodoList local = await fixture.Store.CreateListAsync("Local", cancellationToken: Token);
        TodoTask root = await fixture.Store.CreateTaskAsync(new TodoTask { ListId = local.Id, Summary = "Root" }, Token);
        TodoTask step = await fixture.Store.AddStepAsync(root.Uid, "Step", Token);
        await fixture.Store.ReorderTaskAsync(step.Uid, 13.5, Token);
        TodoTask original = (await fixture.Store.GetTaskAsync(step.Uid, Token))!;
        await fixture.Store.MoveTaskAsync(step.Uid, target.Id, cancellationToken: Token);
        TaskMoveIntent move = (await fixture.Store.GetPendingTaskMovesAsync(cancellationToken: Token)).Single();
        TodoTask snapshot = new IcalReader(fixture.Clock.LocalTimeZone).Read(move.SourcePayload!).Tasks.Single();
        Assert.Null(move.SourceAccountId);
        Assert.Equal(original.Uid, snapshot.Uid);
        Assert.Equal(original.ParentUid, snapshot.ParentUid);
        Assert.Equal(original.Sequence, snapshot.Sequence);
        Assert.Equal(original.SortOrder, snapshot.SortOrder);
    }

    [Fact]
    public async Task PlannedIndependentStepPromotion_DetachesItsConflictGroup()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        (TodoList source, TodoList target, Account account) = await ListsAsync(fixture.Store);
        TodoTask root = await SyncedTaskAsync(fixture.Store, source, account, "Root");
        TodoTask child = await fixture.Store.AddStepAsync(root.Uid, "Step", Token);
        await AcknowledgeAsync(fixture.Store, child, source, account);
        await fixture.Store.MoveTaskAsync(root.Uid, target.Id, cancellationToken: Token);
        TodoList independent = await fixture.Store.CreateListAsync("Independent", cancellationToken: Token);
        await fixture.Store.MoveTaskAsync(child.Uid, independent.Id, cancellationToken: Token);
        IReadOnlyList<TaskMoveIntent> moves = await fixture.Store.GetPendingTaskMovesAsync(cancellationToken: Token);
        Assert.NotEqual(moves.Single(move => move.TaskUid == root.Uid).GroupId,
            moves.Single(move => move.TaskUid == child.Uid).GroupId);
        Assert.Null((await fixture.Store.GetTaskAsync(child.Uid, Token))!.ParentUid);
    }

    [Fact]
    public async Task ProspectiveConfiguredDestination_RemainsEligibleAfterListAcknowledgement()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        (TodoList source, TodoList target, Account account) = await ListsAsync(fixture.Store);
        await fixture.Store.SetListSyncMetadataAsync(target.Id, account.Id, null, null, null,
            markListChangesSynced: true, cancellationToken: Token);
        TodoTask task = await SyncedTaskAsync(fixture.Store, source, account, "Task");
        await fixture.Store.MoveTaskAsync(task.Uid, target.Id, cancellationToken: Token);
        Assert.Equal(account.Id, (await fixture.Store.GetPendingTaskMovesAsync(cancellationToken: Token)).Single().DestinationAccountId);
        Assert.Contains(await fixture.Store.GetSyncListsAsync(account.Id, cancellationToken: Token), list => list.Id == target.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemoteMove_CannotReplaceKnownVersionWithOlderOrAmbiguousSameSequence(bool sameSequence)
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        (TodoList source, TodoList target, Account account) = await ListsAsync(fixture.Store);
        TodoTask task = await SyncedTaskAsync(fixture.Store, source, account, "Known version");
        task.Description = "Known notes";
        await fixture.Store.UpdateTaskAsync(task, Token);
        await AcknowledgeAsync(fixture.Store, task, source, account);
        task = (await fixture.Store.GetTaskAsync(task.Uid, Token))!;
        RemoteTaskState observed = (await fixture.Store.GetRemoteTaskStatesAsync(source.Id, Token)).Single();
        var incoming = new TodoTask { Uid = task.Uid, Summary = sameSequence ? "Unproven version" : task.Summary,
            Sequence = sameSequence ? task.Sequence : task.Sequence - 1, Created = task.Created,
            LastModified = task.LastModified, Description = task.Description, SortOrder = task.SortOrder };
        RemotePullApplyResult result = await fixture.Store.ApplyRemoteTaskMoveAsync(target.Id,
            new RemoteTaskUpdate { Task = incoming, Href = target.RemoteUrl + task.Uid + ".ics", ETag = "\"target\"" }, observed, Token);
        Assert.True(result.HasDeferredChanges);
        Assert.Single(result.Conflicts);
        Assert.Equal(source.Id, (await fixture.Store.GetTaskAsync(task.Uid, Token))!.ListId);
        Assert.Equal("Known version", (await fixture.Store.GetTaskAsync(task.Uid, Token))!.Summary);
    }

    [Fact]
    public async Task GroupFinalization_IsAtomicAndRejectsPartialOrStaleCompletions()
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
            attempts.Add(new TaskMoveAttempt { MoveId = move.Id,
                ExpectedSequence = (await fixture.Store.GetTaskAsync(move.TaskUid, Token))!.Sequence,
                DestinationHref = target.RemoteUrl + move.TaskUid + ".ics", Payload = [1] });
        moves = (await fixture.Store.PrepareTaskMoveAttemptsAsync(attempts, Token))!;
        foreach (TaskMoveIntent move in moves)
        {
            move.Phase = TaskMovePhase.DestinationConfirmed;
            move.DestinationETag = "\"target\"";
            Assert.True(await fixture.Store.UpdateTaskMoveAsync(move, move.Version, Token));
            move.Phase = TaskMovePhase.SourceRemoved;
            Assert.True(await fixture.Store.UpdateTaskMoveAsync(move, move.Version, Token));
        }
        Assert.False(await fixture.Store.CompleteTaskMoveAsync(moves[0].Id, moves[0].Version, Token));
        var updates = moves.Select(move => new TaskMoveUpdate { Intent = move, ExpectedVersion = move.Version }).ToArray();
        updates[1].ExpectedVersion--;
        Assert.False(await fixture.Store.CompleteTaskMoveGroupAsync(updates, Token));
        Assert.Equal(2, (await fixture.Store.GetPendingTaskMovesAsync(cancellationToken: Token)).Count);
        updates[1].ExpectedVersion++;
        int notifications = 0;
        fixture.Store.StoreChanged += (_, _) => notifications++;
        Assert.True(await fixture.Store.CompleteTaskMoveGroupAsync(updates, Token));
        Assert.Equal(1, notifications);
        Assert.Empty(await fixture.Store.GetPendingTaskMovesAsync(cancellationToken: Token));
    }

    [Fact]
    public async Task AcceptedKeepMine_PreservesLaterEditAndRecreatedSourceMustBeRemovedAgain()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        (TodoList source, TodoList target, Account account) = await ListsAsync(fixture.Store);
        TodoTask task = await SyncedTaskAsync(fixture.Store, source, account, "Task");
        await fixture.Store.MoveTaskAsync(task.Uid, target.Id, cancellationToken: Token);
        TaskMoveIntent move = (await fixture.Store.GetPendingTaskMovesAsync(cancellationToken: Token)).Single();
        TodoTask current = (await fixture.Store.GetTaskAsync(task.Uid, Token))!;
        move = (await fixture.Store.PrepareTaskMoveAttemptAsync(move.Id, current.Sequence,
            target.RemoteUrl + task.Uid + ".ics", [1], Token))!;
        move.ResolutionChoice = SyncConflictChoice.KeepMine;
        move.ResolutionSequence = current.Sequence;
        move.ResolutionListId = current.ListId;
        move.ResolutionSourcePayload = [2];
        move.ResolutionSourceETag = "\"source\"";
        Assert.True(await fixture.Store.UpdateTaskMoveAsync(move, move.Version, Token, current.Sequence));
        current.Summary = "Later edit";
        await fixture.Store.UpdateTaskAsync(current, Token);
        move.Phase = TaskMovePhase.DestinationConfirmed;
        move.DestinationETag = "\"target\"";
        Assert.True(await fixture.Store.UpdateTaskMoveAsync(move, move.Version, Token));
        move.Phase = TaskMovePhase.SourceRemoved;
        Assert.True(await fixture.Store.UpdateTaskMoveAsync(move, move.Version, Token));
        Assert.True(await fixture.Store.ConfirmTaskMoveSourceRestorationAsync(move.Id, move.Version, "\"recreated\"", Token));
        move = (await fixture.Store.GetTaskMoveAsync(move.Id, Token))!;
        Assert.False(await fixture.Store.CompleteTaskMoveAsync(move.Id, move.Version, Token));
        move.SourceRestored = false;
        Assert.True(await fixture.Store.UpdateTaskMoveAsync(move, move.Version, Token));
        Assert.True(await fixture.Store.CompleteTaskMoveAsync(move.Id, move.Version, Token));
        Assert.Equal("Later edit", (await fixture.Store.GetTaskAsync(task.Uid, Token))!.Summary);
        Assert.True((await fixture.Store.GetTaskAsync(task.Uid, Token))!.IsDirty);
    }

    [Fact]
    public async Task LegacyUnprovenSourceBinding_RemainsPendingBeforeAnyAttempt()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        (TodoList source, TodoList target, Account account) = await ListsAsync(fixture.Store);
        TodoTask task = await SyncedTaskAsync(fixture.Store, source, account, "Task");
        await fixture.Store.SetTaskRemoteVersionAsync(task.Uid, "https://sync.example.test/unmapped/task.ics",
            "\"source\"", Token);
        await fixture.Store.MoveTaskAsync(task.Uid, target.Id, cancellationToken: Token);
        TaskMoveIntent move = (await fixture.Store.GetPendingTaskMovesAsync(cancellationToken: Token)).Single();
        Assert.False(move.SourceBindingVerified);
        Assert.Null(await fixture.Store.PrepareTaskMoveAttemptAsync(move.Id,
            (await fixture.Store.GetTaskAsync(task.Uid, Token))!.Sequence, target.RemoteUrl + "task.ics", [1], Token));
        Assert.Single(await fixture.Store.GetPendingTaskMovesAsync(cancellationToken: Token));
    }

    [Fact]
    public async Task UnknownAccountlessBinding_IsRetainedAcrossLocalMoveAndUndo()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        TodoList source = await fixture.Store.CreateListAsync("Source", cancellationToken: Token);
        TodoList target = await fixture.Store.CreateListAsync("Target", cancellationToken: Token);
        TodoTask task = await fixture.Store.CreateTaskAsync(new TodoTask { ListId = source.Id, Summary = "Known" }, Token);
        await fixture.Store.SetTaskRemoteVersionAsync(task.Uid, "https://unknown.example.test/calendar/task.ics", "\"old\"", Token);
        await fixture.Store.MoveTaskAsync(task.Uid, target.Id, cancellationToken: Token);
        Assert.False((await fixture.Store.GetPendingTaskMovesAsync(cancellationToken: Token)).Single().SourceBindingVerified);
        await fixture.Store.MoveTaskAsync(task.Uid, source.Id, cancellationToken: Token);
        Assert.Single(await fixture.Store.GetPendingTaskMovesAsync(cancellationToken: Token));
        Assert.Null((await fixture.Store.GetTaskAsync(task.Uid, Token))!.RemoteHref);
    }

    [Fact]
    public async Task RejectedOldRemoteMove_HoldsKnownVersionAcrossRemoteTombstoneAndFuturePulls()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        (TodoList source, TodoList target, Account account) = await ListsAsync(fixture.Store);
        TodoTask known = await SyncedTaskAsync(fixture.Store, source, account, "Known");
        known.Description = "Latest notes";
        await fixture.Store.UpdateTaskAsync(known, Token);
        await AcknowledgeAsync(fixture.Store, known, source, account);
        known = (await fixture.Store.GetTaskAsync(known.Uid, Token))!;
        RemoteTaskState state = (await fixture.Store.GetRemoteTaskStatesAsync(source.Id, Token)).Single();
        await fixture.Store.ApplyRemotePullAsync(source.Id, [], [state.RemoteHref], [state], Token);
        Assert.True((await fixture.Store.GetTaskForSyncAsync(known.Uid, Token))!.IsDeleted);
        state = (await fixture.Store.GetRemoteTaskStatesAsync(source.Id, Token)).Single();
        var old = new TodoTask { Uid = known.Uid, Summary = "Old", Sequence = known.Sequence - 1 };
        await fixture.Store.ApplyRemoteTaskMoveAsync(target.Id,
            new RemoteTaskUpdate { Task = old, Href = target.RemoteUrl + "task.ics", ETag = "\"old\"" }, state, Token);
        Assert.False((await fixture.Store.GetTaskForSyncAsync(known.Uid, Token))!.IsDeleted);
        state = (await fixture.Store.GetRemoteTaskStatesAsync(source.Id, Token)).Single();
        RemotePullApplyResult deleted = await fixture.Store.ApplyRemotePullAsync(source.Id, [], [state.RemoteHref], [state], Token);
        Assert.True(deleted.HasDeferredChanges);
        Assert.False((await fixture.Store.GetTaskForSyncAsync(known.Uid, Token))!.IsDeleted);
        Assert.Equal("Latest notes", (await fixture.Store.GetTaskForSyncAsync(known.Uid, Token))!.Description);
    }

    [Fact]
    public async Task ExplicitUserTombstone_IsNotReclassifiedByRepeatedServerDeletion()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        (TodoList source, TodoList target, Account account) = await ListsAsync(fixture.Store);
        TodoTask task = await SyncedTaskAsync(fixture.Store, source, account, "Task");
        await fixture.Store.DeleteTaskAsync(task.Uid, Token);
        SyncChange deletion = (await fixture.Store.GetPendingSyncChangesAsync(account.Id, Token)).Single();
        await fixture.Store.MarkTaskChangeSyncedAsync(deletion.Id, task.Uid, task.RemoteHref, null, Token);
        RemoteTaskState state = (await fixture.Store.GetRemoteTaskStatesAsync(source.Id, Token)).Single();
        await fixture.Store.ApplyRemotePullAsync(source.Id, [], [state.RemoteHref], [state], Token);
        state = (await fixture.Store.GetRemoteTaskStatesAsync(source.Id, Token)).Single();
        await fixture.Store.ApplyRemoteTaskMoveAsync(target.Id,
            new RemoteTaskUpdate { Task = new TodoTask { Uid = task.Uid, Summary = "Old", Sequence = 0 },
                Href = target.RemoteUrl + "task.ics", ETag = "\"old\"" }, state, Token);
        Assert.True((await fixture.Store.GetTaskForSyncAsync(task.Uid, Token))!.IsDeleted);
    }

    [Fact]
    public async Task RootRestoreAfterSkippedGroupAttempt_QueuesNewChildBoundary()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        (TodoList source, TodoList target, Account account) = await ListsAsync(fixture.Store);
        TodoTask root = await SyncedTaskAsync(fixture.Store, source, account, "Root");
        TodoTask child = await fixture.Store.AddStepAsync(root.Uid, "Step", Token);
        await AcknowledgeAsync(fixture.Store, child, source, account);
        await fixture.Store.MoveTaskAsync(root.Uid, target.Id, cancellationToken: Token);
        await fixture.Store.DeleteTaskAsync(root.Uid, Token);
        IReadOnlyList<TaskMoveIntent> moves = await fixture.Store.GetPendingTaskMovesAsync(cancellationToken: Token);
        var attempts = new List<TaskMoveAttempt>();
        foreach (TaskMoveIntent move in moves)
            attempts.Add(new TaskMoveAttempt { MoveId = move.Id,
                ExpectedSequence = (await fixture.Store.GetTaskForSyncAsync(move.TaskUid, Token))!.Sequence,
                DestinationHref = target.RemoteUrl + move.TaskUid + ".ics", Payload = [1] });
        moves = (await fixture.Store.PrepareTaskMoveAttemptsAsync(attempts, Token))!;
        Assert.All(moves, move => Assert.Empty(move.AttemptedPayload!));
        await fixture.Store.RestoreTaskAsync(root.Uid, Token);
        foreach (TaskMoveIntent move in moves)
        {
            move.Phase = TaskMovePhase.DestinationConfirmed;
            Assert.True(await fixture.Store.UpdateTaskMoveAsync(move, move.Version, Token));
            move.Phase = TaskMovePhase.SourceRemoved;
            Assert.True(await fixture.Store.UpdateTaskMoveAsync(move, move.Version, Token));
        }
        Assert.True(await fixture.Store.CompleteTaskMoveGroupAsync(moves.Select(move =>
            new TaskMoveUpdate { Intent = move, ExpectedVersion = move.Version }).ToArray(), Token));
        TodoTask queued = (await fixture.Store.GetTaskAsync(child.Uid, Token))!;
        Assert.True(queued.IsDirty);
        Assert.Null(queued.RemoteHref);
        Assert.Contains(await fixture.Store.GetPendingSyncChangesAsync(account.Id, Token), change => change.EntityUid == child.Uid);
    }

    [Fact]
    public async Task SkippedDestination_RepreparesLivePayloadAfterSourceRemovedWithoutLosingSourceProof()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        (TodoList source, TodoList target, Account account) = await ListsAsync(fixture.Store);
        TodoTask task = await SyncedTaskAsync(fixture.Store, source, account, "Task");
        await fixture.Store.MoveTaskAsync(task.Uid, target.Id, cancellationToken: Token);
        await fixture.Store.DeleteTaskAsync(task.Uid, Token);
        TaskMoveIntent move = (await fixture.Store.GetPendingTaskMovesAsync(cancellationToken: Token)).Single();
        move = (await fixture.Store.PrepareTaskMoveAttemptAsync(move.Id,
            (await fixture.Store.GetTaskForSyncAsync(task.Uid, Token))!.Sequence,
            target.RemoteUrl + task.Uid + ".ics", [1], Token))!;
        Assert.Empty(move.AttemptedPayload!);
        move.SourcePayload = [3];
        move.Phase = TaskMovePhase.DestinationConfirmed;
        Assert.True(await fixture.Store.UpdateTaskMoveAsync(move, move.Version, Token));
        move.Phase = TaskMovePhase.SourceRemoved;
        Assert.True(await fixture.Store.UpdateTaskMoveAsync(move, move.Version, Token));
        await fixture.Store.RestoreTaskAsync(task.Uid, Token);
        TodoTask restored = (await fixture.Store.GetTaskForSyncAsync(task.Uid, Token))!;
        TaskMoveIntent prepared = (await fixture.Store.PrepareTaskMoveAttemptAsync(move.Id,
            restored.Sequence, move.DestinationHref!, [4, 5], Token))!;
        Assert.Equal(TaskMovePhase.DestinationAttempted, prepared.Phase);
        Assert.Equal(new byte[] { 4, 5 }, prepared.AttemptedPayload);
        Assert.Equal(restored.Sequence, prepared.AttemptedSequence);
        Assert.Equal(new byte[] { 3 }, prepared.SourcePayload);
        Assert.Equal(move.SourceHref, prepared.SourceHref);
        Assert.Equal(move.SourceETag, prepared.SourceETag);
    }

    [Fact]
    public async Task RepreparingSkippedStep_PreservesRealParentAttemptBytesAndVersion()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        (TodoList source, TodoList target, Account account) = await ListsAsync(fixture.Store);
        TodoTask root = await SyncedTaskAsync(fixture.Store, source, account, "Root");
        TodoTask child = await fixture.Store.AddStepAsync(root.Uid, "Step", Token);
        await AcknowledgeAsync(fixture.Store, child, source, account);
        await fixture.Store.MoveTaskAsync(root.Uid, target.Id, cancellationToken: Token);
        await fixture.Store.DeleteTaskAsync(child.Uid, Token);
        IReadOnlyList<TaskMoveIntent> moves = await fixture.Store.GetPendingTaskMovesAsync(cancellationToken: Token);
        var attempts = new List<TaskMoveAttempt>();
        foreach (TaskMoveIntent move in moves)
            attempts.Add(new TaskMoveAttempt { MoveId = move.Id,
                ExpectedSequence = (await fixture.Store.GetTaskForSyncAsync(move.TaskUid, Token))!.Sequence,
                DestinationHref = target.RemoteUrl + move.TaskUid + ".ics", Payload = [1, 2] });
        moves = (await fixture.Store.PrepareTaskMoveAttemptsAsync(attempts, Token))!;
        TaskMoveIntent originalParent = moves.Single(move => move.TaskUid == root.Uid);
        Assert.Equal(new byte[] { 1, 2 }, originalParent.AttemptedPayload);
        Assert.Empty(moves.Single(move => move.TaskUid == child.Uid).AttemptedPayload!);
        await fixture.Store.RestoreTaskAsync(child.Uid, Token);
        attempts.Clear();
        foreach (TaskMoveIntent move in moves)
            attempts.Add(new TaskMoveAttempt { MoveId = move.Id,
                ExpectedSequence = (await fixture.Store.GetTaskForSyncAsync(move.TaskUid, Token))!.Sequence,
                DestinationHref = move.DestinationHref!, Payload = move.TaskUid == root.Uid ? [] : [9] });
        IReadOnlyList<TaskMoveIntent> prepared = (await fixture.Store.PrepareTaskMoveAttemptsAsync(attempts, Token))!;
        TaskMoveIntent parent = prepared.Single(move => move.TaskUid == root.Uid);
        Assert.Equal(originalParent.Version, parent.Version);
        Assert.Equal(new byte[] { 1, 2 }, parent.AttemptedPayload);
        Assert.Equal(new byte[] { 9 }, prepared.Single(move => move.TaskUid == child.Uid).AttemptedPayload);
    }
}
