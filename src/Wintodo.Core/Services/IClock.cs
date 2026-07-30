namespace Wintodo.Core.Services;

public interface IClock
{
    DateTimeOffset UtcNow { get; }

    TimeZoneInfo LocalTimeZone { get; }

    DateOnly Today
    {
        get
        {
            DateTime local = TimeZoneInfo.ConvertTime(UtcNow, LocalTimeZone).DateTime;
            return DateOnly.FromDateTime(local);
        }
    }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public TimeZoneInfo LocalTimeZone => TimeZoneInfo.Local;
}
