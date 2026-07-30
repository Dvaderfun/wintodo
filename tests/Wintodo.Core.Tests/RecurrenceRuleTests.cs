using Wintodo.Core.Recurrence;

namespace Wintodo.Core.Tests;

public sealed class RecurrenceRuleTests
{
    [Fact]
    public void ParseAndSerialize_PreservesSupportedComponents()
    {
        const string text = "FREQ=MONTHLY;INTERVAL=2;BYDAY=1MO,-1FR;BYMONTHDAY=1,-1;BYMONTH=1,6;COUNT=8;WKST=SU";

        RecurrenceRule rule = RecurrenceRule.Parse(text);

        Assert.Equal(RecurrenceFrequency.Monthly, rule.Frequency);
        Assert.Equal(2, rule.Interval);
        Assert.Equal(2, rule.ByDay.Count);
        Assert.Equal([1, -1], rule.ByMonthDay);
        Assert.Equal([1, 6], rule.ByMonth);
        Assert.Equal(8, rule.Count);
        Assert.Equal(DayOfWeek.Sunday, rule.WeekStart);
        Assert.Equal(text, rule.ToString());
    }

    [Fact]
    public void ComputeNext_Daily()
    {
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=DAILY");
        DateTimeOffset start = AtUtc(2026, 1, 10, 9);

        Assert.Equal(AtUtc(2026, 1, 11, 9), rule.ComputeNext(start));
    }

    [Fact]
    public void ComputeNext_DailyWeekdays_SkipsWeekend()
    {
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=DAILY;BYDAY=MO,TU,WE,TH,FR");
        DateTimeOffset friday = AtUtc(2026, 1, 9, 9);

        Assert.Equal(AtUtc(2026, 1, 12, 9), rule.ComputeNext(friday));
    }

    [Fact]
    public void ComputeNext_WeeklyByDay_UsesWeekStart()
    {
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=WEEKLY;BYDAY=MO,WE;WKST=MO");
        DateTimeOffset monday = AtUtc(2026, 1, 5, 9);

        Assert.Equal(AtUtc(2026, 1, 7, 9), rule.ComputeNext(monday));
    }

    [Fact]
    public void ComputeNext_MonthlyByDate_SkipsMonthWithoutDate()
    {
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=MONTHLY;BYMONTHDAY=31");
        DateTimeOffset january = AtUtc(2026, 1, 31, 9);

        Assert.Equal(AtUtc(2026, 3, 31, 9), rule.ComputeNext(january));
    }

    [Fact]
    public void ComputeNext_MonthlyNthWeekday()
    {
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=MONTHLY;BYDAY=2TU");
        DateTimeOffset start = AtUtc(2026, 1, 13, 9);

        Assert.Equal(AtUtc(2026, 2, 10, 9), rule.ComputeNext(start));
    }

    [Fact]
    public void ComputeNext_MonthlyLastWeekday()
    {
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=MONTHLY;BYDAY=-1FR");
        DateTimeOffset start = AtUtc(2026, 1, 30, 9);

        Assert.Equal(AtUtc(2026, 2, 27, 9), rule.ComputeNext(start));
    }

    [Fact]
    public void ComputeNext_Yearly()
    {
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=YEARLY;BYMONTH=7;BYMONTHDAY=4");
        DateTimeOffset start = AtUtc(2026, 7, 4, 9);

        Assert.Equal(AtUtc(2027, 7, 4, 9), rule.ComputeNext(start));
    }

    [Fact]
    public void ComputeNext_LeapDay_ReachesNextLeapYear()
    {
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=YEARLY;BYMONTH=2;BYMONTHDAY=29");
        DateTimeOffset start = AtUtc(2024, 2, 29, 9);

        Assert.Equal(AtUtc(2028, 2, 29, 9), rule.ComputeNext(start));
    }

    [Fact]
    public void ComputeNext_EveryNIntervals()
    {
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=MONTHLY;INTERVAL=3");
        DateTimeOffset start = AtUtc(2026, 1, 15, 9);

        Assert.Equal(AtUtc(2026, 4, 15, 9), rule.ComputeNext(start));
    }

    [Fact]
    public void ComputeNext_CountExhausted_ReturnsNull()
    {
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=DAILY;COUNT=3");
        DateTimeOffset start = AtUtc(2026, 1, 1, 9);

        Assert.NotNull(rule.ComputeNext(start, completedOccurrences: 2));
        Assert.Null(rule.ComputeNext(start, completedOccurrences: 3));
    }

    [Fact]
    public void ComputeNext_UntilDate_IsInclusive()
    {
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=DAILY;UNTIL=20260102");

        Assert.Equal(
            AtUtc(2026, 1, 2, 9),
            rule.ComputeNext(AtUtc(2026, 1, 1, 9)));
        Assert.Null(rule.ComputeNext(AtUtc(2026, 1, 2, 9)));
    }

    [Fact]
    public void ComputeNext_AcrossDst_PreservesWallClockTime()
    {
        TimeZoneInfo eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var beforeDst = new DateTimeOffset(2026, 3, 7, 9, 0, 0, TimeSpan.FromHours(-5));
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=DAILY");

        DateTimeOffset? next = rule.ComputeNext(beforeDst, eastern);

        Assert.Equal(new DateTimeOffset(2026, 3, 8, 9, 0, 0, TimeSpan.FromHours(-4)), next);
    }

    [Fact]
    public void ComputeNext_InvalidDstTime_AdvancesToFirstValidMinute()
    {
        TimeZoneInfo eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var beforeDst = new DateTimeOffset(2026, 3, 7, 2, 30, 0, TimeSpan.FromHours(-5));
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=DAILY");

        DateTimeOffset? next = rule.ComputeNext(beforeDst, eastern);

        Assert.Equal(new DateTimeOffset(2026, 3, 8, 3, 0, 0, TimeSpan.FromHours(-4)), next);
    }

    [Theory]
    [InlineData("FREQ=HOURLY")]
    [InlineData("FREQ=DAILY;INTERVAL=0")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=0")]
    [InlineData("FREQ=YEARLY;BYMONTH=13")]
    [InlineData("FREQ=DAILY;COUNT=2;UNTIL=20260102")]
    [InlineData("INTERVAL=2")]
    [InlineData("FREQ=DAILY;UNKNOWN=1")]
    public void Parse_InvalidRule_Throws(string text)
    {
        Assert.Throws<FormatException>(() => RecurrenceRule.Parse(text));
    }

    [Fact]
    public void ComputeNext_AmbiguousTime_UsesEarlierOccurrence()
    {
        TimeZoneInfo eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var beforeFallback = new DateTimeOffset(2026, 10, 31, 1, 30, 0, TimeSpan.FromHours(-4));
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=DAILY");

        DateTimeOffset? next = rule.ComputeNext(beforeFallback, eastern);

        Assert.Equal(new DateTimeOffset(2026, 11, 1, 1, 30, 0, TimeSpan.FromHours(-4)), next);
    }

    private static DateTimeOffset AtUtc(int year, int month, int day, int hour)
    {
        return new DateTimeOffset(year, month, day, hour, 0, 0, TimeSpan.Zero);
    }
}
