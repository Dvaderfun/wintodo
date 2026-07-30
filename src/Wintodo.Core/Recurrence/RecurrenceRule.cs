using System.Globalization;
using System.Text;

namespace Wintodo.Core.Recurrence;

public enum RecurrenceFrequency
{
    Daily,
    Weekly,
    Monthly,
    Yearly,
}

public readonly struct RecurrenceDay
{
    public RecurrenceDay(DayOfWeek day, int? ordinal = null)
    {
        if (ordinal is 0 or > 53 or < -53)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        }

        Day = day;
        Ordinal = ordinal;
    }

    public DayOfWeek Day { get; }

    public int? Ordinal { get; }
}

public sealed class RecurrenceRule
{
    private const int MaxSearchDays = 146_097;

    public RecurrenceFrequency Frequency { get; set; }

    public int Interval { get; set; } = 1;

    public List<RecurrenceDay> ByDay { get; } = [];

    public List<int> ByMonthDay { get; } = [];

    public List<int> ByMonth { get; } = [];

    public int? Count { get; set; }

    public DateTimeOffset? Until { get; set; }

    public bool UntilIsDateOnly { get; set; }

    public DayOfWeek WeekStart { get; set; } = DayOfWeek.Monday;

    public static RecurrenceRule Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        string text = value.StartsWith("RRULE:", StringComparison.OrdinalIgnoreCase)
            ? value[6..]
            : value;
        var rule = new RecurrenceRule();
        bool frequencyFound = false;

        foreach (string part in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int equalsIndex = part.IndexOf('=');
            if (equalsIndex <= 0 || equalsIndex == part.Length - 1)
            {
                throw new FormatException($"Invalid RRULE component '{part}'.");
            }

            string key = part[..equalsIndex].ToUpperInvariant();
            string componentValue = part[(equalsIndex + 1)..].ToUpperInvariant();

            switch (key)
            {
                case "FREQ":
                    rule.Frequency = ParseFrequency(componentValue);
                    frequencyFound = true;
                    break;
                case "INTERVAL":
                    rule.Interval = ParsePositiveInt(componentValue, key);
                    break;
                case "BYDAY":
                    rule.ByDay.AddRange(componentValue.Split(',').Select(ParseDay));
                    break;
                case "BYMONTHDAY":
                    rule.ByMonthDay.AddRange(componentValue.Split(',').Select(ParseMonthDay));
                    break;
                case "BYMONTH":
                    rule.ByMonth.AddRange(componentValue.Split(',').Select(ParseMonth));
                    break;
                case "COUNT":
                    rule.Count = ParsePositiveInt(componentValue, key);
                    break;
                case "UNTIL":
                    ParseUntil(componentValue, rule);
                    break;
                case "WKST":
                    rule.WeekStart = ParseWeekday(componentValue);
                    break;
                default:
                    throw new FormatException($"Unsupported RRULE component '{key}'.");
            }
        }

        if (!frequencyFound)
        {
            throw new FormatException("RRULE must include FREQ.");
        }

        if (rule.Count is not null && rule.Until is not null)
        {
            throw new FormatException("RRULE cannot contain both COUNT and UNTIL.");
        }

        return rule;
    }

    public DateTimeOffset? ComputeNext(DateTimeOffset from)
    {
        return ComputeNext(from, completedOccurrences: 1, timeZone: null);
    }

    public DateTimeOffset? ComputeNext(DateTimeOffset from, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        return ComputeNext(from, completedOccurrences: 1, timeZone);
    }

    public DateTimeOffset? ComputeNext(
        DateTimeOffset from,
        int completedOccurrences,
        TimeZoneInfo? timeZone = null)
    {
        if (completedOccurrences < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(completedOccurrences));
        }

        Validate();
        if (Count is not null && completedOccurrences >= Count.Value)
        {
            return null;
        }

        DateTime localFrom = timeZone is null
            ? from.DateTime
            : TimeZoneInfo.ConvertTime(from, timeZone).DateTime;

        IEnumerable<DateTime> localCandidates = Frequency switch
        {
            RecurrenceFrequency.Daily => EnumerateDaily(localFrom),
            RecurrenceFrequency.Weekly => EnumerateWeekly(localFrom),
            RecurrenceFrequency.Monthly => EnumerateMonthly(localFrom),
            RecurrenceFrequency.Yearly => EnumerateYearly(localFrom),
            _ => throw new InvalidOperationException("Unsupported recurrence frequency."),
        };

        foreach (DateTime localCandidate in localCandidates)
        {
            DateTimeOffset candidate = CreateOffset(localCandidate, from.Offset, timeZone);
            if (!IsWithinUntil(candidate, localCandidate))
            {
                return null;
            }

            return candidate;
        }

        return null;
    }

    public override string ToString()
    {
        Validate();

        var parts = new List<string>
        {
            $"FREQ={FormatFrequency(Frequency)}",
        };

        if (Interval != 1)
        {
            parts.Add($"INTERVAL={Interval.ToString(CultureInfo.InvariantCulture)}");
        }

        if (ByDay.Count > 0)
        {
            parts.Add($"BYDAY={string.Join(',', ByDay.Select(FormatDay))}");
        }

        if (ByMonthDay.Count > 0)
        {
            parts.Add($"BYMONTHDAY={string.Join(',', ByMonthDay)}");
        }

        if (ByMonth.Count > 0)
        {
            parts.Add($"BYMONTH={string.Join(',', ByMonth)}");
        }

        if (Count is not null)
        {
            parts.Add($"COUNT={Count.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        if (Until is not null)
        {
            string until = UntilIsDateOnly
                ? Until.Value.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
                : Until.Value.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
            parts.Add($"UNTIL={until}");
        }

        if (WeekStart != DayOfWeek.Monday)
        {
            parts.Add($"WKST={FormatWeekday(WeekStart)}");
        }

        return string.Join(';', parts);
    }

    private IEnumerable<DateTime> EnumerateDaily(DateTime from)
    {
        for (int dayOffset = Interval; dayOffset <= MaxSearchDays; dayOffset += Interval)
        {
            DateTime candidate = from.AddDays(dayOffset);
            if (MatchesDateFilters(candidate, useOrdinalByDay: true))
            {
                yield return candidate;
            }
        }
    }

    private IEnumerable<DateTime> EnumerateWeekly(DateTime from)
    {
        DateTime anchorWeek = StartOfWeek(from.Date, WeekStart);
        DayOfWeek[] weekdays = ByDay.Count == 0
            ? [from.DayOfWeek]
            : ByDay.Select(value => value.Day).Distinct().ToArray();

        for (int dayOffset = 1; dayOffset <= MaxSearchDays; dayOffset++)
        {
            DateTime candidate = from.AddDays(dayOffset);
            DateTime candidateWeek = StartOfWeek(candidate.Date, WeekStart);
            int weekOffset = (candidateWeek - anchorWeek).Days / 7;
            if (weekOffset % Interval != 0 || !weekdays.Contains(candidate.DayOfWeek))
            {
                continue;
            }

            if (MatchesMonth(candidate) && MatchesMonthDay(candidate))
            {
                yield return candidate;
            }
        }
    }

    private IEnumerable<DateTime> EnumerateMonthly(DateTime from)
    {
        for (int monthOffset = 0; monthOffset <= 4_800; monthOffset += Interval)
        {
            DateTime month = from.Date.AddDays(1 - from.Day).AddMonths(monthOffset);
            if (ByMonth.Count > 0 && !ByMonth.Contains(month.Month))
            {
                continue;
            }

            foreach (DateTime candidate in DatesForMonth(month.Year, month.Month, from).Order())
            {
                if (candidate > from)
                {
                    yield return candidate;
                }
            }
        }
    }

    private IEnumerable<DateTime> EnumerateYearly(DateTime from)
    {
        for (int yearOffset = 0; yearOffset <= 400; yearOffset += Interval)
        {
            int year = from.Year + yearOffset;
            IEnumerable<int> months = ByMonth.Count > 0 ? ByMonth.Distinct() : [from.Month];

            foreach (int month in months.Order())
            {
                foreach (DateTime candidate in DatesForMonth(year, month, from).Order())
                {
                    if (candidate > from)
                    {
                        yield return candidate;
                    }
                }
            }
        }
    }

    private IEnumerable<DateTime> DatesForMonth(int year, int month, DateTime anchor)
    {
        int daysInMonth = DateTime.DaysInMonth(year, month);
        TimeSpan time = anchor.TimeOfDay;

        if (ByMonthDay.Count > 0)
        {
            foreach (int value in ByMonthDay.Distinct())
            {
                int day = value > 0 ? value : daysInMonth + value + 1;
                if (day < 1 || day > daysInMonth)
                {
                    continue;
                }

                DateTime candidate = new DateTime(year, month, day).Add(time);
                if (MatchesByDay(candidate, useOrdinal: true))
                {
                    yield return candidate;
                }
            }

            yield break;
        }

        if (ByDay.Count > 0)
        {
            foreach (RecurrenceDay recurrenceDay in ByDay)
            {
                if (recurrenceDay.Ordinal is not null)
                {
                    int? day = NthWeekdayOfMonth(year, month, recurrenceDay.Day, recurrenceDay.Ordinal.Value);
                    if (day is not null)
                    {
                        yield return new DateTime(year, month, day.Value).Add(time);
                    }

                    continue;
                }

                for (int day = 1; day <= daysInMonth; day++)
                {
                    DateTime candidate = new DateTime(year, month, day).Add(time);
                    if (candidate.DayOfWeek == recurrenceDay.Day)
                    {
                        yield return candidate;
                    }
                }
            }

            yield break;
        }

        if (anchor.Day <= daysInMonth)
        {
            yield return new DateTime(year, month, anchor.Day).Add(time);
        }
    }

    private bool MatchesDateFilters(DateTime candidate, bool useOrdinalByDay)
    {
        return MatchesMonth(candidate)
            && MatchesMonthDay(candidate)
            && MatchesByDay(candidate, useOrdinalByDay);
    }

    private bool MatchesMonth(DateTime candidate)
    {
        return ByMonth.Count == 0 || ByMonth.Contains(candidate.Month);
    }

    private bool MatchesMonthDay(DateTime candidate)
    {
        if (ByMonthDay.Count == 0)
        {
            return true;
        }

        int daysInMonth = DateTime.DaysInMonth(candidate.Year, candidate.Month);
        return ByMonthDay.Any(value =>
            value > 0 ? candidate.Day == value : candidate.Day == daysInMonth + value + 1);
    }

    private bool MatchesByDay(DateTime candidate, bool useOrdinal)
    {
        if (ByDay.Count == 0)
        {
            return true;
        }

        foreach (RecurrenceDay recurrenceDay in ByDay)
        {
            if (candidate.DayOfWeek != recurrenceDay.Day)
            {
                continue;
            }

            if (!useOrdinal || recurrenceDay.Ordinal is null)
            {
                return true;
            }

            int? day = NthWeekdayOfMonth(
                candidate.Year,
                candidate.Month,
                recurrenceDay.Day,
                recurrenceDay.Ordinal.Value);
            if (day == candidate.Day)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsWithinUntil(DateTimeOffset candidate, DateTime localCandidate)
    {
        if (Until is null)
        {
            return true;
        }

        return UntilIsDateOnly
            ? DateOnly.FromDateTime(localCandidate) <= DateOnly.FromDateTime(Until.Value.Date)
            : candidate.ToUniversalTime() <= Until.Value.ToUniversalTime();
    }

    private static DateTimeOffset CreateOffset(
        DateTime localCandidate,
        TimeSpan originalOffset,
        TimeZoneInfo? timeZone)
    {
        DateTime unspecified = DateTime.SpecifyKind(localCandidate, DateTimeKind.Unspecified);
        if (timeZone is null)
        {
            return new DateTimeOffset(unspecified, originalOffset);
        }

        while (timeZone.IsInvalidTime(unspecified))
        {
            unspecified = unspecified.AddMinutes(1);
        }

        TimeSpan offset = timeZone.IsAmbiguousTime(unspecified)
            ? timeZone.GetAmbiguousTimeOffsets(unspecified).Max()
            : timeZone.GetUtcOffset(unspecified);
        return new DateTimeOffset(unspecified, offset);
    }

    private static DateTime StartOfWeek(DateTime date, DayOfWeek weekStart)
    {
        int difference = (7 + (date.DayOfWeek - weekStart)) % 7;
        return date.AddDays(-difference);
    }

    private static int? NthWeekdayOfMonth(int year, int month, DayOfWeek day, int ordinal)
    {
        int daysInMonth = DateTime.DaysInMonth(year, month);
        if (ordinal > 0)
        {
            DateTime first = new(year, month, 1);
            int dayNumber = 1 + ((7 + day - first.DayOfWeek) % 7) + ((ordinal - 1) * 7);
            return dayNumber <= daysInMonth ? dayNumber : null;
        }

        DateTime last = new(year, month, daysInMonth);
        int reverseDayNumber = daysInMonth - ((7 + last.DayOfWeek - day) % 7) + ((ordinal + 1) * 7);
        return reverseDayNumber >= 1 ? reverseDayNumber : null;
    }

    private void Validate()
    {
        if (Interval < 1)
        {
            throw new InvalidOperationException("INTERVAL must be positive.");
        }

        if (Count is < 1)
        {
            throw new InvalidOperationException("COUNT must be positive.");
        }

        if (Count is not null && Until is not null)
        {
            throw new InvalidOperationException("COUNT and UNTIL are mutually exclusive.");
        }

        if (ByMonth.Any(value => value is < 1 or > 12))
        {
            throw new InvalidOperationException("BYMONTH values must be between 1 and 12.");
        }

        if (ByMonthDay.Any(value => value is 0 or < -31 or > 31))
        {
            throw new InvalidOperationException("BYMONTHDAY values must be between -31 and 31, excluding zero.");
        }
    }

    private static RecurrenceFrequency ParseFrequency(string value)
    {
        return value switch
        {
            "DAILY" => RecurrenceFrequency.Daily,
            "WEEKLY" => RecurrenceFrequency.Weekly,
            "MONTHLY" => RecurrenceFrequency.Monthly,
            "YEARLY" => RecurrenceFrequency.Yearly,
            _ => throw new FormatException($"Unsupported recurrence frequency '{value}'."),
        };
    }

    private static string FormatFrequency(RecurrenceFrequency value)
    {
        return value switch
        {
            RecurrenceFrequency.Daily => "DAILY",
            RecurrenceFrequency.Weekly => "WEEKLY",
            RecurrenceFrequency.Monthly => "MONTHLY",
            RecurrenceFrequency.Yearly => "YEARLY",
            _ => throw new InvalidOperationException("Unsupported recurrence frequency."),
        };
    }

    private static int ParsePositiveInt(string value, string componentName)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) || parsed < 1)
        {
            throw new FormatException($"{componentName} must be a positive integer.");
        }

        return parsed;
    }

    private static int ParseMonth(string value)
    {
        int parsed = ParseSignedInt(value, "BYMONTH");
        return parsed is >= 1 and <= 12
            ? parsed
            : throw new FormatException("BYMONTH values must be between 1 and 12.");
    }

    private static int ParseMonthDay(string value)
    {
        int parsed = ParseSignedInt(value, "BYMONTHDAY");
        return parsed is >= -31 and <= 31 and not 0
            ? parsed
            : throw new FormatException("BYMONTHDAY values must be between -31 and 31, excluding zero.");
    }

    private static int ParseSignedInt(string value, string componentName)
    {
        if (!int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int parsed))
        {
            throw new FormatException($"{componentName} contains an invalid integer.");
        }

        return parsed;
    }

    private static RecurrenceDay ParseDay(string value)
    {
        if (value.Length < 2)
        {
            throw new FormatException($"Invalid BYDAY value '{value}'.");
        }

        string weekdayText = value[^2..];
        DayOfWeek day = ParseWeekday(weekdayText);
        if (value.Length == 2)
        {
            return new RecurrenceDay(day);
        }

        int ordinal = ParseSignedInt(value[..^2], "BYDAY");
        try
        {
            return new RecurrenceDay(day, ordinal);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new FormatException($"Invalid BYDAY ordinal '{ordinal}'.", exception);
        }
    }

    private static string FormatDay(RecurrenceDay value)
    {
        string prefix = value.Ordinal?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        return prefix + FormatWeekday(value.Day);
    }

    private static DayOfWeek ParseWeekday(string value)
    {
        return value switch
        {
            "SU" => DayOfWeek.Sunday,
            "MO" => DayOfWeek.Monday,
            "TU" => DayOfWeek.Tuesday,
            "WE" => DayOfWeek.Wednesday,
            "TH" => DayOfWeek.Thursday,
            "FR" => DayOfWeek.Friday,
            "SA" => DayOfWeek.Saturday,
            _ => throw new FormatException($"Invalid weekday '{value}'."),
        };
    }

    private static string FormatWeekday(DayOfWeek value)
    {
        return value switch
        {
            DayOfWeek.Sunday => "SU",
            DayOfWeek.Monday => "MO",
            DayOfWeek.Tuesday => "TU",
            DayOfWeek.Wednesday => "WE",
            DayOfWeek.Thursday => "TH",
            DayOfWeek.Friday => "FR",
            DayOfWeek.Saturday => "SA",
            _ => throw new InvalidOperationException("Invalid weekday."),
        };
    }

    private static void ParseUntil(string value, RecurrenceRule rule)
    {
        if (DateOnly.TryParseExact(
            value,
            "yyyyMMdd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out DateOnly date))
        {
            rule.Until = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            rule.UntilIsDateOnly = true;
            return;
        }

        string[] formats = ["yyyyMMdd'T'HHmmss'Z'", "yyyyMMdd'T'HHmmss"];
        if (!DateTime.TryParseExact(
            value,
            formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out DateTime instant))
        {
            throw new FormatException($"Invalid UNTIL value '{value}'.");
        }

        rule.Until = new DateTimeOffset(instant, TimeSpan.Zero);
        rule.UntilIsDateOnly = false;
    }
}
