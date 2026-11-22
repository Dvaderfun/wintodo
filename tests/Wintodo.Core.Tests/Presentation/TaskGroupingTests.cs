using Wintodo.Core.Models;
using Wintodo.Core.Presentation;
using Wintodo.Core.Queries;
using Wintodo.Core.Services;

namespace Wintodo.Core.Tests.Presentation;

public sealed class TaskGroupingTests
{
    [Theory]
    [InlineData(-1, DayOfWeek.Monday, "Overdue")]
    [InlineData(0, DayOfWeek.Monday, "Today")]
    [InlineData(1, DayOfWeek.Monday, "Tomorrow")]
    [InlineData(2, DayOfWeek.Monday, "ThisWeek")]
    [InlineData(5, DayOfWeek.Monday, "ThisWeek")]
    [InlineData(6, DayOfWeek.Monday, "Later")]
    [InlineData(-1, DayOfWeek.Sunday, "Overdue")]
    [InlineData(0, DayOfWeek.Sunday, "Today")]
    [InlineData(1, DayOfWeek.Sunday, "Tomorrow")]
    [InlineData(4, DayOfWeek.Sunday, "ThisWeek")]
    [InlineData(5, DayOfWeek.Sunday, "Later")]
    [InlineData(2, DayOfWeek.Wednesday, "Later")]
    [InlineData(2, DayOfWeek.Tuesday, "ThisWeek")]
    public void PlannedBucket_UsesLocalDayAndConfiguredWeekBoundary(int offset, DayOfWeek weekStart, string expected)
    {
        var clock = new Clock(new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero)); // Tuesday
        Assert.Equal(expected, TaskGrouping.GetPlannedBucket(new() { Due = clock.UtcNow.AddDays(offset) }, clock, weekStart));
    }

    [Fact]
    public void PlannedBucket_NoDateIsExplicit() =>
        Assert.Equal("NoDate", TaskGrouping.GetPlannedBucket(new(), new Clock(), DayOfWeek.Monday));

    [Theory]
    [InlineData(0, "Tomorrow")]
    [InlineData(1, "Today")]
    [InlineData(2, "Overdue")]
    public void PlannedBucket_ReevaluatesAfterLocalMidnight(int days, string expected)
    {
        TimeZoneInfo zone = TimeZoneInfo.CreateCustomTimeZone("Test+02", TimeSpan.FromHours(2), "Test+02", "Test+02");
        var clock = new Clock(new DateTimeOffset(2026, 10, 6, 21, 59, 0, TimeSpan.Zero).AddDays(days), zone);
        DateTimeOffset due = new(2026, 10, 6, 22, 0, 0, TimeSpan.Zero);
        Assert.Equal(expected, TaskGrouping.GetPlannedBucket(new() { Due = due }, clock, DayOfWeek.Monday));
    }

    [Theory]
    [InlineData(true, "Today", 0)]
    [InlineData(false, "Overdue", -1)]
    public void PlannedGroupAndRowUseTheSameCalendarDateWestOfUtc(bool dateOnly, string bucket, int dayOffset)
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("UTC-07", TimeSpan.FromHours(-7), "UTC-07", "UTC-07");
        IClock clock = new Clock(new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero), zone);
        var task = new TodoTask { Uid = "midnight", Due = new(2026, 10, 6, 0, 0, 0, TimeSpan.Zero), DueIsDateOnly = dateOnly };

        var group = Assert.Single(Build(ListQueryKind.Planned, [task], [], clock: clock));
        var row = TaskMetadataLine.Build(task, ListQueryKind.Planned, clock, null);

        Assert.Equal(bucket, group.Key);
        Assert.Equal(clock.Today.AddDays(dayOffset), Assert.Single(row.Parts, part => part.Kind == TaskMetadataKind.DueDate).Date);
        Assert.Equal(dayOffset == 0, row.Parts.Any(part => part.Kind == TaskMetadataKind.MyDay));
    }

    [Fact]
    public void Planned_GroupsInBucketOrderAndPreservesInputOrderIncludingCompleted()
    {
        var clock = new Clock();
        TodoTask[] active = [Task("later", due: clock.UtcNow.AddDays(20)), Task("today2", due: clock.UtcNow),
            Task("overdue", due: clock.UtcNow.AddDays(-1)), Task("tomorrow", due: clock.UtcNow.AddDays(1)),
            Task("week", due: clock.UtcNow.AddDays(2)), Task("no-date")];
        TodoTask[] completed = [Task("today1", due: clock.UtcNow)];
        IReadOnlyList<TaskGroup> groups = Build(ListQueryKind.Planned, active, completed, clock: clock);
        Assert.Equal(["Overdue", "Today", "Tomorrow", "ThisWeek", "Later", "NoDate"], groups.Select(group => group.Key));
        Assert.Equal(["today2", "today1"], groups[1].TaskUids);
        Assert.All(groups, group => Assert.Equal(TaskGroupKind.Planned, group.Kind));
    }

    [Theory]
    [InlineData(false, false, 1, 2)]
    [InlineData(false, true, 1, 2)]
    [InlineData(true, false, 2, 2)]
    [InlineData(true, true, 2, 4)]
    public void Ordinary_RespectsCompletedVisibilityExpansionAndHeaderCount(
        bool showCompleted, bool expanded, int groupCount, int visibleCount)
    {
        IReadOnlyList<TaskGroup> groups = Build(ListQueryKind.UserList,
            [Task("a"), Task("b")], [Task("c"), Task("d")], showCompleted, expanded);
        Assert.Equal(groupCount, groups.Count);
        Assert.Equal(visibleCount, groups.Sum(group => group.TaskUids.Count));
        if (showCompleted)
        {
            Assert.Equal(2, groups[1].Count);
            Assert.Equal(expanded, groups[1].IsExpanded);
            Assert.Equal(TaskGroupKind.Completed, groups[1].Kind);
        }
    }

    [Fact]
    public void CompletedView_IgnoresCollapsedSectionAndShowCompletedFlag()
    {
        TaskGroup group = Assert.Single(Build(ListQueryKind.Completed, [Task("active")], [Task("done")], false, false));
        Assert.Equal(TaskGroupKind.Hidden, group.Kind);
        Assert.Equal(["done"], group.TaskUids);
    }

    [Fact]
    public void Search_UsesListOrderAndActiveThenCompletedInputOrder()
    {
        IReadOnlyList<TaskGroup> groups = Build(ListQueryKind.Planned,
            [Task("b", 2), Task("a", 1), Task("orphan", 9)], [Task("done", 2)], false, false,
            search: "query", lists: [new(1, "alpha", 20), new(2, "beta", 10), new(3, "empty", 0)]);
        Assert.Equal(["beta", "alpha"], groups.Select(group => group.Key));
        Assert.Equal(["b", "done"], groups[0].TaskUids);
        Assert.Equal(2, groups[0].ListId);
        Assert.All(groups, group => Assert.Equal(TaskGroupKind.Search, group.Kind));
    }

    [Fact]
    public void Search_EqualListOrderKeepsOriginalListOrder()
    {
        IReadOnlyList<TaskGroup> groups = Build(ListQueryKind.All, [Task("b", 2), Task("a", 1)], [],
            search: "query", lists: [new(2, "beta", 1), new(1, "alpha", 1)]);
        Assert.Equal(["beta", "alpha"], groups.Select(group => group.Key));
    }

    [Theory]
    [InlineData(ListQueryKind.MyDay)]
    [InlineData(ListQueryKind.Planned)]
    [InlineData(ListQueryKind.Completed)]
    [InlineData(ListQueryKind.UserList)]
    public void EmptyInputs_DoNotCreateEmptyGroups(ListQueryKind kind) => Assert.Empty(Build(kind, [], []));

    private static IReadOnlyList<TaskGroup> Build(ListQueryKind kind, TodoTask[] active, TodoTask[] completed,
        bool showCompleted = true, bool expanded = true, string search = "", TaskGroupList[]? lists = null, IClock? clock = null) =>
        TaskGrouping.Build(new ListQuery { Kind = kind }, active, completed, showCompleted, search,
            clock ?? new Clock(), DayOfWeek.Monday, lists ?? [], expanded);

    private static TodoTask Task(string uid, long listId = 1, DateTimeOffset? due = null) =>
        new() { Uid = uid, ListId = listId, Due = due };

    private sealed class Clock(DateTimeOffset? now = null, TimeZoneInfo? zone = null) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now ?? new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
        public TimeZoneInfo LocalTimeZone { get; } = zone ?? TimeZoneInfo.Utc;
    }
}
