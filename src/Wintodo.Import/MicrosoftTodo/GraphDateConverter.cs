using System.Globalization;

namespace Wintodo.Import.MicrosoftTodo;

/// <summary>Converts Graph <c>dateTimeTimeZone</c> values.</summary>
public static class GraphDateConverter
{
    private static readonly string[] DateTimeFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF",
        "yyyy-MM-dd'T'HH:mm:ss",
        "yyyy-MM-dd'T'HH:mm",
        "yyyy-MM-dd",
    ];

    /// <summary>The instant the value describes, or null when it is absent or unreadable.</summary>
    public static DateTimeOffset? ToInstant(GraphDateTimeTimeZone? value)
    {
        if (!TryParseLocal(value, out DateTime local))
        {
            return null;
        }

        TimeZoneInfo zone = ResolveZone(value!.TimeZone);
        if (zone.IsInvalidTime(local))
        {
            local = local.AddHours(1);
        }

        TimeSpan offset = zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    /// <summary>
    /// The calendar date of a To Do due or start date. To Do stores these as
    /// midnight in some zone. When the stored time is midnight, its own date is
    /// the date the person picked. Otherwise the instant is read in
    /// <paramref name="localZone"/>, so a date is never shifted by the UTC offset.
    /// </summary>
    public static DateOnly? ToDate(GraphDateTimeTimeZone? value, TimeZoneInfo localZone)
    {
        ArgumentNullException.ThrowIfNull(localZone);
        if (!TryParseLocal(value, out DateTime local))
        {
            return null;
        }

        if (local.TimeOfDay == TimeSpan.Zero)
        {
            return DateOnly.FromDateTime(local);
        }

        DateTimeOffset instant = ToInstant(value)!.Value;
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, localZone).DateTime);
    }

    public static TimeZoneInfo ResolveZone(string? timeZone)
    {
        if (string.IsNullOrWhiteSpace(timeZone)
            || string.Equals(timeZone, "UTC", StringComparison.OrdinalIgnoreCase)
            || string.Equals(timeZone, "tzone://Microsoft/Utc", StringComparison.OrdinalIgnoreCase))
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            // Windows ids and IANA ids both resolve with ICU globalization.
            return TimeZoneInfo.FindSystemTimeZoneById(timeZone.Trim());
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    private static bool TryParseLocal(GraphDateTimeTimeZone? value, out DateTime local)
    {
        local = default;
        if (string.IsNullOrWhiteSpace(value?.DateTime))
        {
            return false;
        }

        string text = value.DateTime.Trim();
        if (text.EndsWith('Z'))
        {
            text = text[..^1];
        }

        if (!DateTime.TryParseExact(
                text,
                DateTimeFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime parsed))
        {
            return false;
        }

        local = DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
        return true;
    }
}
