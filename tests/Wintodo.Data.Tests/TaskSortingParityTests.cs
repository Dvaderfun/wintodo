using Wintodo.Core.Models;
using Wintodo.Core.Presentation;
using Wintodo.Core.Queries;

namespace Wintodo.Data.Tests;

public sealed class TaskSortingParityTests
{
    [Theory]
    [InlineData(SortMode.Manual, false, ListQueryKind.UserList)]
    [InlineData(SortMode.Manual, true, ListQueryKind.UserList)]
    [InlineData(SortMode.Importance, false, ListQueryKind.UserList)]
    [InlineData(SortMode.Importance, true, ListQueryKind.UserList)]
    [InlineData(SortMode.DueDate, false, ListQueryKind.UserList)]
    [InlineData(SortMode.DueDate, true, ListQueryKind.UserList)]
    [InlineData(SortMode.Alphabetical, false, ListQueryKind.UserList)]
    [InlineData(SortMode.Alphabetical, true, ListQueryKind.UserList)]
    [InlineData(SortMode.CreationDate, false, ListQueryKind.UserList)]
    [InlineData(SortMode.CreationDate, true, ListQueryKind.UserList)]
    [InlineData(SortMode.Importance, false, ListQueryKind.Important)]
    [InlineData(SortMode.Importance, true, ListQueryKind.Important)]
    [InlineData(SortMode.DueDate, false, ListQueryKind.Planned)]
    [InlineData(SortMode.DueDate, true, ListQueryKind.Planned)]
    [InlineData(SortMode.CreationDate, false, ListQueryKind.Completed)]
    [InlineData(SortMode.CreationDate, true, ListQueryKind.Completed)]
    public async Task CoreOrdering_MatchesSqliteQueries(SortMode mode, bool reverse, ListQueryKind kind)
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        TodoList list = await fixture.Store.CreateListAsync("Sort parity", cancellationToken: TestContext.Current.CancellationToken);
        string[] names = ["alpha", "ALPHA", "Ä", "ä", "Beta", "undated", "timed", "date-only"];
        int[] priorities = [1, 1, 0, 9, 4, 0, 3, 2];
        for (int index = 0; index < names.Length; index++)
        {
            fixture.Clock.UtcNow = DateTimeOffset.UnixEpoch.AddHours(index / 2);
            TodoTask task = await fixture.Store.CreateTaskAsync(new()
            {
                ListId = list.Id, Summary = names[index], Priority = priorities[index],
                Due = index == 5 ? null : DateTimeOffset.UnixEpoch.AddDays(index / 2),
                DueIsDateOnly = index == 7,
            }, cancellationToken: TestContext.Current.CancellationToken);
            if (kind == ListQueryKind.Completed)
            {
                await fixture.Store.CompleteTaskAsync(task.Uid, TestContext.Current.CancellationToken);
            }
        }
        var query = new ListQuery { Kind = kind, ListId = list.Id, SortMode = mode, SortDescending = reverse };
        IReadOnlyList<TodoTask> actual = await fixture.Store.GetTasksAsync(query, TestContext.Current.CancellationToken);
        TodoTask[] expected = actual.Reverse().ToArray();
        Array.Sort(expected, TaskSorting.Compare(mode, reverse, kind));
        Assert.Equal(actual.Select(task => task.Uid), expected.Select(task => task.Uid));
    }
}
