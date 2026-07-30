using Wintodo.Core.Queries;

namespace Wintodo.Core.Tests;

public sealed class ListQueryTests
{
    [Fact]
    public void ForList_CreatesUserListQuery()
    {
        ListQuery query = ListQuery.ForList(42);

        Assert.Equal(ListQueryKind.UserList, query.Kind);
        Assert.Equal(42, query.ListId);
    }

    [Fact]
    public void ForSmartList_RejectsUserList()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ListQuery.ForSmartList(ListQueryKind.UserList));
    }
}
