using Wintodo.Core.Models;
using Wintodo.Core.Queries;
using Wintodo.Core.Services;

namespace Wintodo.Data.Tests;

public sealed class TaskStoreTests
{
    [Fact]
    public async Task TaskLifecycle_RoundTripsAllOwnedCollectionsAndSearches()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        TodoList source = await fixture.Store.CreateListAsync("Source", cancellationToken: TestContext.Current.CancellationToken);
        TodoList destination = await fixture.Store.CreateListAsync("Destination", cancellationToken: TestContext.Current.CancellationToken);
        var task = new TodoTask
        {
            ListId = source.Id,
            Summary = "Prepare quarterly report",
            Description = "Collect revenue and expense data",
            Priority = (int)TaskPriority.High,
            Due = fixture.Clock.UtcNow.AddDays(2),
            Start = fixture.Clock.UtcNow.AddDays(1),
            MyDayDate = fixture.Clock.Today,
            Location = "Office",
            Url = "https://example.test/report",
            Class = Classification.Confidential,
            IcalUnknown = "X-OWNER:finance",
        };
        task.Categories.AddRange(["Finance", "Quarterly", "finance"]);
        task.Alarms.Add(new TaskAlarm
        {
            TriggerOffsetSeconds = -900,
            TriggerRelated = AlarmRelation.End,
            Description = "Report due soon",
        });
        task.Attachments.Add(new TaskAttachment
        {
            FileName = "source.csv",
            RelativePath = "attachments/source.csv",
            SizeBytes = 1234,
            MimeType = "text/csv",
        });

        TodoTask created = await fixture.Store.CreateTaskAsync(task, TestContext.Current.CancellationToken);
        TodoTask stored = Assert.IsType<TodoTask>(await fixture.Store.GetTaskAsync(created.Uid, TestContext.Current.CancellationToken));

        Assert.True(stored.Id > 0);
        Assert.Equal(source.Id, stored.ListId);
        Assert.Equal("Prepare quarterly report", stored.Summary);
        Assert.Equal(2, stored.Categories.Count);
        Assert.Single(stored.Alarms);
        Assert.Single(stored.Attachments);
        Assert.True(stored.IsDirty);
        Assert.Single(await fixture.Store.SearchAsync("quarter rep", cancellationToken: TestContext.Current.CancellationToken));

        stored.Summary = "Publish quarterly report";
        stored.Description = "Final reviewed copy";
        stored.Priority = (int)TaskPriority.Medium;
        stored.Categories.Clear();
        stored.Categories.Add("Published");
        stored.Alarms.Clear();
        stored.Alarms.Add(new TaskAlarm { Trigger = fixture.Clock.UtcNow.AddHours(3) });
        stored.Attachments.Clear();
        stored.Attachments.Add(new TaskAttachment
        {
            FileName = "report.pdf",
            RelativePath = "attachments/report.pdf",
            SizeBytes = 9000,
        });
        await fixture.Store.UpdateTaskAsync(stored, TestContext.Current.CancellationToken);

        TodoTask updated = Assert.IsType<TodoTask>(await fixture.Store.GetTaskAsync(created.Uid, TestContext.Current.CancellationToken));
        Assert.Equal("Publish quarterly report", updated.Summary);
        Assert.Equal("Published", Assert.Single(updated.Categories));
        Assert.Equal(1, updated.Sequence);
        Assert.Single(updated.Alarms);
        Assert.Single(updated.Attachments);
        Assert.Single(await fixture.Store.SearchAsync("reviewed", cancellationToken: TestContext.Current.CancellationToken));

        await fixture.Store.ReorderTaskAsync(updated.Uid, 42.5, TestContext.Current.CancellationToken);
        await fixture.Store.MoveTaskAsync(updated.Uid, destination.Id, cancellationToken: TestContext.Current.CancellationToken);
        TodoTask moved = Assert.IsType<TodoTask>(await fixture.Store.GetTaskAsync(updated.Uid, TestContext.Current.CancellationToken));
        Assert.Equal(destination.Id, moved.ListId);
        Assert.Equal(1, moved.SortOrder);

        await fixture.Store.DeleteTaskAsync(moved.Uid, TestContext.Current.CancellationToken);
        Assert.Null(await fixture.Store.GetTaskAsync(moved.Uid, TestContext.Current.CancellationToken));
        await fixture.Store.RestoreTaskAsync(moved.Uid, TestContext.Current.CancellationToken);
        Assert.NotNull(await fixture.Store.GetTaskAsync(moved.Uid, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Steps_AreOrderedAndRemainInParentList()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        TodoList list = await fixture.Store.CreateListAsync("Project", cancellationToken: TestContext.Current.CancellationToken);
        TodoTask parent = await fixture.Store.CreateTaskAsync(new TodoTask
        {
            ListId = list.Id,
            Summary = "Ship release",
        }, TestContext.Current.CancellationToken);

        TodoTask first = await fixture.Store.AddStepAsync(parent.Uid, "Build", TestContext.Current.CancellationToken);
        TodoTask second = await fixture.Store.AddStepAsync(parent.Uid, "Test", TestContext.Current.CancellationToken);
        await fixture.Store.ReorderStepAsync(second.Uid, 0.5, TestContext.Current.CancellationToken);

        IReadOnlyList<TodoTask> children = await fixture.Store.GetChildrenAsync(parent.Uid, TestContext.Current.CancellationToken);
        Assert.Equal([second.Uid, first.Uid], children.Select(item => item.Uid));
        Assert.All(children, child =>
        {
            Assert.Equal(parent.Uid, child.ParentUid);
            Assert.Equal(list.Id, child.ListId);
        });
    }

    [Fact]
    public async Task MyDayIncludesTasksAsSoonAsTheirDueDateBecomesToday()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        fixture.Clock.LocalTimeZone = TimeZoneInfo.CreateCustomTimeZone(
            "Test UTC+05:30", TimeSpan.FromMinutes(330), "Test UTC+05:30", "Test UTC+05:30");
        fixture.Clock.UtcNow = new DateTimeOffset(2026, 1, 4, 23, 0, 0, TimeSpan.Zero);
        var token = TestContext.Current.CancellationToken;
        TodoList list = await fixture.Store.CreateListAsync("Inbox", cancellationToken: token);
        TodoTask task = await AddTaskAsync(fixture, list.Id, "Due date changes", priority: 0,
            due: fixture.Clock.UtcNow.AddDays(1));
        Assert.Empty(await QueryAsync(fixture, ListQueryKind.MyDay));

        task.Due = new DateTimeOffset(fixture.Clock.Today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        task.DueIsDateOnly = true;
        await fixture.Store.UpdateTaskAsync(task, token);
        TodoTask timed = await AddTaskAsync(fixture, list.Id, "Timed today", priority: 0,
            due: fixture.Clock.UtcNow);
        Assert.Equal(2, (await QueryAsync(fixture, ListQueryKind.MyDay)).Count);
        Assert.Equal(2, (await fixture.Store.GetSmartListTaskCountsAsync(token))[ListQueryKind.MyDay]);

        task.Due = null;
        task.DueIsDateOnly = false;
        await fixture.Store.UpdateTaskAsync(task, token);
        Assert.Equal(timed.Uid, Assert.Single(await QueryAsync(fixture, ListQueryKind.MyDay)).Uid);
        Assert.Equal(1, (await fixture.Store.GetSmartListTaskCountsAsync(token))[ListQueryKind.MyDay]);

        var undoRemoval = await fixture.Store.CaptureTaskUndoAsync([timed.Uid], TaskUndoFields.MyDay, token);
        await fixture.Store.ApplyTaskBatchAsync([timed.Uid], TaskBatchAction.RemoveFromMyDay, token);
        Assert.Empty(await QueryAsync(fixture, ListQueryKind.MyDay));
        Assert.Equal(0, (await fixture.Store.GetSmartListTaskCountsAsync(token))[ListQueryKind.MyDay]);
        var dismissed = Assert.Single(await QueryAsync(fixture, ListQueryKind.Planned));
        Assert.Equal(timed.Due, dismissed.Due);
        Assert.Equal(fixture.Clock.Today, dismissed.MyDayExcludedDate);

        await fixture.Store.RestoreTaskFieldsAsync(undoRemoval, token);
        Assert.Equal(timed.Uid, Assert.Single(await QueryAsync(fixture, ListQueryKind.MyDay)).Uid);

        await fixture.Store.ApplyTaskBatchAsync([timed.Uid], TaskBatchAction.RemoveFromMyDay, token);
        await fixture.Store.ApplyTaskBatchAsync([timed.Uid], TaskBatchAction.AddToMyDay, token);
        Assert.Null(Assert.Single(await QueryAsync(fixture, ListQueryKind.MyDay)).MyDayExcludedDate);

        await fixture.Store.ApplyTaskBatchAsync([timed.Uid], TaskBatchAction.RemoveFromMyDay, token);
        dismissed = Assert.Single(await QueryAsync(fixture, ListQueryKind.Planned));
        dismissed.Due = fixture.Clock.UtcNow.AddDays(1);
        await fixture.Store.UpdateTaskAsync(dismissed, token);
        fixture.Clock.UtcNow = fixture.Clock.UtcNow.AddDays(1);
        Assert.Equal(timed.Uid, Assert.Single(await QueryAsync(fixture, ListQueryKind.MyDay)).Uid);
    }

    [Theory]
    [InlineData(-7)]
    [InlineData(9)]
    public async Task MyDayCombinesPinsAndDueTasksBeforeFilteringSortingAndLimiting(int offsetHours)
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        TimeSpan offset = TimeSpan.FromHours(offsetHours);
        fixture.Clock.LocalTimeZone = TimeZoneInfo.CreateCustomTimeZone("Test", offset, "Test", "Test");
        var token = TestContext.Current.CancellationToken;
        TodoList list = await fixture.Store.CreateListAsync("Inbox", cancellationToken: token);
        DateOnly today = fixture.Clock.Today;
        var start = new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue), offset);
        var date = new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        Task<TodoTask> Add(string summary, DateTimeOffset? due = null, bool dateOnly = false, bool pin = false) =>
            fixture.Store.CreateTaskAsync(new TodoTask
            {
                ListId = list.Id, Summary = summary, Due = due, DueIsDateOnly = dateOnly,
                MyDayDate = pin ? today : null,
            }, token);

        await Add("C match pin", pin: true);
        await Add("B match both", start, pin: true);
        TodoTask done = await Add("Z match completed", date, dateOnly: true);
        await fixture.Store.CompleteTaskAsync(done.Uid, token);
        await Add("D match timed", start.AddDays(1).AddTicks(-1));
        await Add("No filter hit", date, dateOnly: true);
        await Add("Out of range: match before start", start.AddTicks(-1));
        await Add("Out of range: match next date", date.AddDays(1), dateOnly: true);

        var query = new ListQuery
        {
            Kind = ListQueryKind.MyDay, Filter = "match", SortMode = SortMode.Alphabetical,
            IncludeCompleted = true,
        };
        Assert.Equal(["B match both", "C match pin", "D match timed", "Z match completed"],
            (await fixture.Store.GetTasksAsync(query, token)).Select(task => task.Summary));
        query.IncludeCompleted = false;
        query.SortDescending = true;
        query.Limit = 2;
        Assert.Equal(["D match timed", "C match pin"],
            (await fixture.Store.GetTasksAsync(query, token)).Select(task => task.Summary));
    }

    [Theory]
    [InlineData(3, 29, 30)] // The spring jump skips midnight and ends at 00:30 local.
    [InlineData(10, 25, 0)] // The autumn clock reaches midnight twice, an hour apart.
    public async Task MyDayChangesAtTheFirstInstantOfTheLocalDate(int month, int day, int utcMinute)
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        DateTime start = new(2026, 1, 1);
        DateTime end = new(2026, 12, 31);
        var adjustment = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(start, end, TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 23, 30, 0), 3, 28),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 1, 0, 0), 10, 25));
        fixture.Clock.LocalTimeZone = TimeZoneInfo.CreateCustomTimeZone(
            "Midnight transitions", TimeSpan.FromHours(-1), "Midnight transitions", "Standard", "Daylight",
            [adjustment]);
        var boundary = new DateTimeOffset(2026, month, day, 0, utcMinute, 0, TimeSpan.Zero);
        var token = TestContext.Current.CancellationToken;
        TodoList list = await fixture.Store.CreateListAsync("Inbox", cancellationToken: token);
        TodoTask before = await AddTaskAsync(fixture, list.Id, "Before today's start", 0, boundary.AddTicks(-1));
        TodoTask after = await AddTaskAsync(fixture, list.Id, "At today's start", 0, boundary);
        TodoTask later = await AddTaskAsync(fixture, list.Id, "After the transition", 0, boundary.AddMinutes(75));

        fixture.Clock.UtcNow = boundary.AddTicks(-1);
        Assert.Equal(new DateOnly(2026, month, day).AddDays(-1), fixture.Clock.Today);
        Assert.Equal(before.Uid, Assert.Single(await QueryAsync(fixture, ListQueryKind.MyDay)).Uid);
        Assert.Equal(1, (await fixture.Store.GetSmartListTaskCountsAsync(token))[ListQueryKind.MyDay]);

        fixture.Clock.UtcNow = boundary;
        Assert.Equal(new DateOnly(2026, month, day), fixture.Clock.Today);
        Assert.Equal([after.Uid, later.Uid], (await QueryAsync(fixture, ListQueryKind.MyDay)).Select(task => task.Uid));
        Assert.Equal(2, (await fixture.Store.GetSmartListTaskCountsAsync(token))[ListQueryKind.MyDay]);
    }

    [Fact]
    public async Task SmartListsAndSorts_ReturnExpectedTopLevelTasks()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        TodoList list = await fixture.Store.CreateListAsync("Inbox", cancellationToken: TestContext.Current.CancellationToken);
        TodoTask today = await AddTaskAsync(
            fixture,
            list.Id,
            "Today",
            priority: 0,
            due: null,
            myDay: fixture.Clock.Today);
        TodoTask important = await AddTaskAsync(
            fixture,
            list.Id,
            "Important",
            priority: 1,
            due: null);
        TodoTask planned = await AddTaskAsync(
            fixture,
            list.Id,
            "Planned",
            priority: 0,
            due: fixture.Clock.UtcNow.AddDays(1));
        TodoTask completed = await AddTaskAsync(
            fixture,
            list.Id,
            "Completed",
            priority: 0,
            due: null);
        await fixture.Store.CompleteTaskAsync(completed.Uid, TestContext.Current.CancellationToken);
        await fixture.Store.AddStepAsync(today.Uid, "Hidden child", TestContext.Current.CancellationToken);

        Assert.Equal(today.Uid, Assert.Single(await QueryAsync(fixture, ListQueryKind.MyDay)).Uid);
        Assert.Equal(important.Uid, Assert.Single(await QueryAsync(fixture, ListQueryKind.Important)).Uid);
        Assert.Equal(planned.Uid, Assert.Single(await QueryAsync(fixture, ListQueryKind.Planned)).Uid);
        Assert.Equal(completed.Uid, Assert.Single(await QueryAsync(fixture, ListQueryKind.Completed)).Uid);

        IReadOnlyList<TodoTask> active = await fixture.Store.GetTasksAsync(new ListQuery
        {
            Kind = ListQueryKind.All,
            IncludeCompleted = false,
            SortMode = SortMode.Alphabetical,
            Filter = "a",
            Limit = 10,
        }, TestContext.Current.CancellationToken);
        Assert.Equal(["Important", "Planned", "Today"], active.Select(item => item.Summary));

        IReadOnlyList<TodoTask> listTasks = await fixture.Store.GetTasksAsync(ListQuery.ForList(list.Id), TestContext.Current.CancellationToken);
        Assert.Equal(4, listTasks.Count);
    }

    [Fact]
    public async Task CompleteAndUncomplete_NonRecurringTask_UpdatesStatus()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        TodoList list = await fixture.Store.CreateListAsync("Inbox", cancellationToken: TestContext.Current.CancellationToken);
        TodoTask task = await AddTaskAsync(fixture, list.Id, "One-off", 0, null);

        await fixture.Store.CompleteTaskAsync(task.Uid, TestContext.Current.CancellationToken);
        TodoTask completed = Assert.IsType<TodoTask>(await fixture.Store.GetTaskAsync(task.Uid, TestContext.Current.CancellationToken));
        Assert.Equal(Wintodo.Core.Models.TaskStatus.Completed, completed.Status);
        Assert.Equal(100, completed.PercentComplete);
        Assert.Equal(fixture.Clock.UtcNow, completed.CompletedAt);

        fixture.Clock.UtcNow = fixture.Clock.UtcNow.AddMinutes(5);
        await fixture.Store.UncompleteTaskAsync(task.Uid, TestContext.Current.CancellationToken);
        TodoTask reopened = Assert.IsType<TodoTask>(await fixture.Store.GetTaskAsync(task.Uid, TestContext.Current.CancellationToken));
        Assert.Equal(Wintodo.Core.Models.TaskStatus.NeedsAction, reopened.Status);
        Assert.Equal(0, reopened.PercentComplete);
        Assert.Null(reopened.CompletedAt);
    }

    [Fact]
    public async Task InvalidTaskData_IsRejected()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        TodoList list = await fixture.Store.CreateListAsync("Inbox", cancellationToken: TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<ArgumentException>(
            () => fixture.Store.CreateTaskAsync(new TodoTask { ListId = list.Id }, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => fixture.Store.CreateTaskAsync(new TodoTask
            {
                ListId = list.Id,
                Summary = "Bad priority",
                Priority = 10,
            }, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => fixture.Store.MoveTaskAsync("missing", list.Id, cancellationToken: TestContext.Current.CancellationToken));
    }

    private static async Task<TodoTask> AddTaskAsync(
        StoreFixture fixture,
        long listId,
        string summary,
        int priority,
        DateTimeOffset? due,
        DateOnly? myDay = null)
    {
        return await fixture.Store.CreateTaskAsync(new TodoTask
        {
            ListId = listId,
            Summary = summary,
            Priority = priority,
            Due = due,
            MyDayDate = myDay,
        });
    }

    private static Task<IReadOnlyList<TodoTask>> QueryAsync(
        StoreFixture fixture,
        ListQueryKind kind)
    {
        return fixture.Store.GetTasksAsync(ListQuery.ForSmartList(kind));
    }
}
