using Wintodo.Core.Recurrence;
using Wintodo.Import.MicrosoftTodo;

namespace Wintodo.Import.Tests;

public sealed class ConverterTests
{
    public static TheoryData<string, string> RecurrenceCases => new()
    {
        { """{"pattern":{"type":"daily","interval":2},"range":{"type":"noEnd"}}""", "FREQ=DAILY;INTERVAL=2" },
        { """{"pattern":{"type":"weekly","interval":1,"daysOfWeek":["monday","wednesday"],"firstDayOfWeek":"sunday"},"range":{"type":"noEnd"}}""", "FREQ=WEEKLY;BYDAY=MO,WE;WKST=SU" },
        { """{"pattern":{"type":"weekly","interval":1,"daysOfWeek":["monday","tuesday","wednesday","thursday","friday"],"firstDayOfWeek":"monday"},"range":{"type":"noEnd"}}""", "FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR" },
        { """{"pattern":{"type":"absoluteMonthly","interval":1,"dayOfMonth":31},"range":{"type":"noEnd"}}""", "FREQ=MONTHLY;BYMONTHDAY=31" },
        { """{"pattern":{"type":"relativeMonthly","interval":1,"daysOfWeek":["friday"],"index":"last"},"range":{"type":"noEnd"}}""", "FREQ=MONTHLY;BYDAY=-1FR" },
        { """{"pattern":{"type":"relativeMonthly","interval":3,"daysOfWeek":["tuesday"],"index":"second"},"range":{"type":"noEnd"}}""", "FREQ=MONTHLY;INTERVAL=3;BYDAY=2TU" },
        { """{"pattern":{"type":"absoluteYearly","interval":1,"month":2,"dayOfMonth":29},"range":{"type":"noEnd"}}""", "FREQ=YEARLY;BYMONTHDAY=29;BYMONTH=2" },
        { """{"pattern":{"type":"relativeYearly","interval":1,"month":11,"daysOfWeek":["thursday"],"index":"fourth"},"range":{"type":"noEnd"}}""", "FREQ=YEARLY;BYDAY=4TH;BYMONTH=11" },
        { """{"pattern":{"type":"daily","interval":1},"range":{"type":"numbered","numberOfOccurrences":10}}""", "FREQ=DAILY;COUNT=10" },
        { """{"pattern":{"type":"daily","interval":1},"range":{"type":"endDate","endDate":"2026-12-31"}}""", "FREQ=DAILY;UNTIL=20261231" },
    };

    [Theory]
    [MemberData(nameof(RecurrenceCases))]
    public void Recurrence_ConvertsToParseableRrule(string json, string expected)
    {
        GraphPatternedRecurrence recurrence = Parse(json);

        Assert.True(PatternedRecurrenceConverter.TryConvert(recurrence, out string? rrule));
        Assert.Equal(expected, rrule);
        Assert.Equal(expected, RecurrenceRule.Parse(rrule!).ToString());
    }

    [Theory]
    [InlineData("""{"pattern":{"type":"relativeMonthly","interval":1,"daysOfWeek":["monday","tuesday"],"index":"first"},"range":{"type":"noEnd"}}""")]
    [InlineData("""{"pattern":{"type":"hourly","interval":1},"range":{"type":"noEnd"}}""")]
    [InlineData("""{"pattern":{"type":"absoluteMonthly","interval":1,"dayOfMonth":0},"range":{"type":"noEnd"}}""")]
    [InlineData("""{"pattern":{"type":"daily","interval":1},"range":{"type":"endDate","endDate":"not-a-date"}}""")]
    [InlineData("""{"range":{"type":"noEnd"}}""")]
    public void Recurrence_RejectsPatternsItCannotExpressExactly(string json)
    {
        Assert.False(PatternedRecurrenceConverter.TryConvert(Parse(json), out string? rrule));
        Assert.Null(rrule);
    }

    [Fact]
    public void DueDate_AtMidnightKeepsItsOwnCalendarDate()
    {
        var due = new GraphDateTimeTimeZone { DateTime = "2026-10-21T00:00:00.0000000", TimeZone = "UTC" };

        Assert.Equal(new DateOnly(2026, 10, 21), GraphDateConverter.ToDate(due, Zone("Pacific Standard Time")));
        Assert.Equal(new DateOnly(2026, 10, 21), GraphDateConverter.ToDate(due, Zone("Tokyo Standard Time")));
    }

    [Fact]
    public void DueDate_StoredAsLocalMidnightInUtcIsReadInTheLocalZone()
    {
        // Midnight of 20 Oct in Berlin (CEST, UTC+2) stored as a UTC instant.
        var due = new GraphDateTimeTimeZone { DateTime = "2026-10-19T22:00:00.0000000", TimeZone = "UTC" };

        Assert.Equal(new DateOnly(2026, 10, 20), GraphDateConverter.ToDate(due, Zone("W. Europe Standard Time")));
    }

    [Theory]
    [InlineData("W. Europe Standard Time")]
    [InlineData("Europe/Berlin")]
    public void Instant_ResolvesWindowsAndIanaZones(string zone)
    {
        var value = new GraphDateTimeTimeZone { DateTime = "2026-09-30T09:00:00.0000000", TimeZone = zone };

        Assert.Equal(
            new DateTimeOffset(2026, 9, 30, 7, 0, 0, TimeSpan.Zero),
            GraphDateConverter.ToInstant(value));
    }

    [Fact]
    public void Instant_FallsBackToUtcAndRejectsGarbage()
    {
        Assert.Equal(
            new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero),
            GraphDateConverter.ToInstant(new GraphDateTimeTimeZone { DateTime = "2026-01-01T09:00:00", TimeZone = "Mars/Olympus" }));
        Assert.Null(GraphDateConverter.ToInstant(new GraphDateTimeTimeZone { DateTime = "soon", TimeZone = "UTC" }));
        Assert.Null(GraphDateConverter.ToInstant(null));
    }

    [Fact]
    public void Instant_InsideDaylightSavingGapIsShiftedForward()
    {
        // 2026-03-29 02:30 does not exist in Berlin.
        var value = new GraphDateTimeTimeZone { DateTime = "2026-03-29T02:30:00", TimeZone = "W. Europe Standard Time" };

        Assert.Equal(new DateTimeOffset(2026, 3, 29, 1, 30, 0, TimeSpan.Zero), GraphDateConverter.ToInstant(value));
    }

    [Fact]
    public void HtmlNote_BecomesReadablePlainText()
    {
        const string html = "<html><head><style>p{color:red}</style></head><body><p>Numbers from <b>finance</b> &amp; sales</p>"
            + "<ul><li>Revenue</li><li>Costs</li></ul><div>Line<br/>break&nbsp;here</div></body></html>";

        Assert.Equal(
            "Numbers from finance & sales\n\n• Revenue\n• Costs\nLine\nbreak here",
            HtmlNoteConverter.ToPlainText(html));
        Assert.Equal(string.Empty, HtmlNoteConverter.ToPlainText("  "));
    }

    [Theory]
    [InlineData("🛒 Groceries", "🛒", "Groceries")]
    [InlineData("👨‍👩‍👧 Family", "👨‍👩‍👧", "Family")]
    [InlineData("Groceries", null, "Groceries")]
    [InlineData("🛒", null, "🛒")]
    [InlineData("A list", null, "A list")]
    public void ListEmoji_IsSplitOnlyWhenANameRemains(string displayName, string? emoji, string name)
    {
        Assert.Equal((emoji, name), GraphTodoMapper.SplitListEmoji(displayName));
    }

    private static GraphPatternedRecurrence Parse(string json)
    {
        return System.Text.Json.JsonSerializer.Deserialize(json, GraphJsonContext.Default.GraphPatternedRecurrence)!;
    }

    private static TimeZoneInfo Zone(string id) => TimeZoneInfo.FindSystemTimeZoneById(id);
}
