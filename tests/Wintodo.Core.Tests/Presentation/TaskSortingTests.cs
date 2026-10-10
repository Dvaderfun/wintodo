using Wintodo.Core.Models;
using Wintodo.Core.Presentation;
using Wintodo.Core.Queries;

namespace Wintodo.Core.Tests.Presentation;

public sealed class TaskSortingTests
{
    [Theory]
    [InlineData(SortMode.Manual, false)]
    [InlineData(SortMode.Manual, true)]
    [InlineData(SortMode.Importance, false)]
    [InlineData(SortMode.Importance, true)]
    [InlineData(SortMode.DueDate, false)]
    [InlineData(SortMode.DueDate, true)]
    [InlineData(SortMode.Alphabetical, false)]
    [InlineData(SortMode.Alphabetical, true)]
    [InlineData(SortMode.CreationDate, false)]
    [InlineData(SortMode.CreationDate, true)]
    public void EverySort_UsesPrimaryValueAndReversesIdTies(SortMode mode, bool reverse)
    {
        var low = new TodoTask { Id = 1, SortOrder = 1, Priority = 1, Summary = "alpha",
            Due = DateTimeOffset.UnixEpoch, Created = DateTimeOffset.UnixEpoch };
        var high = new TodoTask { Id = 2, SortOrder = 2, Priority = 5, Summary = "beta",
            Due = DateTimeOffset.UnixEpoch.AddDays(1), Created = DateTimeOffset.UnixEpoch.AddDays(1) };
        Comparison<TodoTask> compare = TaskSorting.Compare(mode, reverse);
        Assert.Equal(reverse ? 1 : -1, compare(low, high));
        low.SortOrder = high.SortOrder;
        low.Priority = high.Priority;
        low.Summary = high.Summary;
        low.Due = high.Due;
        low.Created = high.Created;
        Assert.Equal(reverse ? 1 : -1, compare(low, high));
        Assert.Equal(0, compare(low, low));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinarySort_KeepsUndefinedPriorityAndMissingDueLastEvenInReverse(bool reverse)
    {
        Assert.True(TaskSorting.Compare(SortMode.Importance, reverse)(new() { Priority = 1 }, new() { Priority = 0 }) < 0);
        Assert.True(TaskSorting.Compare(SortMode.DueDate, reverse)(new() { Due = DateTimeOffset.UnixEpoch }, new()) < 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SmartSort_UsesRawPriorityAndNullableDue(bool reverse)
    {
        Assert.Equal(reverse ? 1 : -1, TaskSorting.Compare(SortMode.Importance, reverse, ListQueryKind.Important)(
            new() { Priority = 0 }, new() { Priority = 1 }));
        Assert.Equal(reverse ? 1 : -1, TaskSorting.Compare(SortMode.DueDate, reverse, ListQueryKind.Planned)(
            new(), new() { Due = DateTimeOffset.UnixEpoch }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Completed_DefaultIsNewestFirstAndIgnoresChosenMode(bool reverse)
    {
        Comparison<TodoTask> compare = TaskSorting.Compare(SortMode.Alphabetical, reverse, ListQueryKind.Completed);
        Assert.Equal(reverse ? -1 : 1, compare(new() { CompletedAt = DateTimeOffset.UnixEpoch },
            new() { CompletedAt = DateTimeOffset.UnixEpoch.AddDays(1) }));
        Assert.Equal(reverse ? -1 : 1, compare(new() { Id = 1 }, new() { Id = 2 }));
    }

    [Theory]
    [InlineData("ALPHA", "alpha", 0)]
    [InlineData("alpha", "alphabet", -1)]
    [InlineData("Ä", "ä", -1)]
    [InlineData("\uE000", "😀", -1)]
    public void Alphabetical_MatchesSqliteAsciiOnlyNoCase(string a, string b, int expected) =>
        Assert.Equal(expected, TaskSorting.Compare(SortMode.Alphabetical, false)(new() { Summary = a }, new() { Summary = b }));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ManualInsertion_DoesNotBreakEqualOrderTies(bool reverse)
    {
        Assert.Equal(0, TaskSorting.CompareManualOrder(1, 1, reverse));
        Assert.Equal(reverse ? 1 : -1, TaskSorting.CompareManualOrder(1, 2, reverse));
    }

    [Fact]
    public void InvalidMode_Throws() => Assert.Throws<ArgumentOutOfRangeException>(() => TaskSorting.Compare((SortMode)99, false));

    [Fact]
    public void DueDate_DateOnlySortsBeforeTimedValueOnSameStoredDay()
    {
        var dateOnly = new TodoTask { Due = DateTimeOffset.UnixEpoch.AddHours(20), DueIsDateOnly = true };
        var timed = new TodoTask { Due = DateTimeOffset.UnixEpoch };
        Assert.True(TaskSorting.Compare(SortMode.DueDate, false)(dateOnly, timed) < 0);
    }
}
