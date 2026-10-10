using Wintodo.Core.Models;
using Wintodo.Core.Queries;

namespace Wintodo.Data.Tests;

public sealed class TaskOrderingPreferencesTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NewTasks_AppendByDefault_AndRepeatedTopInsertsPrepend(bool descending)
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        TodoList list = await fixture.Store.CreateListAsync("Manual", cancellationToken: ct);
        TodoTask first = await fixture.Store.CreateTaskAsync(new TodoTask { ListId = list.Id, Summary = "First" }, ct);
        TodoTask second = await fixture.Store.CreateTaskAsync(new TodoTask { ListId = list.Id, Summary = "Second" }, ct);
        await fixture.Store.SetListSortAsync(list.Id, SortMode.Manual, descending, ct);
        TodoTask third = await fixture.Store.CreateTaskAsync(new TodoTask { ListId = list.Id, Summary = "Third" }, ct, addToTop: true);
        TodoTask fourth = await fixture.Store.CreateTaskAsync(new TodoTask { ListId = list.Id, Summary = "Fourth" }, ct, addToTop: true);
        IReadOnlyList<TodoTask> tasks = await fixture.Store.GetTasksAsync(
            new ListQuery { Kind = ListQueryKind.UserList, ListId = list.Id, SortMode = SortMode.Manual, SortDescending = descending }, ct);
        Assert.Equal(descending ? [fourth.Uid, third.Uid, second.Uid, first.Uid]
            : new[] { fourth.Uid, third.Uid, first.Uid, second.Uid }, tasks.Select(task => task.Uid));
        Assert.True(descending ? fourth.SortOrder > third.SortOrder : fourth.SortOrder < third.SortOrder);
        TodoTask saved = (await fixture.Store.GetTaskAsync(fourth.Uid, ct))!;
        Assert.Equal(fourth.SortOrder, saved.SortOrder);
    }

    [Fact]
    public async Task Starring_PrependsOnlyOnTransition_WhenEnabled_AndOtherEditsKeepOrder()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        TodoList list = await fixture.Store.CreateListAsync("Manual", cancellationToken: ct);
        TodoTask first = await fixture.Store.CreateTaskAsync(new TodoTask { ListId = list.Id, Summary = "First" }, ct);
        TodoTask second = await fixture.Store.CreateTaskAsync(new TodoTask { ListId = list.Id, Summary = "Second" }, ct);
        second.Priority = 1;
        await fixture.Store.UpdateTaskAsync(second, ct);
        Assert.True(second.SortOrder > first.SortOrder);
        second.Priority = 0;
        await fixture.Store.UpdateTaskAsync(second, ct, moveStarredToTop: true);
        second.Priority = 1;
        await fixture.Store.UpdateTaskAsync(second, ct, moveStarredToTop: true);
        Assert.True(second.SortOrder < first.SortOrder);
        double order = second.SortOrder;
        second.Summary = "Edited";
        await fixture.Store.UpdateTaskAsync(second, ct, moveStarredToTop: true);
        Assert.Equal(order, second.SortOrder);
        Assert.Equal(order, (await fixture.Store.GetTaskAsync(second.Uid, ct))!.SortOrder);
        first.Priority = 1;
        await fixture.Store.UpdateTaskAsync(first, ct, moveStarredToTop: true);
        Assert.True(first.SortOrder < second.SortOrder);
        second.Priority = 0;
        await fixture.Store.UpdateTaskAsync(second, ct, moveStarredToTop: true);
        Assert.Equal(order, second.SortOrder);
    }

    [Fact]
    public async Task OrderingPreferences_DoNotReorderAutomaticListsOrSteps()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        TodoList list = await fixture.Store.CreateListAsync("Automatic", cancellationToken: ct);
        await fixture.Store.SetListSortAsync(list.Id, SortMode.Alphabetical, false, ct);
        TodoTask first = await fixture.Store.CreateTaskAsync(new TodoTask { ListId = list.Id, Summary = "A" }, ct);
        TodoTask second = await fixture.Store.CreateTaskAsync(new TodoTask { ListId = list.Id, Summary = "B" }, ct, addToTop: true);
        double order = second.SortOrder;
        second.Priority = 1;
        await fixture.Store.UpdateTaskAsync(second, ct, moveStarredToTop: true);
        Assert.True(order > first.SortOrder);
        Assert.Equal(order, second.SortOrder);
        await fixture.Store.SetListSortAsync(list.Id, SortMode.Manual, false, ct);
        TodoTask step = await fixture.Store.AddStepAsync(first.Uid, "Step", ct);
        TodoTask nextStep = await fixture.Store.CreateTaskAsync(
            new TodoTask { ListId = list.Id, ParentUid = first.Uid, Summary = "Next step" }, ct, addToTop: true);
        order = nextStep.SortOrder;
        nextStep.Priority = 1;
        await fixture.Store.UpdateTaskAsync(nextStep, ct, moveStarredToTop: true);
        Assert.True(order > step.SortOrder);
        Assert.Equal(order, nextStep.SortOrder);
    }
}
