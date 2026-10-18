using Wintodo.Core.Models;
using Wintodo.Core.Presentation;
using Wintodo.Core.Queries;

namespace Wintodo.Data.Tests;

public sealed class DefaultSortPersistenceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SortAndReset_SurviveStoreReopenAndRestoreManualTaskOrder(bool isDefaultList)
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        CancellationToken token = TestContext.Current.CancellationToken;
        TodoList list = isDefaultList
            ? await fixture.Store.EnsureDefaultListAsync("Tasks", token)
            : await fixture.Store.CreateListAsync("Custom", cancellationToken: token);
        TodoTask first = await fixture.Store.CreateTaskAsync(new() { ListId = list.Id, Summary = "Zulu" }, token);
        TodoTask second = await fixture.Store.CreateTaskAsync(new() { ListId = list.Id, Summary = "Alpha" }, token);
        TodoTask third = await fixture.Store.CreateTaskAsync(new() { ListId = list.Id, Summary = "Middle" }, token);
        await fixture.Store.SetListSortAsync(list.Id, SortMode.Alphabetical, true, token);

        await using (var reopened = new SqliteTaskStore(fixture.DatabasePath, fixture.Clock))
        {
            await reopened.InitializeAsync(token);
            TodoList saved = Assert.Single(await reopened.GetListsAsync(token), item => item.Id == list.Id);
            Assert.Equal(SortMode.Alphabetical, saved.SortMode);
            Assert.True(saved.SortDescending);
            Assert.False(TaskSorting.IsDefault(saved.SortMode, saved.SortDescending, ListQueryKind.UserList));
            var query = new ListQuery { ListId = list.Id, SortMode = saved.SortMode, SortDescending = saved.SortDescending };
            Assert.Equal(new[] { first.Uid, third.Uid, second.Uid },
                (await reopened.GetTasksAsync(query, token)).Select(task => task.Uid));
            await reopened.SetListSortAsync(list.Id, TaskSorting.GetDefaultMode(ListQueryKind.UserList), false, token);
        }

        await using var resetStore = new SqliteTaskStore(fixture.DatabasePath, fixture.Clock);
        await resetStore.InitializeAsync(token);
        TodoList reset = Assert.Single(await resetStore.GetListsAsync(token), item => item.Id == list.Id);
        Assert.Equal(SortMode.Manual, reset.SortMode);
        Assert.False(reset.SortDescending);
        Assert.True(TaskSorting.IsDefault(reset.SortMode, reset.SortDescending, ListQueryKind.UserList));
        Assert.Equal(new[] { first.Uid, second.Uid, third.Uid },
            (await resetStore.GetTasksAsync(ListQuery.ForList(list.Id), token)).Select(task => task.Uid));
    }
}
