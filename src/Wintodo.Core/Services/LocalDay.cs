namespace Wintodo.Core.Services;

public static class LocalDay
{
    public static DateTimeOffset StartUtc(DateOnly date, TimeZoneInfo zone)
    {
        DateTime start = date.ToDateTime(TimeOnly.MinValue);
        if (zone.IsInvalidTime(start))
        {
            // Midnight can fall inside a clock jump. Find the first valid tick after it.
            DateTime valid = start.AddHours(1);
            while (zone.IsInvalidTime(valid))
            {
                valid = valid.AddHours(1);
            }

            long lower = start.Ticks;
            long upper = valid.Ticks;
            while (upper - lower > 1)
            {
                long middle = lower + ((upper - lower) / 2);
                if (zone.IsInvalidTime(new DateTime(middle))) lower = middle;
                else upper = middle;
            }
            start = new DateTime(upper);
        }

        // The larger offset is the earlier UTC instant when midnight occurs twice.
        TimeSpan offset = zone.IsAmbiguousTime(start)
            ? zone.GetAmbiguousTimeOffsets(start).Max()
            : zone.GetUtcOffset(start);
        return new DateTimeOffset(start, offset).ToUniversalTime();
    }
}
