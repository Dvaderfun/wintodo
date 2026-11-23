using Wintodo.Core.Models;
using Wintodo.Core.Queries;

namespace Wintodo.Data.Tests;

public sealed class TaskRowPersistenceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EditingProjectedRowPreservesTaskData(bool star)
    {
        var ct = TestContext.Current.CancellationToken;
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        TodoList list = await fixture.Store.CreateListAsync("Manual list", cancellationToken: ct);
        var first = await fixture.Store.CreateTaskAsync(new TodoTask { ListId = list.Id, Summary = "First" }, ct);
        var task = new TodoTask
        {
            ListId = list.Id,
            Summary = "Original title",
            Description = "Keep this note",
            IcalUnknown = "X-OWNER:keep",
        };
        task.Categories.Add("Work");
        task.Alarms.Add(new TaskAlarm { Trigger = fixture.Clock.UtcNow.AddHours(1) });
        task.Attachments.Add(new TaskAttachment { FileName = "fixture.txt", RelativePath = "fixture.txt", SizeBytes = 12 });
        await fixture.Store.CreateTaskAsync(task, ct);
        var query = ListQuery.ForList(list.Id);
        var projection = await fixture.Store.GetTasksAsync(query, ct);
        Assert.Equal([first.Uid, task.Uid], projection.Select(row => row.Uid));
        TodoTask row = projection[1];
        Assert.Empty(row.Alarms);
        Assert.Empty(row.Categories);
        Assert.Empty(row.Attachments);
        TodoTask before = (await fixture.Store.GetTaskAsync(task.Uid, ct))!;
        fixture.Clock.UtcNow = fixture.Clock.UtcNow.AddMinutes(1);

        if (star)
        {
            double sortOrder = await fixture.Store.SetTaskImportanceAsync(row.Uid, true, moveStarredToTop: true, ct);
            Assert.True(sortOrder < first.SortOrder);
        }
        else
        {
            await fixture.Store.RenameTaskAsync(row.Uid, "Renamed title", ct);
        }

        await fixture.Store.DisposeAsync();
        await using var reopened = new SqliteTaskStore(fixture.DatabasePath, fixture.Clock);
        await reopened.InitializeAsync(ct);
        TodoTask after = (await reopened.GetTaskAsync(task.Uid, ct))!;
        Assert.Equal(star ? before.Summary : "Renamed title", after.Summary);
        Assert.Equal(star ? (int)TaskPriority.High : before.Priority, after.Priority);
        Assert.Equal(before.Description, after.Description);
        Assert.Equal(before.IcalUnknown, after.IcalUnknown);
        Assert.Equal(before.Created, after.Created);
        Assert.Equal(before.ListId, after.ListId);
        Assert.Equal(before.Categories, after.Categories);
        Assert.Equal(Assert.Single(before.Alarms).Id, Assert.Single(after.Alarms).Id);
        Assert.Equal(Assert.Single(before.Attachments).Id, Assert.Single(after.Attachments).Id);
        Assert.Equal(before.Sequence + 1, after.Sequence);
        Assert.Equal(fixture.Clock.UtcNow, after.LastModified);
        Assert.True(after.IsDirty);
        var reordered = await reopened.GetTasksAsync(query, ct);
        Assert.Equal(star ? [task.Uid, first.Uid] : new[] { first.Uid, task.Uid }, reordered.Select(entry => entry.Uid));
        if (!star) Assert.Equal(before.SortOrder, after.SortOrder);
    }
}
