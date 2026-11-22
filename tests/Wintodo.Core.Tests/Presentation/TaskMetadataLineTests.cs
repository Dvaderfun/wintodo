using Wintodo.Core.Models;
using Wintodo.Core.Presentation;
using Wintodo.Core.Queries;
using Wintodo.Core.Services;

namespace Wintodo.Core.Tests.Presentation;

public sealed class TaskMetadataLineTests
{
    [Theory]
    [InlineData(ListQueryKind.UserList, "DueDate,Repeat,MyDay")]
    [InlineData(ListQueryKind.MyDay, "ListName,DueDate,Repeat")]
    [InlineData(ListQueryKind.Important, "ListName,DueDate,Repeat,MyDay")]
    [InlineData(ListQueryKind.Planned, "ListName,DueDate,Repeat,MyDay")]
    [InlineData(ListQueryKind.All, "ListName,DueDate,Repeat,MyDay")]
    [InlineData(ListQueryKind.Completed, "ListName,DueDate,Repeat,MyDay")]
    [InlineData(null, "ListName,DueDate,Repeat,MyDay")]
    public void Parts_FollowShippedOrderAndContext(ListQueryKind? context, string expected)
    {
        IClock clock = new Clock();
        var task = new TodoTask { Due = clock.UtcNow, RRule = "FREQ=DAILY", MyDayDate = clock.Today.AddDays(-5) };
        TaskMetadataResult result = TaskMetadataLine.Build(task, context, clock, "Work");
        Assert.Equal(expected, string.Join(',', result.Parts.Select(part => part.Kind)));
        Assert.Equal(clock.Today, Assert.Single(result.Parts, part => part.Kind == TaskMetadataKind.DueDate).Date);
        if (context != ListQueryKind.UserList) { Assert.Equal("Work", result.Parts[0].Text); }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void EmptyTaskAndBlankRecurrence_OmitParts(string? recurrence) =>
        Assert.Empty(TaskMetadataLine.Build(new() { RRule = recurrence }, ListQueryKind.All, new Clock(), null).Parts);

    [Theory]
    [InlineData(-1, false, true, false)]
    [InlineData(0, false, false, true)]
    [InlineData(1, false, false, false)]
    [InlineData(-1, true, false, false)]
    [InlineData(0, true, false, false)]
    public void DueFlags_UseLocalDateAndExcludeCompleted(int days, bool completed, bool overdue, bool today)
    {
        var clock = new Clock();
        var task = new TodoTask { Due = clock.UtcNow.AddDays(days),
            Status = completed ? Models.TaskStatus.Completed : Models.TaskStatus.NeedsAction };
        TaskMetadataResult result = TaskMetadataLine.Build(task, ListQueryKind.All, clock, null);
        Assert.Equal(overdue, result.IsOverdue);
        Assert.Equal(today, result.IsDueToday);
    }

    [Fact]
    public void NoDue_DoesNotSetDueFlags()
    {
        TaskMetadataResult result = TaskMetadataLine.Build(new(), ListQueryKind.All, new Clock(), null);
        Assert.False(result.IsOverdue);
        Assert.False(result.IsDueToday);
    }

    [Fact]
    public void LocalTimeZoneDeterminesDueDay()
    {
        TimeZoneInfo zone = TimeZoneInfo.CreateCustomTimeZone("Test+02", TimeSpan.FromHours(2), "Test+02", "Test+02");
        IClock clock = new Clock(zone);
        TaskMetadataResult result = TaskMetadataLine.Build(new() { Due = new(2026, 10, 5, 23, 0, 0, TimeSpan.Zero) },
            ListQueryKind.All, clock, null);
        Assert.True(result.IsDueToday);
        Assert.Equal(clock.Today, Assert.Single(result.Parts, part => part.Kind == TaskMetadataKind.DueDate).Date);
    }

    [Theory]
    [InlineData(0, null, false, true)]
    [InlineData(0, null, true, false)]
    [InlineData(-1, -1, false, false)]
    [InlineData(0, -1, false, true)]
    [InlineData(-1, 0, false, true)]
    public void MyDayBadgeUsesCurrentMembership(int dueOffset, int? pinOffset, bool dismissed, bool expected)
    {
        TimeZoneInfo zone = TimeZoneInfo.CreateCustomTimeZone("Test-07", TimeSpan.FromHours(-7), "Test-07", "Test-07");
        IClock clock = new Clock(zone);
        var task = new TodoTask
        {
            Due = new DateTimeOffset(clock.Today.AddDays(dueOffset).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            DueIsDateOnly = true,
            MyDayDate = pinOffset is int days ? clock.Today.AddDays(days) : null,
            MyDayExcludedDate = dismissed ? clock.Today : null,
        };
        var metadata = TaskMetadataLine.Build(task, ListQueryKind.UserList, clock, null);
        Assert.Equal(expected, metadata.Parts.Any(part => part.Kind == TaskMetadataKind.MyDay));
        Assert.Equal(dueOffset == 0, metadata.IsDueToday);
    }

    private sealed class Clock(TimeZoneInfo? zone = null) : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
        public TimeZoneInfo LocalTimeZone => zone ?? TimeZoneInfo.Utc;
    }
}
