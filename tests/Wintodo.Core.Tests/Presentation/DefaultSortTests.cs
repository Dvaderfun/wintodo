using Wintodo.Core.Models;
using Wintodo.Core.Presentation;
using Wintodo.Core.Queries;

namespace Wintodo.Core.Tests.Presentation;

public sealed class DefaultSortTests
{
    [Theory]
    [InlineData(ListQueryKind.UserList, SortMode.Manual)]
    [InlineData(ListQueryKind.MyDay, SortMode.Manual)]
    [InlineData(ListQueryKind.Important, SortMode.Importance)]
    [InlineData(ListQueryKind.Planned, SortMode.DueDate)]
    [InlineData(ListQueryKind.All, SortMode.CreationDate)]
    [InlineData(ListQueryKind.Completed, SortMode.CreationDate)]
    public void Defaults_MatchShippedOrderingAndReversalIsNonDefault(ListQueryKind kind, SortMode expected)
    {
        Assert.Equal(expected, TaskSorting.GetDefaultMode(kind));
        Assert.True(TaskSorting.IsDefault(expected, false, kind));
        Assert.False(TaskSorting.IsDefault(expected, true, kind));
    }

    [Theory]
    [InlineData(ListQueryKind.UserList)]
    [InlineData(ListQueryKind.MyDay)]
    [InlineData(ListQueryKind.Important)]
    [InlineData(ListQueryKind.Planned)]
    [InlineData(ListQueryKind.All)]
    public void DifferentMode_IsNonDefaultUntilBothModeAndDirectionReset(ListQueryKind kind)
    {
        SortMode baseline = TaskSorting.GetDefaultMode(kind);
        Assert.False(TaskSorting.IsDefault(SortMode.Alphabetical, false, kind));
        Assert.False(TaskSorting.IsDefault(baseline, true, kind));
        Assert.True(TaskSorting.IsDefault(baseline, false, kind));
    }

    [Theory]
    [InlineData(SortMode.Manual)]
    [InlineData(SortMode.Importance)]
    [InlineData(SortMode.DueDate)]
    [InlineData(SortMode.Alphabetical)]
    [InlineData(SortMode.CreationDate)]
    public void Completed_IgnoredModeDoesNotShowAnIndicator(SortMode mode)
    {
        Assert.True(TaskSorting.IsDefault(mode, false, ListQueryKind.Completed));
        Assert.False(TaskSorting.IsDefault(mode, true, ListQueryKind.Completed));
    }
}
