using Wintodo.Core.Models;

namespace Wintodo.Core.Queries;

public enum ListQueryKind
{
    UserList = 0,
    MyDay,
    Important,
    Planned,
    All,
    Completed,
}

public sealed class ListQuery
{
    public ListQueryKind Kind { get; set; }

    public long? ListId { get; set; }

    public SortMode SortMode { get; set; } = SortMode.Manual;

    public bool SortDescending { get; set; }

    public bool IncludeCompleted { get; set; } = true;

    public string? Filter { get; set; }

    public int? Limit { get; set; }

    public static ListQuery ForList(long listId)
    {
        return new ListQuery
        {
            Kind = ListQueryKind.UserList,
            ListId = listId,
        };
    }

    public static ListQuery ForSmartList(ListQueryKind kind)
    {
        if (kind == ListQueryKind.UserList)
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "A user-list query requires a list id.");
        }

        return new ListQuery { Kind = kind };
    }
}
