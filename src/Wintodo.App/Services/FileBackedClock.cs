using System.Globalization;
using Wintodo.Core.Services;

namespace Wintodo.Services;

internal sealed class FileBackedClock : IClock
{
    private readonly string _path;

    internal FileBackedClock(string path)
    {
        _path = Path.GetFullPath(path);
    }

    public DateTimeOffset UtcNow
    {
        get
        {
            string value = File.ReadAllText(_path).Trim();
            return DateTimeOffset.Parse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        }
    }

    public TimeZoneInfo LocalTimeZone => TimeZoneInfo.Local;
}
