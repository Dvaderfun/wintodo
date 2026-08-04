using Wintodo.Core.Models;

namespace Wintodo.Data.Tests;

public sealed class DefaultListTests
{
    [Fact]
    public async Task EnsureDefaultListAsync_CreatesOnceAndStaysFirst()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();

        TodoList first = await fixture.Store.EnsureDefaultListAsync("Tasks");
        TodoList second = await fixture.Store.EnsureDefaultListAsync("Tasks");

        Assert.True(first.IsDefault);
        Assert.Equal(first.Id, second.Id);
        Assert.True(second.IsDefault);

        await fixture.Store.CreateListAsync("M3 Gate");
        IReadOnlyList<TodoList> lists = await fixture.Store.GetListsAsync();
        Assert.Equal(2, lists.Count);
        Assert.Equal(first.Id, lists[0].Id);
        Assert.False(lists[1].IsDefault);
    }

    [Fact]
    public async Task DeleteListAsync_RejectsDefaultListButAllowsOthers()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        TodoList defaultList = await fixture.Store.EnsureDefaultListAsync("Tasks");
        TodoList userList = await fixture.Store.CreateListAsync("M3 Gate");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Store.DeleteListAsync(defaultList.Id));

        await fixture.Store.DeleteListAsync(userList.Id);
        TodoList remaining = Assert.Single(await fixture.Store.GetListsAsync());
        Assert.Equal(defaultList.Id, remaining.Id);
    }

    [Fact]
    public async Task SetListIconEmojiAsync_RejectsDefaultList()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        TodoList defaultList = await fixture.Store.EnsureDefaultListAsync("Tasks");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Store.SetListIconEmojiAsync(defaultList.Id, "\U0001F3E0"));

        Assert.Null(Assert.Single(await fixture.Store.GetListsAsync()).IconEmoji);
    }

    [Fact]
    public async Task EnsureDefaultListAsync_AfterExistingListsKeepsSingleDefault()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        await fixture.Store.CreateListAsync("M3 Gate");

        TodoList created = await fixture.Store.EnsureDefaultListAsync("Tasks");

        IReadOnlyList<TodoList> lists = await fixture.Store.GetListsAsync();
        Assert.Equal(created.Id, Assert.Single(lists, list => list.IsDefault).Id);
        Assert.Equal("Tasks", lists[0].DisplayName);
    }
}
