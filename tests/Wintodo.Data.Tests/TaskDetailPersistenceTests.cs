using Wintodo.Core.Models;
using Wintodo.Core.Services;

namespace Wintodo.Data.Tests;

public sealed class TaskDetailPersistenceTests
{
    [Fact]
    public async Task TomorrowFromStaleDetailPreservesRowEditsAndUndoOnlyRestoresDue()
    {
        var ct = TestContext.Current.CancellationToken;
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        var store = fixture.Store;
        TodoList list = await store.CreateListAsync("Manual", cancellationToken: ct);
        TodoTask first = await store.CreateTaskAsync(new() { ListId = list.Id, Summary = "First" }, ct);
        TodoTask opened = await store.CreateTaskAsync(new() { ListId = list.Id, Summary = "Before" }, ct);
        await store.RenameTaskAsync(opened.Uid, "Renamed in list", ct);
        double order = await store.SetTaskImportanceAsync(opened.Uid, true, moveStarredToTop: true, ct);
        Assert.True(order < first.SortOrder);
        await store.ApplyTaskBatchAsync([opened.Uid], TaskBatchAction.AddToMyDay, ct);

        await store.SetAlarmAsync(opened.Uid, new() { Trigger = fixture.Clock.UtcNow.AddHours(2) }, ct);
        Assert.True(await store.TryAddTaskAttachmentAsync(opened.Uid,
            new() { FileName = "later.txt", RelativePath = "later.txt", SizeBytes = 4 }, 1000, ct));
        var undo = await store.CaptureTaskUndoAsync([opened.Uid], TaskUndoFields.Due, ct);
        TodoTask before = Assert.Single(undo).Task;
        var tomorrow = new DateTimeOffset(fixture.Clock.UtcNow.UtcDateTime.Date.AddDays(1), TimeSpan.Zero);
        opened.Due = tomorrow;
        opened.DueIsDateOnly = true;
        TodoTask saved = await store.UpdateTaskDetailAsync(opened, TaskDetailFields.Due,
            moveStarredToTop: true, cancellationToken: ct);

        foreach (TodoTask task in new[] { saved, (await store.GetTaskAsync(opened.Uid, ct))! })
        {
            Assert.Equal("Renamed in list", task.Summary);
            Assert.Equal((int)TaskPriority.High, task.Priority);
            Assert.Equal(order, task.SortOrder);
            Assert.Equal(fixture.Clock.Today, task.MyDayDate);
            Assert.Equal(tomorrow, task.Due);
            Assert.True(task.DueIsDateOnly);
            Assert.Equal(Assert.Single(before.Alarms).Id, Assert.Single(task.Alarms).Id);
            Assert.Equal(Assert.Single(before.Attachments).Id, Assert.Single(task.Attachments).Id);
            Assert.Equal(before.Sequence + 1, task.Sequence);
        }

        await store.RestoreTaskFieldsAsync(undo, ct);
        TodoTask restored = (await store.GetTaskAsync(opened.Uid, ct))!;
        Assert.Null(restored.Due);
        Assert.False(restored.DueIsDateOnly);
        Assert.Equal("Renamed in list", restored.Summary);
        Assert.Equal((int)TaskPriority.High, restored.Priority);
        Assert.Equal(fixture.Clock.Today, restored.MyDayDate);
        Assert.Equal("later.txt", Assert.Single(restored.Attachments).RelativePath);
        Assert.Equal(Assert.Single(before.Alarms).Uid, Assert.Single(restored.Alarms).Uid);
    }

    [Theory]
    [InlineData(TaskDetailFields.Description, "Current title", "Detail note")]
    [InlineData(TaskDetailFields.Summary, "Detail title", "Current note")]
    [InlineData(TaskDetailFields.Summary | TaskDetailFields.Description, "Detail title", "Detail note")]
    public async Task DebouncedTextSavesOnlyTheFieldsTheUserEdited(TaskDetailFields fields, string title, string note)
    {
        var ct = TestContext.Current.CancellationToken;
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        TodoList list = await fixture.Store.CreateListAsync("Tasks", cancellationToken: ct);
        TodoTask opened = await fixture.Store.CreateTaskAsync(new() { ListId = list.Id, Summary = "Opened" }, ct);
        await fixture.Store.SetTaskTextAsync(opened.Uid, "Current title", "Current note", ct);
        opened.Summary = "Detail title";
        opened.Description = "Detail note";

        TodoTask saved = await fixture.Store.UpdateTaskDetailAsync(opened, fields, cancellationToken: ct);
        TodoTask stored = (await fixture.Store.GetTaskAsync(opened.Uid, ct))!;
        Assert.Equal((title, note), (saved.Summary, saved.Description));
        Assert.Equal((title, note), (stored.Summary, stored.Description));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SettingRepeatDefaultsToTodayOnlyIfTaskStillHasNoDueDate(bool dateSetAfterOpening)
    {
        var ct = TestContext.Current.CancellationToken;
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        TodoList list = await fixture.Store.CreateListAsync("Repeat", cancellationToken: ct);
        TodoTask opened = await fixture.Store.CreateTaskAsync(new() { ListId = list.Id, Summary = "Weekly" }, ct);
        var today = new DateTimeOffset(fixture.Clock.UtcNow.UtcDateTime.Date, TimeSpan.Zero);
        DateTimeOffset expectedDue = dateSetAfterOpening ? today.AddDays(3) : today;
        if (dateSetAfterOpening)
            await fixture.Store.ApplyTaskBatchAsync([opened.Uid], TaskBatchAction.SetDueDate, ct, date: fixture.Clock.Today.AddDays(3));
        opened.RRule = "FREQ=WEEKLY";
        opened.Due = today; // The detail editor shows today's default while the write is pending.
        opened.DueIsDateOnly = true;
        TodoTask saved = await fixture.Store.UpdateTaskDetailAsync(opened, TaskDetailFields.Repeat, cancellationToken: ct);
        foreach (TodoTask after in new[] { saved, (await fixture.Store.GetTaskAsync(opened.Uid, ct))! })
        {
            Assert.Equal("FREQ=WEEKLY", after.RRule);
            Assert.Equal(expectedDue, after.Due);
            Assert.True(after.DueIsDateOnly);
        }
    }

    [Fact]
    public async Task RemovingAnAttachmentFromStaleDetailPreservesASecondAttachmentAndReminder()
    {
        var ct = TestContext.Current.CancellationToken;
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        TodoList list = await fixture.Store.CreateListAsync("Attachments", cancellationToken: ct);
        var task = new TodoTask { ListId = list.Id, Summary = "With file" };
        task.Attachments.Add(new() { FileName = "old.txt", RelativePath = "old.txt", SizeBytes = 2 });
        TodoTask opened = await fixture.Store.CreateTaskAsync(task, ct);
        long remove = Assert.Single(opened.Attachments).Id;
        var later = new TaskAttachment { FileName = "new.txt", RelativePath = "new.txt", SizeBytes = 3 };
        Assert.True(await fixture.Store.TryAddTaskAttachmentAsync(opened.Uid, later, 1000, ct));

        DateTimeOffset trigger = fixture.Clock.UtcNow.AddHours(4);
        opened.Alarms.Add(new() { Trigger = trigger });
        TodoTask withReminder = await fixture.Store.UpdateTaskDetailAsync(opened, TaskDetailFields.Reminder, cancellationToken: ct);
        Assert.Equal(2, withReminder.Attachments.Count);
        long keep = Assert.Single(withReminder.Attachments, item => item.RelativePath == later.RelativePath).Id;
        TaskAlarm alarm = Assert.Single(withReminder.Alarms);
        Assert.Equal(trigger, alarm.Trigger);
        await fixture.Store.SnoozeAlarmAsync(alarm.Uid, trigger.AddMinutes(10), ct);

        opened.Attachments.Clear();
        TodoTask saved = await fixture.Store.UpdateTaskDetailAsync(opened, TaskDetailFields.RemoveAttachment,
            removedAttachmentId: remove, cancellationToken: ct);
        foreach (TodoTask after in new[] { saved, (await fixture.Store.GetTaskAsync(opened.Uid, ct))! })
        {
            Assert.Equal(keep, Assert.Single(after.Attachments).Id);
            Assert.Equal(alarm.Id, Assert.Single(after.Alarms).Id);
            Assert.Equal(trigger.AddMinutes(10), after.Alarms[0].SnoozedUntil);
        }
    }
}
