using Microsoft.Data.Sqlite;
using Wintodo.Core.Models;
using Wintodo.Core.Services;

namespace Wintodo.Data.Tests;

public sealed class TaskBatchTests
{
    [Fact]
    public async Task UndatedSuggestionCanBeAddedAndRejectsStaleActions()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        var list = await fixture.Store.CreateListAsync("Source", cancellationToken: token);
        var task = await fixture.Store.CreateTaskAsync(new()
        {
            ListId = list.Id,
            Summary = "Suggestion",
        }, token);
        var displayed = TaskSuggestionSnapshot.FromTask(task);
        await fixture.Store.ApplyTaskBatchAsync([task.Uid], TaskBatchAction.AddToMyDay, token,
            expectedSuggestion: displayed);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.ApplyTaskBatchAsync(
            [task.Uid], TaskBatchAction.Complete, token, expectedSuggestion: displayed));
        var pinned = (await fixture.Store.GetTaskAsync(task.Uid, token))!;
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.ApplyTaskBatchAsync(
            [task.Uid], TaskBatchAction.Complete, token, expectedSuggestion: TaskSuggestionSnapshot.FromTask(pinned)));
        Assert.Equal(list.Id, pinned.ListId);
        Assert.Equal(fixture.Clock.Today, pinned.MyDayDate);
        Assert.Null(pinned.Due);
        Assert.Equal(Core.Models.TaskStatus.NeedsAction, pinned.Status);
    }

    [Theory]
    [InlineData(TaskBatchAction.AddToMyDay)]
    [InlineData(TaskBatchAction.Complete)]
    public async Task StaleSuggestionCannotActOnNextRecurringOccurrence(TaskBatchAction staleAction)
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        var list = await fixture.Store.CreateListAsync("Source", cancellationToken: token);
        DateTimeOffset due = fixture.Clock.UtcNow.AddDays(1);
        var first = await fixture.Store.CreateTaskAsync(new()
        {
            ListId = list.Id, Summary = "Daily appointment", Due = due, RRule = "FREQ=DAILY",
        }, token);
        var displayed = TaskSuggestionSnapshot.FromTask(first);
        string[] uids = [first.Uid];
        await fixture.Store.CompleteTaskAsync(first.Uid, token);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.ApplyTaskBatchAsync(
            uids, staleAction, token, expectedSuggestion: displayed));
        var afterStale = Assert.Single(await fixture.Store.CaptureTaskUndoAsync(uids, TaskUndoFields.Completion, token));
        Assert.Equal(1, afterStale.CompletionCount);
        Assert.Equal(first.Sequence + 1, afterStale.Task.Sequence);
        Assert.Equal(due.AddDays(1), afterStale.Task.Due);
        Assert.Null(afterStale.Task.MyDayDate);
        Assert.Equal(Core.Models.TaskStatus.NeedsAction, afterStale.Task.Status);

        await fixture.Store.ApplyTaskBatchAsync(uids, TaskBatchAction.Complete, token,
            expectedSuggestion: TaskSuggestionSnapshot.FromTask(afterStale.Task));
        var afterFresh = Assert.Single(await fixture.Store.CaptureTaskUndoAsync(uids, TaskUndoFields.Completion, token));
        Assert.Equal(2, afterFresh.CompletionCount);
        Assert.Equal(due.AddDays(2), afterFresh.Task.Due);
    }

    [Fact]
    public async Task RemoteRescheduleWithoutSequenceBumpInvalidatesDisplayedSuggestion()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        var list = await fixture.Store.CreateListAsync("Source", cancellationToken: token);
        DateTimeOffset due = fixture.Clock.UtcNow.AddDays(1);
        var task = new TodoTask { Uid = "shared", Summary = "Appointment", Due = due, RRule = "FREQ=DAILY", Sequence = 2 };
        const string href = "https://dav.example/shared.ics";
        await fixture.Store.ApplyRemoteChangesAsync(list.Id, [new() { Task = task, Href = href, ETag = "v1" }], [], token);
        var displayed = TaskSuggestionSnapshot.FromTask((await fixture.Store.GetTaskAsync(task.Uid, token))!);
        task.Due = due.AddDays(1);
        await fixture.Store.ApplyRemoteChangesAsync(list.Id, [new() { Task = task, Href = href, ETag = "v2" }], [], token);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.ApplyTaskBatchAsync(
            [task.Uid], TaskBatchAction.Complete, token, expectedSuggestion: displayed));
        var saved = Assert.Single(await fixture.Store.CaptureTaskUndoAsync([task.Uid], TaskUndoFields.Completion, token));
        Assert.Equal(displayed.Sequence, saved.Task.Sequence);
        Assert.Equal(due.AddDays(1), saved.Task.Due);
        Assert.Equal(0, saved.CompletionCount);
    }

    [Theory]
    [InlineData(TaskBatchAction.AddToMyDay)]
    [InlineData(TaskBatchAction.MarkImportant)]
    public async Task SmartListDrop_SetsSelectionOnceAndRetainsOriginalList(TaskBatchAction action)
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        TodoList list = await fixture.Store.CreateListAsync("Source", cancellationToken: token);
        TodoTask first = await fixture.Store.CreateTaskAsync(new() { ListId = list.Id, Summary = "First" }, token);
        TodoTask second = await fixture.Store.CreateTaskAsync(new() { ListId = list.Id, Summary = "Second" }, token);
        TodoTask untouched = await fixture.Store.CreateTaskAsync(new() { ListId = list.Id, Summary = "Untouched" }, token);
        int events = 0;
        fixture.Store.StoreChanged += (_, _) => events++;
        await fixture.Store.ApplyTaskBatchAsync([first.Uid, second.Uid], action, token);
        int changes = ChangeCount(fixture.DatabasePath);
        await fixture.Store.ApplyTaskBatchAsync([first.Uid, second.Uid], action, token);
        Assert.Equal(1, events);
        Assert.Equal(changes, ChangeCount(fixture.DatabasePath));
        foreach (string uid in new[] { first.Uid, second.Uid })
        {
            TodoTask updated = (await fixture.Store.GetTaskAsync(uid, token))!;
            Assert.Equal(list.Id, updated.ListId);
            if (action == TaskBatchAction.AddToMyDay) Assert.Equal(fixture.Clock.Today, updated.MyDayDate);
            else Assert.Equal((int)TaskPriority.High, updated.Priority);
        }
        TodoTask retained = (await fixture.Store.GetTaskAsync(untouched.Uid, token))!;
        Assert.Null(retained.MyDayDate);
        Assert.Equal(0, retained.Priority);
    }

    [Theory]
    [InlineData(TaskBatchAction.Complete)]
    [InlineData(TaskBatchAction.Uncomplete)]
    [InlineData(TaskBatchAction.MarkImportant)]
    [InlineData(TaskBatchAction.RemoveImportance)]
    [InlineData(TaskBatchAction.AddToMyDay)]
    [InlineData(TaskBatchAction.RemoveFromMyDay)]
    [InlineData(TaskBatchAction.SetDueDate)]
    [InlineData(TaskBatchAction.Move)]
    [InlineData(TaskBatchAction.Delete)]
    [InlineData(TaskBatchAction.Restore)]
    public async Task Batch_CommitsOnceAndPreservesUnrelatedData(TaskBatchAction action)
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        TodoList source = await fixture.Store.CreateListAsync("Source", cancellationToken: token);
        TodoList target = await fixture.Store.CreateListAsync("Target", cancellationToken: token);
        var tasks = new List<TodoTask>();
        for (int i = 0; i < 3; i++)
        {
            var task = new TodoTask
            {
                ListId = source.Id, Summary = $"Task {i}", Description = "Retain note",
                IcalUnknown = "X-OTHER;PARAM=VALUE:keep", Priority = action == TaskBatchAction.RemoveImportance ? 1 : 0,
                MyDayDate = action == TaskBatchAction.RemoveFromMyDay ? fixture.Clock.Today : null,
            };
            task.Categories.Add("retain-tag");
            tasks.Add(await fixture.Store.CreateTaskAsync(task, token));
            if (action == TaskBatchAction.Uncomplete) await fixture.Store.CompleteTaskAsync(task.Uid, token);
            if (action == TaskBatchAction.Restore) await fixture.Store.DeleteTaskAsync(task.Uid, token);
        }
        int before = ChangeCount(fixture.DatabasePath);
        var events = new List<StoreChangedEventArgs>();
        fixture.Store.StoreChanged += (_, args) => events.Add(args);
        string[] uids = tasks.Select(task => task.Uid).ToArray();
        await fixture.Store.ApplyTaskBatchAsync([.. uids, uids[0]], action, token,
            date: new DateOnly(2026, 1, 9), destinationListId: target.Id);
        Assert.Single(events);
        Assert.Equal(3, events[0].TaskUids.Count);
        Assert.Equal(before + 3, ChangeCount(fixture.DatabasePath));
        foreach (string uid in uids)
        {
            TodoTask? task = await fixture.Store.GetTaskAsync(uid, token);
            if (action == TaskBatchAction.Delete) { Assert.Null(task); continue; }
            Assert.NotNull(task);
            Assert.Equal("Retain note", task.Description);
            Assert.Equal("X-OTHER;PARAM=VALUE:keep", task.IcalUnknown);
            Assert.Contains("retain-tag", task.Categories);
            Assert.True(task.IsDirty);
            if (action == TaskBatchAction.Complete) Assert.Equal(Core.Models.TaskStatus.Completed, task.Status);
            if (action == TaskBatchAction.Uncomplete) Assert.Equal(Core.Models.TaskStatus.NeedsAction, task.Status);
            if (action == TaskBatchAction.MarkImportant) Assert.Equal(1, task.Priority);
            if (action == TaskBatchAction.RemoveImportance) Assert.Equal(0, task.Priority);
            if (action == TaskBatchAction.AddToMyDay) Assert.Equal(fixture.Clock.Today, task.MyDayDate);
            if (action == TaskBatchAction.RemoveFromMyDay) Assert.Null(task.MyDayDate);
            if (action == TaskBatchAction.SetDueDate)
            {
                Assert.True(task.DueIsDateOnly);
                Assert.Equal(new DateOnly(2026, 1, 9), DateOnly.FromDateTime(task.Due!.Value.DateTime));
            }
            if (action == TaskBatchAction.Move) Assert.Equal(target.Id, task.ListId);
        }
        if (action == TaskBatchAction.Move)
        {
            var moved = await fixture.Store.GetTasksAsync(new() { Kind = Core.Queries.ListQueryKind.UserList, ListId = target.Id }, token);
            Assert.Equal(3, moved.Select(task => task.SortOrder).Distinct().Count());
            Assert.Contains(source.Id, events[0].ListIds);
            Assert.Contains(target.Id, events[0].ListIds);
        }
    }

    [Theory]
    [InlineData(TaskBatchAction.Complete)]
    [InlineData(TaskBatchAction.MarkImportant)]
    [InlineData(TaskBatchAction.Move)]
    [InlineData(TaskBatchAction.Delete)]
    public async Task Batch_MissingTaskRollsBackChangesAndEmitsNoEvent(TaskBatchAction action)
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        TodoList list = await fixture.Store.CreateListAsync("Source", cancellationToken: token);
        TodoList target = await fixture.Store.CreateListAsync("Target", cancellationToken: token);
        TodoTask task = await fixture.Store.CreateTaskAsync(new() { ListId = list.Id, Summary = "Keep" }, token);
        int before = ChangeCount(fixture.DatabasePath);
        int events = 0;
        fixture.Store.StoreChanged += (_, _) => events++;
        await Assert.ThrowsAsync<KeyNotFoundException>(() => fixture.Store.ApplyTaskBatchAsync(
            [task.Uid, "missing"], action, token, destinationListId: target.Id));
        TodoTask restored = (await fixture.Store.GetTaskAsync(task.Uid, token))!;
        Assert.Equal(list.Id, restored.ListId);
        Assert.Equal(Core.Models.TaskStatus.NeedsAction, restored.Status);
        Assert.Equal(0, restored.Priority);
        Assert.Equal(task.Sequence, restored.Sequence);
        Assert.Equal(before, ChangeCount(fixture.DatabasePath));
        Assert.Equal(0, events);
    }

    [Fact]
    public async Task Batch_RecurringCompletionAdvancesAlarmsAndHistory()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        TodoList list = await fixture.Store.CreateListAsync("Repeat", cancellationToken: token);
        TodoTask task = new() { ListId = list.Id, Summary = "Daily", Due = fixture.Clock.UtcNow, RRule = "FREQ=DAILY" };
        task.Alarms.Add(new() { Uid = Guid.NewGuid().ToString(), Trigger = fixture.Clock.UtcNow });
        await fixture.Store.CreateTaskAsync(task, token);
        await fixture.Store.ApplyTaskBatchAsync([task.Uid], TaskBatchAction.Complete, token);
        TodoTask updated = (await fixture.Store.GetTaskAsync(task.Uid, token))!;
        Assert.Equal(task.Due!.Value.AddDays(1), updated.Due);
        Assert.Equal(Core.Models.TaskStatus.NeedsAction, updated.Status);
        Assert.Equal(fixture.Clock.UtcNow.AddDays(1), Assert.Single(updated.Alarms).Trigger);
    }

    private static int ChangeCount(string path)
    {
        using var connection = TestSqliteConnections.Create(path, SqliteOpenMode.ReadOnly);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM change_log;";
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
