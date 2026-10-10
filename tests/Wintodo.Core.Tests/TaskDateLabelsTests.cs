using System.Globalization;
using Wintodo.Core.Presentation;
using Wintodo.Core.Services;

namespace Wintodo.Core.Tests;

public sealed class TaskDateLabelsTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private static readonly IClock Clock = new FixedClock();

    [Theory]
    [InlineData("uk-UA")]
    [InlineData("de-DE")]
    public void Created_UsesDisplayCultureWhenRegionalCultureDiffers(string regionalTag)
    {
        WithRegionalCulture(regionalTag, () =>
        {
            Assert.Equal("Created Sat, 10 Oct", TaskDateLabels.FormatCreated(
                Clock.UtcNow,
                "Created {0:ddd, d MMM}",
                DisplayCultureResolver.Resolve("en-US", CultureInfo.CurrentUICulture)));
        });
    }

    [Theory]
    [InlineData(-2, "8 Oct", "Thu, 8 Oct")]
    [InlineData(-1, "Yesterday", "Fri, 9 Oct")]
    [InlineData(0, "Today", "Today")]
    [InlineData(1, "Tomorrow", "Tomorrow")]
    [InlineData(2, "Monday", "Mon, 12 Oct")]
    [InlineData(7, "Saturday", "Next week")]
    [InlineData(8, "18 Oct", "Sun, 18 Oct")]
    [InlineData(31, "10 Nov", "Tue, 10 Nov")]
    public void DueLabels_KeepRelativeRulesAndEnglishDates(
        int dayOffset,
        string listLabel,
        string detailLabel)
    {
        foreach (string regionalTag in new[] { "uk-UA", "de-DE" })
        {
            WithRegionalCulture(regionalTag, () =>
            {
                DateOnly due = Clock.Today.AddDays(dayOffset);
                Assert.Equal(listLabel, TaskDateLabels.FormatListDue(
                    due, Clock, English, "Yesterday", "Today", "Tomorrow"));
                Assert.Equal(detailLabel, TaskDateLabels.FormatDetailDue(
                    due, Clock, English, "Today", "Tomorrow", "Next week"));
            });
        }
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("uk-UA")]
    [InlineData("de-DE")]
    public void Resolve_UsesTranslationLanguage(string tag)
    {
        Assert.Equal(tag, DisplayCultureResolver.Resolve(tag, English).Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("invalid/tag")]
    public void Resolve_FallsBackForMissingOrInvalidTag(string? tag)
    {
        CultureInfo fallback = CultureInfo.GetCultureInfo("de-DE");
        Assert.Same(fallback, DisplayCultureResolver.Resolve(tag, fallback));
    }

    private static void WithRegionalCulture(string tag, Action assertion)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(tag);
            assertion();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

        public TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
