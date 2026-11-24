using Wintodo.Core.Models;
using Wintodo.Core.Presentation;
using Wintodo.Core.Services;

namespace Wintodo.Core.Tests;

public sealed class MyDaySuggestionsTests
{
    [Fact]
    public void PutsOverdueFirstAndOrdersUpcomingByLocalDeadline()
    {
        IClock clock = new TestClock(new DateTimeOffset(2026, 11, 22, 20, 0, 0, TimeSpan.Zero));
        TodoTask overdue = Dated("overdue", 22);
        TodoTask tomorrow = Dated("tomorrow", 24);
        TodoTask later = Dated("later", 27);
        TodoTask dueToday = Dated("auto", 23);
        TodoTask removed = Dated("removed", 23);
        removed.MyDayExcludedDate = clock.Today;
        TodoTask timedToday = new() { Uid = "timed", Due = new DateTimeOffset(2026, 11, 22, 21, 0, 0, TimeSpan.Zero) };
        TodoTask pinned = Dated("pinned", 24);
        pinned.MyDayDate = clock.Today;
        var carryOver = new TodoTask { Uid = "carry", MyDayDate = clock.Today.AddDays(-1) };
        TodoTask completed = Dated("completed", 22);
        completed.Status = Core.Models.TaskStatus.Completed;
        TodoTask cancelled = Dated("cancelled", 22);
        cancelled.Status = Core.Models.TaskStatus.Cancelled;
        TodoTask deleted = Dated("deleted", 22);
        deleted.IsDeleted = true;
        TodoTask step = Dated("step", 22);
        step.ParentUid = overdue.Uid;

        var result = MyDaySuggestions.Build(
            [later, tomorrow, overdue, dueToday, timedToday, removed, pinned, carryOver, completed,
                cancelled, deleted, step, new TodoTask { Uid = "undated" }],
            clock);

        Assert.Equal(
            new[] { ("overdue", SuggestionSection.Overdue), ("tomorrow", SuggestionSection.Upcoming),
                ("later", SuggestionSection.Upcoming), ("removed", SuggestionSection.Today),
                ("carry", SuggestionSection.Yesterday), ("undated", SuggestionSection.Undated) },
            result.Select(item => (item.Task.Uid, item.Section)));
    }

    [Fact]
    public void RolloverExpiresOldPinsAndReclassifiesDatedSuggestions()
    {
        IClock before = new TestClock(new DateTimeOffset(2026, 11, 23, 18, 29, 0, TimeSpan.Zero));
        IClock after = new TestClock(before.UtcNow.AddMinutes(1));
        TodoTask due = Dated("due", 23);
        var pinned = new TodoTask { Uid = "pinned", MyDayDate = before.Today };
        Assert.Empty(MyDaySuggestions.Build([due, pinned], before));
        Assert.Equal(new[] { SuggestionSection.Overdue, SuggestionSection.Yesterday },
            MyDaySuggestions.Build([due, pinned], after).Select(item => item.Section));
    }

    private static TodoTask Dated(string uid, int day) => new()
    {
        Uid = uid,
        Due = new DateTimeOffset(2026, 11, day, 0, 0, 0, TimeSpan.Zero),
        DueIsDateOnly = true,
    };

    private sealed class TestClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
        public TimeZoneInfo LocalTimeZone { get; } = TimeZoneInfo.CreateCustomTimeZone(
            "Test +05:30", TimeSpan.FromMinutes(330), "Test +05:30", "Test +05:30");
    }
}
