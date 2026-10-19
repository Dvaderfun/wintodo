using System.Globalization;
using System.Text;
using Wintodo.Core.Models;
using TaskStatus = Wintodo.Core.Models.TaskStatus;

namespace Wintodo.Core.Ical;

public sealed class IcalWriter
{
    private static readonly UTF8Encoding Utf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private readonly TimeZoneInfo _localTimeZone;

    public IcalWriter(TimeZoneInfo localTimeZone)
    {
        ArgumentNullException.ThrowIfNull(localTimeZone);
        _localTimeZone = localTimeZone;
    }

    public byte[] WriteCalendar(
        TodoList list,
        IEnumerable<TodoTask> tasks,
        DateTimeOffset timestamp,
        string? attachmentRoot = null)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(tasks);
        return Write(
            list.DisplayName,
            list.Color,
            list.IconEmoji,
            tasks,
            timestamp,
            attachmentRoot);
    }

    public byte[] WriteCalendar(
        IcalCalendar calendar,
        DateTimeOffset timestamp,
        string? attachmentRoot = null)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        return Write(
            calendar.Name,
            calendar.Color,
            calendar.IconEmoji,
            calendar.Tasks,
            timestamp,
            attachmentRoot);
    }

    private byte[] Write(
        string? calendarName,
        ListColor? color,
        string? iconEmoji,
        IEnumerable<TodoTask> tasks,
        DateTimeOffset timestamp,
        string? attachmentRoot)
    {
        var output = new StringBuilder();
        AppendLine(output, "BEGIN:VCALENDAR");
        AppendLine(output, "PRODID:-//Wintodo//Wintodo 1.0//EN");
        AppendLine(output, "VERSION:2.0");
        if (!string.IsNullOrWhiteSpace(calendarName))
        {
            AppendLine(output, "X-WR-CALNAME:" + EscapeText(calendarName));
        }

        if (color is not null)
        {
            AppendLine(output, "X-WINTODO-LIST-COLOR:" + color.Value);
        }

        if (ListEmoji.TryNormalize(iconEmoji, out string? normalizedIcon)
            && normalizedIcon is not null)
        {
            AppendLine(output, "X-WINTODO-LIST-ICON:" + EscapeText(normalizedIcon));
        }

        foreach (TodoTask task in tasks)
        {
            AppendTask(output, task, timestamp, attachmentRoot);
        }

        AppendLine(output, "END:VCALENDAR");
        return Utf8.GetBytes(output.ToString());
    }

    private void AppendTask(
        StringBuilder output,
        TodoTask task,
        DateTimeOffset timestamp,
        string? attachmentRoot)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (string.IsNullOrWhiteSpace(task.Uid))
        {
            throw new ArgumentException("A task UID is required for iCalendar export.", nameof(task));
        }

        if (string.IsNullOrWhiteSpace(task.Summary))
        {
            throw new ArgumentException("A task summary is required for iCalendar export.", nameof(task));
        }

        IReadOnlyList<IcalUnknownEntry> unknown = IcalUnknownCodec.Decode(task.IcalUnknown);
        HashSet<string>? present = ReadPresence(unknown);
        AppendLine(output, "BEGIN:VTODO");
        AppendLine(output, "UID:" + EscapeText(task.Uid));
        AppendLine(output, "DTSTAMP:" + FormatUtc(timestamp));
        AppendLine(output, "SUMMARY:" + EscapeText(task.Summary));

        if (ShouldWrite(present, "DESCRIPTION", task.Description is not null))
        {
            AppendLine(output, "DESCRIPTION:" + EscapeText(task.Description ?? string.Empty));
        }

        if (ShouldWrite(present, "STATUS", alwaysForLocalTask: true))
        {
            AppendLine(output, "STATUS:" + FormatStatus(task.Status));
        }

        if (ShouldWrite(present, "PERCENT-COMPLETE", task.PercentComplete is not null))
        {
            int percent = task.PercentComplete ?? (task.Status == TaskStatus.Completed ? 100 : 0);
            AppendLine(output, "PERCENT-COMPLETE:" + percent.ToString(CultureInfo.InvariantCulture));
        }

        if (ShouldWrite(present, "PRIORITY", task.Priority != 0))
        {
            AppendLine(output, "PRIORITY:" + task.Priority.ToString(CultureInfo.InvariantCulture));
        }

        if (ShouldWrite(present, "DUE", task.Due is not null) && task.Due is not null)
        {
            AppendDate(output, "DUE", task.Due.Value, task.DueIsDateOnly, task.DueTzid);
        }

        if (ShouldWrite(present, "DTSTART", task.Start is not null) && task.Start is not null)
        {
            IcalUnknownEntry? startTimeZone = unknown
                .Where(entry => entry.Context == IcalUnknownCodec.StartTimeZoneContext)
                .Cast<IcalUnknownEntry?>()
                .FirstOrDefault();
            AppendDate(
                output,
                "DTSTART",
                task.Start.Value,
                task.StartIsDateOnly,
                task.StartIsDateOnly ? null : startTimeZone?.Raw);
        }

        if (ShouldWrite(present, "COMPLETED", task.CompletedAt is not null)
            && task.CompletedAt is not null)
        {
            AppendLine(output, "COMPLETED:" + FormatUtc(task.CompletedAt.Value));
        }

        if (ShouldWrite(present, "CREATED", task.Created != default))
        {
            AppendLine(output, "CREATED:" + FormatUtc(task.Created));
        }

        if (ShouldWrite(present, "LAST-MODIFIED", task.LastModified != default))
        {
            AppendLine(output, "LAST-MODIFIED:" + FormatUtc(task.LastModified));
        }

        if (ShouldWrite(present, "SEQUENCE", alwaysForLocalTask: true))
        {
            AppendLine(output, "SEQUENCE:" + task.Sequence.ToString(CultureInfo.InvariantCulture));
        }

        if (ShouldWrite(present, "RRULE", task.RRule is not null) && task.RRule is not null)
        {
            AppendLine(output, "RRULE:" + task.RRule);
        }

        if (ShouldWrite(present, "RELATED-TO", task.ParentUid is not null)
            && task.ParentUid is not null)
        {
            AppendLine(output, "RELATED-TO;RELTYPE=PARENT:" + EscapeText(task.ParentUid));
        }

        if (ShouldWrite(present, "CATEGORIES", task.Categories.Count > 0))
        {
            IcalUnknownEntry? categoriesOverride = unknown
                .Where(entry => entry.Context == IcalUnknownCodec.CategoriesOverrideContext)
                .Cast<IcalUnknownEntry?>()
                .FirstOrDefault();
            if (categoriesOverride is not null)
            {
                AppendRaw(output, categoriesOverride.Value.Raw);
            }
            else
            {
                AppendLine(
                    output,
                    "CATEGORIES:" + string.Join(',', task.Categories.Select(EscapeText)));
            }
        }

        if (ShouldWrite(present, "LOCATION", task.Location is not null))
        {
            AppendLine(output, "LOCATION:" + EscapeText(task.Location ?? string.Empty));
        }

        if (ShouldWrite(present, "URL", task.Url is not null))
        {
            AppendLine(output, "URL:" + (task.Url ?? string.Empty));
        }

        if (ShouldWrite(present, "CLASS", task.Class != Classification.Public))
        {
            AppendLine(output, "CLASS:" + FormatClassification(task.Class));
        }

        if (ShouldWrite(present, "X-APPLE-SORT-ORDER", alwaysForLocalTask: true))
        {
            AppendLine(
                output,
                "X-APPLE-SORT-ORDER:" + task.SortOrder.ToString("G17", CultureInfo.InvariantCulture));
        }

        if (ShouldWrite(present, "X-WINTODO-MYDAY", task.MyDayDate is not null)
            && task.MyDayDate is not null)
        {
            AppendLine(
                output,
                "X-WINTODO-MYDAY:" + task.MyDayDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        AppendUnknown(output, unknown, IcalUnknownCodec.VTodoContext);
        AppendAttachments(output, task.Attachments, attachmentRoot);

        for (int index = 0; index < task.Alarms.Count; index++)
        {
            AppendAlarm(output, task.Alarms[index], unknown, index);
        }

        AppendLine(output, "END:VTODO");
    }

    private void AppendAlarm(
        StringBuilder output,
        TaskAlarm alarm,
        IReadOnlyList<IcalUnknownEntry> unknown,
        int index)
    {
        AppendLine(output, "BEGIN:VALARM");
        if (!string.IsNullOrWhiteSpace(alarm.Uid))
        {
            AppendLine(output, "UID:" + EscapeText(alarm.Uid));
        }

        AppendLine(output, "ACTION:" + alarm.Action.ToString().ToUpperInvariant());
        if (alarm.Trigger is not null)
        {
            AppendLine(output, "TRIGGER;VALUE=DATE-TIME:" + FormatUtc(alarm.Trigger.Value));
        }
        else if (alarm.TriggerOffsetSeconds is not null)
        {
            string relation = alarm.TriggerRelated == AlarmRelation.End ? ";RELATED=END" : string.Empty;
            AppendLine(
                output,
                "TRIGGER" + relation + ";VALUE=DURATION:" +
                FormatDuration(alarm.TriggerOffsetSeconds.Value));
        }
        else
        {
            throw new ArgumentException("An alarm must have an absolute or relative trigger.", nameof(alarm));
        }

        if (alarm.Description is not null)
        {
            AppendLine(output, "DESCRIPTION:" + EscapeText(alarm.Description));
        }

        string context = string.IsNullOrWhiteSpace(alarm.Uid)
            ? IcalUnknownCodec.AlarmIndexContext(index)
            : IcalUnknownCodec.AlarmContext(alarm.Uid);
        AppendUnknown(output, unknown, context);
        AppendLine(output, "END:VALARM");
    }

    private static void AppendAttachments(
        StringBuilder output,
        IReadOnlyList<TaskAttachment> attachments,
        string? attachmentRoot)
    {
        foreach (TaskAttachment attachment in attachments)
        {
            string path = attachment.RelativePath;
            if (!string.IsNullOrWhiteSpace(attachmentRoot))
            {
                string root = Path.GetFullPath(attachmentRoot);
                string candidate = Path.GetFullPath(
                    Path.IsPathRooted(path) ? path : Path.Combine(root, path));
                if (!candidate.StartsWith(
                        root + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                path = candidate;
            }
            else if (!Path.IsPathRooted(path))
            {
                continue;
            }

            string mediaType = string.IsNullOrWhiteSpace(attachment.MimeType)
                ? "application/octet-stream"
                : attachment.MimeType;
            AppendLine(
                output,
                "ATTACH;FMTTYPE=" + EscapeParameter(mediaType) + ":" +
                new Uri(Path.GetFullPath(path)).AbsoluteUri);
        }
    }

    private void AppendDate(
        StringBuilder output,
        string propertyName,
        DateTimeOffset value,
        bool isDateOnly,
        string? timeZoneMarker)
    {
        if (isDateOnly)
        {
            AppendLine(
                output,
                propertyName + ";VALUE=DATE:" + value.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
            return;
        }

        if (timeZoneMarker is null)
        {
            AppendLine(output, propertyName + ":" + FormatUtc(value));
            return;
        }

        TimeZoneInfo timeZone = _localTimeZone;
        string parameter = string.Empty;
        if (timeZoneMarker.Length > 0)
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneMarker);
            parameter = ";TZID=" + EscapeParameter(timeZoneMarker);
        }

        DateTime local = TimeZoneInfo.ConvertTime(value, timeZone).DateTime;
        AppendLine(
            output,
            propertyName + parameter + ":" + local.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture));
    }

    private static HashSet<string>? ReadPresence(IReadOnlyList<IcalUnknownEntry> unknown)
    {
        IcalUnknownEntry? metadata = unknown
            .Where(entry => entry.Context == IcalUnknownCodec.PresenceContext)
            .Cast<IcalUnknownEntry?>()
            .FirstOrDefault();
        return metadata is null
            ? null
            : metadata.Value.Raw.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .ToHashSet(StringComparer.Ordinal);
    }

    private static bool ShouldWrite(
        HashSet<string>? present,
        string propertyName,
        bool alwaysForLocalTask)
    {
        return present?.Contains(propertyName) ?? alwaysForLocalTask;
    }

    private static void AppendUnknown(
        StringBuilder output,
        IReadOnlyList<IcalUnknownEntry> unknown,
        string context)
    {
        foreach (IcalUnknownEntry entry in unknown)
        {
            if (entry.Context == context)
            {
                AppendRaw(output, entry.Raw);
            }
        }
    }

    private static void AppendRaw(StringBuilder output, string raw)
    {
        output.Append(raw);
        if (!raw.EndsWith("\r\n", StringComparison.Ordinal)
            && !raw.EndsWith('\n')
            && !raw.EndsWith('\r'))
        {
            output.Append("\r\n");
        }
    }

    internal static void AppendLine(StringBuilder output, string line)
    {
        if (line.Contains('\r') || line.Contains('\n'))
        {
            throw new ArgumentException("An iCalendar content line cannot contain raw line breaks.", nameof(line));
        }

        int start = 0;
        int currentBytes = 0;
        int byteLimit = 75;
        for (int index = 0; index < line.Length;)
        {
            int characterLength = char.IsHighSurrogate(line[index])
                && index + 1 < line.Length
                && char.IsLowSurrogate(line[index + 1])
                ? 2
                : 1;
            int characterBytes = Utf8.GetByteCount(line.AsSpan(index, characterLength));
            if (currentBytes > 0 && currentBytes + characterBytes > byteLimit)
            {
                output.Append(line, start, index - start);
                output.Append("\r\n ");
                start = index;
                currentBytes = 0;
                byteLimit = 74;
            }

            currentBytes += characterBytes;
            index += characterLength;
        }

        output.Append(line, start, line.Length - start);
        output.Append("\r\n");
    }

    internal static string EscapeText(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace(";", "\\;", StringComparison.Ordinal)
            .Replace(",", "\\,", StringComparison.Ordinal);
    }

    internal static string EscapeParameter(string value)
    {
        bool quote = value.IndexOfAny([':', ';', ',']) >= 0;
        string escaped = value
            .Replace("^", "^^", StringComparison.Ordinal)
            .Replace("\"", "^'", StringComparison.Ordinal)
            .Replace("\n", "^n", StringComparison.Ordinal);
        return quote ? '"' + escaped + '"' : escaped;
    }

    private static string FormatUtc(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
    }

    private static string FormatStatus(TaskStatus status)
    {
        return status switch
        {
            TaskStatus.NeedsAction => "NEEDS-ACTION",
            TaskStatus.InProcess => "IN-PROCESS",
            TaskStatus.Completed => "COMPLETED",
            TaskStatus.Cancelled => "CANCELLED",
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };
    }

    private static string FormatClassification(Classification classification)
    {
        return classification switch
        {
            Classification.Public => "PUBLIC",
            Classification.Private => "PRIVATE",
            Classification.Confidential => "CONFIDENTIAL",
            _ => throw new ArgumentOutOfRangeException(nameof(classification)),
        };
    }

    private static string FormatDuration(long totalSeconds)
    {
        bool negative = totalSeconds < 0;
        ulong seconds = negative
            ? checked((ulong)(-(totalSeconds + 1)) + 1)
            : (ulong)totalSeconds;
        ulong days = seconds / 86400;
        seconds %= 86400;
        ulong hours = seconds / 3600;
        seconds %= 3600;
        ulong minutes = seconds / 60;
        seconds %= 60;

        var result = new StringBuilder();
        if (negative)
        {
            result.Append('-');
        }

        result.Append('P');
        if (days > 0)
        {
            result.Append(days.ToString(CultureInfo.InvariantCulture));
            result.Append('D');
        }

        if (hours > 0 || minutes > 0 || seconds > 0 || days == 0)
        {
            result.Append('T');
            if (hours > 0)
            {
                result.Append(hours.ToString(CultureInfo.InvariantCulture));
                result.Append('H');
            }

            if (minutes > 0)
            {
                result.Append(minutes.ToString(CultureInfo.InvariantCulture));
                result.Append('M');
            }

            if (seconds > 0 || (hours == 0 && minutes == 0))
            {
                result.Append(seconds.ToString(CultureInfo.InvariantCulture));
                result.Append('S');
            }
        }

        return result.ToString();
    }
}
