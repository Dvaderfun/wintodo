using System.Globalization;
using Wintodo.Core.Services;

namespace Wintodo.Core.Presentation;

public static class TaskDateLabels
{
    public static string FormatCreated(
        DateTimeOffset localCreated,
        string format,
        CultureInfo culture)
    {
        return string.Format(culture, format, localCreated);
    }

    public static string FormatListDue(
        DateOnly localDue,
        IClock clock,
        CultureInfo culture,
        string yesterday,
        string today,
        string tomorrow)
    {
        int offset = localDue.DayNumber - clock.Today.DayNumber;
        return offset switch
        {
            -1 => yesterday,
            0 => today,
            1 => tomorrow,
            >= 2 and <= 7 => localDue.ToString("dddd", culture),
            _ => localDue.ToString("d MMM", culture),
        };
    }

    public static string FormatDetailDue(
        DateOnly localDue,
        IClock clock,
        CultureInfo culture,
        string today,
        string tomorrow,
        string nextWeek)
    {
        int offset = localDue.DayNumber - clock.Today.DayNumber;
        return offset switch
        {
            0 => today,
            1 => tomorrow,
            7 => nextWeek,
            _ => localDue.ToString("ddd, d MMM", culture),
        };
    }
}
