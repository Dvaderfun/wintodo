using System.Globalization;
using System.Text;

namespace Wintodo.Core.Ical;

internal readonly record struct IcalUnknownEntry(string Context, string Raw);

internal static class IcalUnknownCodec
{
    internal const string VTodoContext = "VTODO";
    internal const string StartTimeZoneContext = "META:DTSTART-TZ";
    internal const string CategoriesOverrideContext = "OVERRIDE:CATEGORIES";
    internal const string PresenceContext = "META:PRESENT";
    private const string AlarmContextPrefix = "VALARM:";
    private const string AlarmIndexContextPrefix = "VALARM-INDEX:";
    private const string Header = "WINTODO-ICAL-UNKNOWN:1\n";

    internal static string AlarmContext(string alarmUid) => AlarmContextPrefix + alarmUid;

    internal static string AlarmIndexContext(int index) =>
        AlarmIndexContextPrefix + index.ToString(CultureInfo.InvariantCulture);

    internal static string? Encode(IReadOnlyList<IcalUnknownEntry> entries)
    {
        if (entries.Count == 0)
        {
            return null;
        }

        var output = new StringBuilder(Header);
        foreach (IcalUnknownEntry entry in entries)
        {
            string context = Convert.ToBase64String(Encoding.UTF8.GetBytes(entry.Context));
            output.Append(context);
            output.Append(':');
            output.Append(entry.Raw.Length.ToString(CultureInfo.InvariantCulture));
            output.Append('\n');
            output.Append(entry.Raw);
        }

        return output.ToString();
    }

    internal static IReadOnlyList<IcalUnknownEntry> Decode(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return [];
        }

        if (!value.StartsWith(Header, StringComparison.Ordinal))
        {
            return [new IcalUnknownEntry(VTodoContext, value)];
        }

        var entries = new List<IcalUnknownEntry>();
        int position = Header.Length;
        while (position < value.Length)
        {
            int separator = value.IndexOf(':', position);
            int lineEnd = value.IndexOf('\n', separator + 1);
            if (separator < position || lineEnd < separator)
            {
                return [new IcalUnknownEntry(VTodoContext, value)];
            }

            string context;
            int length;
            try
            {
                context = Encoding.UTF8.GetString(
                    Convert.FromBase64String(value[position..separator]));
                length = int.Parse(
                    value.AsSpan(separator + 1, lineEnd - separator - 1),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture);
            }
            catch (Exception exception) when (
                exception is FormatException or ArgumentException or OverflowException)
            {
                return [new IcalUnknownEntry(VTodoContext, value)];
            }

            position = lineEnd + 1;
            if (length < 0 || position + length > value.Length)
            {
                return [new IcalUnknownEntry(VTodoContext, value)];
            }

            entries.Add(new IcalUnknownEntry(context, value.Substring(position, length)));
            position += length;
        }

        return entries;
    }
}
