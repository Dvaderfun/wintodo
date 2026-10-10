using Wintodo.Core.Models;

namespace Wintodo.Data.Tests;

public sealed class DefaultListTests
{
    [Fact]
    public async Task EnsureDefaultListAsync_CreatesOnceAndStaysFirst()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();

        TodoList first = await fixture.Store.EnsureDefaultListAsync("Tasks", TestContext.Current.CancellationToken);
        TodoList second = await fixture.Store.EnsureDefaultListAsync("Tasks", TestContext.Current.CancellationToken);

        Assert.True(first.IsDefault);
        Assert.Equal(first.Id, second.Id);
        Assert.True(second.IsDefault);

        await fixture.Store.CreateListAsync("M3 Gate", cancellationToken: TestContext.Current.CancellationToken);
        IReadOnlyList<TodoList> lists = await fixture.Store.GetListsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, lists.Count);
        Assert.Equal(first.Id, lists[0].Id);
        Assert.False(lists[1].IsDefault);
    }

    [Fact]
    public async Task DeleteListAsync_RejectsDefaultListButAllowsOthers()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        TodoList defaultList = await fixture.Store.EnsureDefaultListAsync("Tasks", TestContext.Current.CancellationToken);
        TodoList userList = await fixture.Store.CreateListAsync("M3 Gate", cancellationToken: TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Store.DeleteListAsync(defaultList.Id, TestContext.Current.CancellationToken));

        await fixture.Store.DeleteListAsync(userList.Id, TestContext.Current.CancellationToken);
        TodoList remaining = Assert.Single(await fixture.Store.GetListsAsync(TestContext.Current.CancellationToken));
        Assert.Equal(defaultList.Id, remaining.Id);
    }

    [Fact]
    public async Task SetListIconEmojiAsync_RejectsDefaultList()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        TodoList defaultList = await fixture.Store.EnsureDefaultListAsync("Tasks", TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Store.SetListIconEmojiAsync(defaultList.Id, "\U0001F3E0", TestContext.Current.CancellationToken));

        Assert.Null(Assert.Single(await fixture.Store.GetListsAsync(TestContext.Current.CancellationToken)).IconEmoji);
    }

    [Fact]
    public async Task EnsureDefaultListAsync_AfterExistingListsKeepsSingleDefault()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        await fixture.Store.CreateListAsync("M3 Gate", cancellationToken: TestContext.Current.CancellationToken);

        TodoList created = await fixture.Store.EnsureDefaultListAsync("Tasks", TestContext.Current.CancellationToken);

        IReadOnlyList<TodoList> lists = await fixture.Store.GetListsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(created.Id, Assert.Single(lists, list => list.IsDefault).Id);
        Assert.Equal("Tasks", lists[0].DisplayName);
    }
}
