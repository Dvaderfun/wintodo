using Wintodo.Core.Presentation;

namespace Wintodo.Core.Tests.Presentation;

public sealed class TaskReorderingTests
{
    [Theory]
    [InlineData(4, 2, false, "ABECD")]
    [InlineData(4, 2, true, "ABCED")]
    [InlineData(0, 2, false, "BACDE")]
    [InlineData(0, 2, true, "BCADE")]
    [InlineData(0, 4, true, "BCDEA")]
    [InlineData(4, 0, false, "EABCD")]
    [InlineData(2, 2, false, "ABCDE")]
    [InlineData(2, 2, true, "ABCDE")]
    [InlineData(2, 1, true, "ABCDE")]
    [InlineData(2, 3, false, "ABCDE")]
    public void InsertionBoundary_ProducesExpectedOrder(
        int source, int target, bool after, string expected)
    {
        var items = new List<char>("ABCDE");
        char moved = items[source];
        items.RemoveAt(source);
        items.Insert(TaskReordering.GetDestinationIndex(source, target, after), moved);
        Assert.Equal(expected, new string(items.ToArray()));
    }
}
