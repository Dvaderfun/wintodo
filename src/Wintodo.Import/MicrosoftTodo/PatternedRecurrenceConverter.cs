using System.Globalization;
using Wintodo.Core.Recurrence;

namespace Wintodo.Import.MicrosoftTodo;

/// <summary>Maps a Graph <c>patternedRecurrence</c> to an RFC 5545 RRULE.</summary>
public static class PatternedRecurrenceConverter
{
    /// <summary>
    /// Returns false for patterns RRULE can't express exactly (for example a
    /// relative monthly rule over several weekdays). The caller keeps the raw
    /// pattern instead of guessing.
    /// </summary>
    public static bool TryConvert(GraphPatternedRecurrence? recurrence, out string? rrule)
    {
        rrule = null;
        GraphRecurrencePattern? pattern = recurrence?.Pattern;
        if (pattern is null || string.IsNullOrWhiteSpace(pattern.Type))
        {
            return false;
        }

        var rule = new RecurrenceRule { Interval = Math.Max(1, pattern.Interval) };
        switch (pattern.Type.Trim().ToLowerInvariant())
        {
            case "daily":
                rule.Frequency = RecurrenceFrequency.Daily;
                break;
            case "weekly":
                rule.Frequency = RecurrenceFrequency.Weekly;
                if (!TryAddDays(rule, pattern.DaysOfWeek, ordinal: null))
                {
                    return false;
                }

                break;
            case "absolutemonthly":
                rule.Frequency = RecurrenceFrequency.Monthly;
                if (pattern.DayOfMonth is < 1 or > 31)
                {
                    return false;
                }

                rule.ByMonthDay.Add(pattern.DayOfMonth);
                break;
            case "relativemonthly":
                rule.Frequency = RecurrenceFrequency.Monthly;
                if (!TryAddRelativeDay(rule, pattern))
                {
                    return false;
                }

                break;
            case "absoluteyearly":
                rule.Frequency = RecurrenceFrequency.Yearly;
                if (pattern.Month is < 1 or > 12 || pattern.DayOfMonth is < 1 or > 31)
                {
                    return false;
                }

                rule.ByMonth.Add(pattern.Month);
                rule.ByMonthDay.Add(pattern.DayOfMonth);
                break;
            case "relativeyearly":
                rule.Frequency = RecurrenceFrequency.Yearly;
                if (pattern.Month is < 1 or > 12 || !TryAddRelativeDay(rule, pattern))
                {
                    return false;
                }

                rule.ByMonth.Add(pattern.Month);
                break;
            default:
                return false;
        }

        if (TryParseDay(pattern.FirstDayOfWeek, out DayOfWeek weekStart))
        {
            rule.WeekStart = weekStart;
        }

        if (!TryApplyRange(rule, recurrence!.Range))
        {
            return false;
        }

        rrule = rule.ToString();
        return true;
    }

    private static bool TryApplyRange(RecurrenceRule rule, GraphRecurrenceRange? range)
    {
        switch (range?.Type?.Trim().ToLowerInvariant())
        {
            case null:
            case "":
            case "noend":
                return true;
            case "numbered":
                if (range.NumberOfOccurrences < 1)
                {
                    return false;
                }

                rule.Count = range.NumberOfOccurrences;
                return true;
            case "enddate":
                if (!DateOnly.TryParseExact(
                        range.EndDate,
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out DateOnly end))
                {
                    return false;
                }

                rule.Until = new DateTimeOffset(end.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
                rule.UntilIsDateOnly = true;
                return true;
            default:
                return false;
        }
    }

    private static bool TryAddRelativeDay(RecurrenceRule rule, GraphRecurrencePattern pattern)
    {
        int? ordinal = pattern.Index?.Trim().ToLowerInvariant() switch
        {
            null or "" or "first" => 1,
            "second" => 2,
            "third" => 3,
            "fourth" => 4,
            "last" => -1,
            _ => null,
        };

        // Several weekdays with an index means "the Nth of any of these days"
        // (BYSETPOS), which this app's recurrence engine doesn't support.
        return ordinal is not null
            && pattern.DaysOfWeek is { Count: 1 }
            && TryAddDays(rule, pattern.DaysOfWeek, ordinal);
    }

    private static bool TryAddDays(RecurrenceRule rule, List<string>? days, int? ordinal)
    {
        if (days is null || days.Count == 0)
        {
            // A weekly rule without days repeats on the start date's weekday.
            return ordinal is null;
        }

        foreach (string day in days.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!TryParseDay(day, out DayOfWeek parsed))
            {
                return false;
            }

            rule.ByDay.Add(new RecurrenceDay(parsed, ordinal));
        }

        return true;
    }

    private static bool TryParseDay(string? value, out DayOfWeek day)
    {
        return Enum.TryParse(value?.Trim(), ignoreCase: true, out day)
            && Enum.IsDefined(day);
    }
}
