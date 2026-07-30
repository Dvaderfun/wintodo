using Wintodo.Core.Models;
using Wintodo.Core.Queries;

namespace Wintodo.Data.Tests;

public sealed class TaskStoreTests
{
    [Fact]
    public async Task TaskLifecycle_RoundTripsAllOwnedCollectionsAndSearches()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        TodoList source = await fixture.Store.CreateListAsync("Source");
        TodoList destination = await fixture.Store.CreateListAsync("Destination");
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

        TodoTask created = await fixture.Store.CreateTaskAsync(task);
        TodoTask stored = Assert.IsType<TodoTask>(await fixture.Store.GetTaskAsync(created.Uid));

        Assert.True(stored.Id > 0);
        Assert.Equal(source.Id, stored.ListId);
        Assert.Equal("Prepare quarterly report", stored.Summary);
        Assert.Equal(2, stored.Categories.Count);
        Assert.Single(stored.Alarms);
        Assert.Single(stored.Attachments);
        Assert.True(stored.IsDirty);
        Assert.Single(await fixture.Store.SearchAsync("quarter rep"));

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
        await fixture.Store.UpdateTaskAsync(stored);

        TodoTask updated = Assert.IsType<TodoTask>(await fixture.Store.GetTaskAsync(created.Uid));
        Assert.Equal("Publish quarterly report", updated.Summary);
        Assert.Equal("Published", Assert.Single(updated.Categories));
        Assert.Equal(1, updated.Sequence);
        Assert.Single(updated.Alarms);
        Assert.Single(updated.Attachments);
        Assert.Single(await fixture.Store.SearchAsync("reviewed"));

        await fixture.Store.ReorderTaskAsync(updated.Uid, 42.5);
        await fixture.Store.MoveTaskAsync(updated.Uid, destination.Id);
        TodoTask moved = Assert.IsType<TodoTask>(await fixture.Store.GetTaskAsync(updated.Uid));
        Assert.Equal(destination.Id, moved.ListId);
        Assert.Equal(1, moved.SortOrder);

        await fixture.Store.DeleteTaskAsync(moved.Uid);
        Assert.Null(await fixture.Store.GetTaskAsync(moved.Uid));
        await fixture.Store.RestoreTaskAsync(moved.Uid);
        Assert.NotNull(await fixture.Store.GetTaskAsync(moved.Uid));
    }

    [Fact]
    public async Task Steps_AreOrderedAndRemainInParentList()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        TodoList list = await fixture.Store.CreateListAsync("Project");
        TodoTask parent = await fixture.Store.CreateTaskAsync(new TodoTask
        {
            ListId = list.Id,
            Summary = "Ship release",
        });

        TodoTask first = await fixture.Store.AddStepAsync(parent.Uid, "Build");
        TodoTask second = await fixture.Store.AddStepAsync(parent.Uid, "Test");
        await fixture.Store.ReorderStepAsync(second.Uid, 0.5);

        IReadOnlyList<TodoTask> children = await fixture.Store.GetChildrenAsync(parent.Uid);
        Assert.Equal([second.Uid, first.Uid], children.Select(item => item.Uid));
        Assert.All(children, child =>
        {
            Assert.Equal(parent.Uid, child.ParentUid);
            Assert.Equal(list.Id, child.ListId);
        });
    }

    [Fact]
    public async Task SmartListsAndSorts_ReturnExpectedTopLevelTasks()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        TodoList list = await fixture.Store.CreateListAsync("Inbox");
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
        await fixture.Store.CompleteTaskAsync(completed.Uid);
        await fixture.Store.AddStepAsync(today.Uid, "Hidden child");

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
        });
        Assert.Equal(["Important", "Planned", "Today"], active.Select(item => item.Summary));

        IReadOnlyList<TodoTask> listTasks = await fixture.Store.GetTasksAsync(ListQuery.ForList(list.Id));
        Assert.Equal(4, listTasks.Count);
    }

    [Fact]
    public async Task CompleteAndUncomplete_NonRecurringTask_UpdatesStatus()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        TodoList list = await fixture.Store.CreateListAsync("Inbox");
        TodoTask task = await AddTaskAsync(fixture, list.Id, "One-off", 0, null);

        await fixture.Store.CompleteTaskAsync(task.Uid);
        TodoTask completed = Assert.IsType<TodoTask>(await fixture.Store.GetTaskAsync(task.Uid));
        Assert.Equal(Wintodo.Core.Models.TaskStatus.Completed, completed.Status);
        Assert.Equal(100, completed.PercentComplete);
        Assert.Equal(fixture.Clock.UtcNow, completed.CompletedAt);

        fixture.Clock.UtcNow = fixture.Clock.UtcNow.AddMinutes(5);
        await fixture.Store.UncompleteTaskAsync(task.Uid);
        TodoTask reopened = Assert.IsType<TodoTask>(await fixture.Store.GetTaskAsync(task.Uid));
        Assert.Equal(Wintodo.Core.Models.TaskStatus.NeedsAction, reopened.Status);
        Assert.Equal(0, reopened.PercentComplete);
        Assert.Null(reopened.CompletedAt);
    }

    [Fact]
    public async Task InvalidTaskData_IsRejected()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        TodoList list = await fixture.Store.CreateListAsync("Inbox");

        await Assert.ThrowsAsync<ArgumentException>(
            () => fixture.Store.CreateTaskAsync(new TodoTask { ListId = list.Id }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => fixture.Store.CreateTaskAsync(new TodoTask
            {
                ListId = list.Id,
                Summary = "Bad priority",
                Priority = 10,
            }));
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => fixture.Store.MoveTaskAsync("missing", list.Id));
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
