using System.Globalization;
using System.Text;
using Wintodo.Core.Models;
using TaskStatus = Wintodo.Core.Models.TaskStatus;

namespace Wintodo.Core.Ical;

public sealed class IcalReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private readonly TimeZoneInfo _localTimeZone;

    public IcalReader(TimeZoneInfo localTimeZone)
    {
        ArgumentNullException.ThrowIfNull(localTimeZone);
        _localTimeZone = localTimeZone;
    }

    public IcalCalendar Read(ReadOnlySpan<byte> content)
    {
        string text;
        try
        {
            text = StrictUtf8.GetString(content);
        }
        catch (DecoderFallbackException exception)
        {
            throw new IcalFormatException("The file is not valid UTF-8.", innerException: exception);
        }

        return Read(text);
    }

    public IcalCalendar Read(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length > 0 && content[0] == '\uFEFF')
        {
            content = content[1..];
        }

        IReadOnlyList<LogicalLine> lines = UnfoldLines(content);
        var calendar = new IcalCalendar();
        var components = new List<string>();
        var taskUids = new HashSet<string>(StringComparer.Ordinal);
        TodoBuilder? todo = null;
        AlarmBuilder? alarm = null;
        bool sawCalendar = false;
        bool sawVersion = false;

        foreach (LogicalLine line in lines)
        {
            if (string.IsNullOrWhiteSpace(line.Unfolded))
            {
                continue;
            }

            IcalProperty property = ParseProperty(line);
            if (string.Equals(property.Name, "BEGIN", StringComparison.OrdinalIgnoreCase))
            {
                string component = property.Value.Trim().ToUpperInvariant();
                if (components.Count == 0 && component != "VCALENDAR")
                {
                    throw Error(line, "The root component must be VCALENDAR.");
                }

                components.Add(component);
                if (component == "VCALENDAR")
                {
                    if (components.Count != 1 || sawCalendar)
                    {
                        throw Error(line, "VCALENDAR cannot be nested or repeated.");
                    }

                    sawCalendar = true;
                }
                else if (component == "VTODO" && IsDirectChild(components, "VCALENDAR", "VTODO"))
                {
                    todo = new TodoBuilder(line.Number);
                }
                else if (component == "VALARM" && todo is not null && IsDirectChild(components, "VTODO", "VALARM"))
                {
                    if (alarm is not null)
                    {
                        throw Error(line, "VALARM cannot be nested.");
                    }

                    alarm = new AlarmBuilder(line.Number);
                }

                continue;
            }

            if (string.Equals(property.Name, "END", StringComparison.OrdinalIgnoreCase))
            {
                string component = property.Value.Trim().ToUpperInvariant();
                if (components.Count == 0 || components[^1] != component)
                {
                    throw Error(line, $"END:{component} does not match the open component.");
                }

                if (component == "VALARM" && alarm is not null && todo is not null)
                {
                    todo.AddAlarm(alarm.Finish(line.Number));
                    alarm = null;
                }
                else if (component == "VTODO" && todo is not null)
                {
                    TodoTask task = todo.Finish(line.Number);
                    if (!taskUids.Add(task.Uid))
                    {
                        throw Error(line, $"Task UID '{task.Uid}' occurs more than once.");
                    }

                    calendar.Tasks.Add(task);
                    todo = null;
                }

                components.RemoveAt(components.Count - 1);
                continue;
            }

            if (components.Count == 1 && components[0] == "VCALENDAR")
            {
                ParseCalendarProperty(calendar, property, line, ref sawVersion);
            }
            else if (alarm is not null && components.Count > 0 && components[^1] == "VALARM")
            {
                ParseAlarmProperty(alarm, property, line);
            }
            else if (todo is not null && components.Count > 0 && components[^1] == "VTODO")
            {
                ParseTodoProperty(todo, property, line);
            }
        }

        if (components.Count != 0)
        {
            throw new IcalFormatException($"The {components[^1]} component is not closed.");
        }

        if (!sawCalendar)
        {
            throw new IcalFormatException("The file does not contain a VCALENDAR component.");
        }

        if (!sawVersion)
        {
            throw new IcalFormatException("VCALENDAR must contain VERSION:2.0.");
        }

        return calendar;
    }

    private static bool IsDirectChild(IReadOnlyList<string> components, string parent, string child)
    {
        return components.Count >= 2
            && components[^2] == parent
            && components[^1] == child;
    }

    private static void ParseCalendarProperty(
        IcalCalendar calendar,
        IcalProperty property,
        LogicalLine line,
        ref bool sawVersion)
    {
        switch (property.Name)
        {
            case "VERSION":
                if (!string.Equals(property.Value.Trim(), "2.0", StringComparison.Ordinal))
                {
                    throw Error(line, "Only iCalendar VERSION:2.0 is supported.");
                }

                sawVersion = true;
                break;
            case "X-WR-CALNAME":
                calendar.Name = UnescapeText(property.Value);
                break;
            case "X-WINTODO-LIST-COLOR":
                if (Enum.TryParse(property.Value, ignoreCase: true, out ListColor color))
                {
                    calendar.Color = color;
                }

                break;
        }
    }

    private void ParseTodoProperty(
        TodoBuilder todo,
        IcalProperty property,
        LogicalLine line)
    {
        todo.MarkPresent(property.Name);
        TodoTask task = todo.Task;
        switch (property.Name)
        {
            case "UID":
                task.Uid = UnescapeText(property.Value).Trim();
                break;
            case "SUMMARY":
                task.Summary = UnescapeText(property.Value);
                break;
            case "DESCRIPTION":
                task.Description = UnescapeText(property.Value);
                break;
            case "STATUS":
                task.Status = ParseStatus(property.Value, line);
                break;
            case "PERCENT-COMPLETE":
                task.PercentComplete = ParseInteger(property.Value, 0, 100, property.Name, line);
                break;
            case "PRIORITY":
                task.Priority = ParseInteger(property.Value, 0, 9, property.Name, line);
                break;
            case "DUE":
            {
                ParsedDate date = ParseDate(property, line);
                task.Due = date.Value;
                task.DueIsDateOnly = date.IsDateOnly;
                task.DueTzid = date.TimeZoneMarker;
                break;
            }
            case "DTSTART":
            {
                ParsedDate date = ParseDate(property, line);
                task.Start = date.Value;
                task.StartIsDateOnly = date.IsDateOnly;
                if (!date.IsDateOnly && date.TimeZoneMarker is not null)
                {
                    todo.Unknown.Add(new IcalUnknownEntry(
                        IcalUnknownCodec.StartTimeZoneContext,
                        date.TimeZoneMarker));
                }

                break;
            }
            case "COMPLETED":
                task.CompletedAt = ParseRequiredDateTime(property, line);
                break;
            case "CREATED":
                task.Created = ParseRequiredDateTime(property, line);
                break;
            case "LAST-MODIFIED":
                task.LastModified = ParseRequiredDateTime(property, line);
                break;
            case "DTSTAMP":
                _ = ParseRequiredDateTime(property, line);
                break;
            case "SEQUENCE":
                task.Sequence = ParseInteger(property.Value, 0, int.MaxValue, property.Name, line);
                break;
            case "RRULE":
                task.RRule = property.Value;
                break;
            case "RELATED-TO":
                if (string.Equals(
                        property.GetParameter("RELTYPE"),
                        "PARENT",
                        StringComparison.OrdinalIgnoreCase))
                {
                    task.ParentUid = UnescapeText(property.Value).Trim();
                }
                else
                {
                    todo.AddUnknown(line.Raw);
                }

                break;
            case "CATEGORIES":
                todo.Unknown.Add(new IcalUnknownEntry(
                    IcalUnknownCodec.CategoriesOverrideContext,
                    line.Raw));
                foreach (string category in SplitEscaped(property.Value, ','))
                {
                    string value = UnescapeText(category);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        task.Categories.Add(value);
                    }
                }

                break;
            case "LOCATION":
                task.Location = UnescapeText(property.Value);
                break;
            case "URL":
                task.Url = property.Value;
                break;
            case "CLASS":
                task.Class = ParseClassification(property.Value, line);
                break;
            case "X-APPLE-SORT-ORDER":
                if (!double.TryParse(
                        property.Value,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out double sortOrder)
                    || !double.IsFinite(sortOrder))
                {
                    throw Error(line, "X-APPLE-SORT-ORDER must be numeric.");
                }

                task.SortOrder = sortOrder;
                break;
            case "X-WINTODO-MYDAY":
                task.MyDayDate = ParseMyDayDate(property.Value, line);
                break;
            case "ATTACH":
                // Imported attachments remain URI properties until an explicit local copy exists.
                todo.AddUnknown(line.Raw);
                break;
            default:
                todo.AddUnknown(line.Raw);
                break;
        }
    }

    private void ParseAlarmProperty(
        AlarmBuilder alarm,
        IcalProperty property,
        LogicalLine line)
    {
        switch (property.Name)
        {
            case "UID":
                alarm.Alarm.Uid = UnescapeText(property.Value).Trim();
                break;
            case "ACTION":
                alarm.Alarm.Action = property.Value.Trim().ToUpperInvariant() switch
                {
                    "DISPLAY" => AlarmAction.Display,
                    "AUDIO" => AlarmAction.Audio,
                    "EMAIL" => AlarmAction.Email,
                    _ => throw Error(line, $"Unsupported VALARM ACTION '{property.Value}'."),
                };
                alarm.HasAction = true;
                break;
            case "TRIGGER":
                ParseTrigger(alarm.Alarm, property, line);
                alarm.HasTrigger = true;
                break;
            case "DESCRIPTION":
                alarm.Alarm.Description = UnescapeText(property.Value);
                break;
            default:
                alarm.Unknown.Add(line.Raw);
                break;
        }
    }

    private void ParseTrigger(TaskAlarm alarm, IcalProperty property, LogicalLine line)
    {
        string valueType = property.GetParameter("VALUE") ?? string.Empty;
        bool isDuration = string.Equals(valueType, "DURATION", StringComparison.OrdinalIgnoreCase)
            || property.Value.StartsWith("P", StringComparison.OrdinalIgnoreCase)
            || property.Value.StartsWith("-P", StringComparison.OrdinalIgnoreCase)
            || property.Value.StartsWith("+P", StringComparison.OrdinalIgnoreCase);
        if (!isDuration)
        {
            alarm.Trigger = ParseRequiredDateTime(property, line);
            alarm.TriggerOffsetSeconds = null;
            return;
        }

        try
        {
            alarm.TriggerOffsetSeconds = ParseDurationSeconds(property.Value);
            alarm.Trigger = null;
            alarm.TriggerRelated = string.Equals(
                property.GetParameter("RELATED"),
                "END",
                StringComparison.OrdinalIgnoreCase)
                ? AlarmRelation.End
                : AlarmRelation.Start;
        }
        catch (Exception exception) when (
            exception is FormatException or OverflowException)
        {
            throw Error(line, "VALARM TRIGGER contains an invalid duration.", exception);
        }
    }

    private DateTimeOffset ParseRequiredDateTime(IcalProperty property, LogicalLine line)
    {
        ParsedDate parsed = ParseDate(property, line);
        if (parsed.IsDateOnly)
        {
            throw Error(line, $"{property.Name} must be a DATE-TIME value.");
        }

        return parsed.Value;
    }

    private ParsedDate ParseDate(IcalProperty property, LogicalLine line)
    {
        string value = property.Value.Trim();
        bool isDateOnly = string.Equals(
                property.GetParameter("VALUE"),
                "DATE",
                StringComparison.OrdinalIgnoreCase)
            || (value.Length == 8 && !value.Contains('T'));
        if (isDateOnly)
        {
            DateOnly date = ParseDateOnly(value, line);
            return new ParsedDate(
                new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                IsDateOnly: true,
                TimeZoneMarker: null);
        }

        bool isUtc = value.EndsWith('Z');
        string dateTimeText = isUtc ? value[..^1] : value;
        if (!DateTime.TryParseExact(
                dateTimeText,
                "yyyyMMdd'T'HHmmss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime localDateTime))
        {
            throw Error(line, $"{property.Name} contains an invalid DATE-TIME value.");
        }

        localDateTime = DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified);
        if (isUtc)
        {
            return new ParsedDate(
                new DateTimeOffset(localDateTime, TimeSpan.Zero),
                IsDateOnly: false,
                TimeZoneMarker: null);
        }

        string? tzid = property.GetParameter("TZID");
        TimeZoneInfo timeZone = _localTimeZone;
        string marker = string.Empty;
        if (!string.IsNullOrWhiteSpace(tzid))
        {
            try
            {
                timeZone = TimeZoneInfo.FindSystemTimeZoneById(tzid);
            }
            catch (Exception exception) when (
                exception is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                throw Error(line, $"The time zone '{tzid}' is not available on this system.", exception);
            }

            marker = tzid;
        }

        if (timeZone.IsInvalidTime(localDateTime))
        {
            throw Error(line, $"{property.Name} falls in a skipped local-time interval.");
        }

        TimeSpan offset = timeZone.GetUtcOffset(localDateTime);
        return new ParsedDate(
            new DateTimeOffset(localDateTime, offset).ToUniversalTime(),
            IsDateOnly: false,
            TimeZoneMarker: marker);
    }

    private static DateOnly ParseDateOnly(string value, LogicalLine line)
    {
        if (!DateOnly.TryParseExact(
                value,
                "yyyyMMdd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateOnly result))
        {
            throw Error(line, "The property contains an invalid DATE value.");
        }

        return result;
    }

    private static DateOnly ParseMyDayDate(string value, LogicalLine line)
    {
        if (!DateOnly.TryParseExact(
                value,
                ["yyyy-MM-dd", "yyyyMMdd"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateOnly result))
        {
            throw Error(line, "X-WINTODO-MYDAY contains an invalid date.");
        }

        return result;
    }

    private static long ParseDurationSeconds(string value)
    {
        value = value.Trim();
        int position = 0;
        bool negative = false;
        if (position < value.Length && value[position] is '+' or '-')
        {
            negative = value[position] == '-';
            position++;
        }

        if (position >= value.Length || value[position] != 'P')
        {
            throw new FormatException("An iCalendar duration must begin with P.");
        }

        position++;
        if (position >= value.Length)
        {
            throw new FormatException("An iCalendar duration has no value.");
        }

        long totalSeconds = 0;
        if (char.IsAsciiDigit(value[position]))
        {
            int numberStart = position;
            long number = ParseDurationNumber(value, ref position);
            if (position < value.Length && value[position] == 'W')
            {
                position++;
                if (position != value.Length)
                {
                    throw new FormatException("Weeks cannot be combined with other duration units.");
                }

                totalSeconds = checked(number * 7 * 24 * 60 * 60);
                return negative ? checked(-totalSeconds) : totalSeconds;
            }

            if (position < value.Length && value[position] == 'D')
            {
                position++;
                totalSeconds = checked(number * 24 * 60 * 60);
                if (position == value.Length)
                {
                    return negative ? checked(-totalSeconds) : totalSeconds;
                }
            }
            else
            {
                position = numberStart;
            }
        }

        if (position >= value.Length || value[position] != 'T')
        {
            throw new FormatException("An iCalendar duration contains an invalid date unit.");
        }

        position++;
        bool foundTimeUnit = false;
        int lastUnitRank = 0;
        while (position < value.Length)
        {
            long number = ParseDurationNumber(value, ref position);
            if (position >= value.Length)
            {
                throw new FormatException("An iCalendar duration is missing a time unit.");
            }

            (int rank, long multiplier) = value[position++] switch
            {
                'H' => (1, 60L * 60),
                'M' => (2, 60L),
                'S' => (3, 1L),
                _ => throw new FormatException("An iCalendar duration contains an invalid time unit."),
            };
            if (rank <= lastUnitRank)
            {
                throw new FormatException("iCalendar duration time units must be ordered and unique.");
            }

            lastUnitRank = rank;
            foundTimeUnit = true;
            totalSeconds = checked(totalSeconds + checked(number * multiplier));
        }

        if (!foundTimeUnit)
        {
            throw new FormatException("An iCalendar duration has no time value.");
        }

        return negative ? checked(-totalSeconds) : totalSeconds;
    }

    private static long ParseDurationNumber(string value, ref int position)
    {
        int start = position;
        long result = 0;
        while (position < value.Length && char.IsAsciiDigit(value[position]))
        {
            result = checked((result * 10) + (value[position] - '0'));
            position++;
        }

        if (position == start)
        {
            throw new FormatException("An iCalendar duration unit has no number.");
        }

        return result;
    }

    private static TaskStatus ParseStatus(string value, LogicalLine line)
    {
        return value.Trim().ToUpperInvariant() switch
        {
            "NEEDS-ACTION" => TaskStatus.NeedsAction,
            "IN-PROCESS" => TaskStatus.InProcess,
            "COMPLETED" => TaskStatus.Completed,
            "CANCELLED" => TaskStatus.Cancelled,
            _ => throw Error(line, $"Unsupported VTODO STATUS '{value}'."),
        };
    }

    private static Classification ParseClassification(string value, LogicalLine line)
    {
        return value.Trim().ToUpperInvariant() switch
        {
            "PUBLIC" => Classification.Public,
            "PRIVATE" => Classification.Private,
            "CONFIDENTIAL" => Classification.Confidential,
            _ => throw Error(line, $"Unsupported CLASS value '{value}'."),
        };
    }

    private static int ParseInteger(
        string value,
        int minimum,
        int maximum,
        string propertyName,
        LogicalLine line)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
            || result < minimum
            || result > maximum)
        {
            throw Error(line, $"{propertyName} must be between {minimum} and {maximum}.");
        }

        return result;
    }

    private static IcalProperty ParseProperty(LogicalLine line)
    {
        int colon = FindDelimiter(line.Unfolded, ':');
        if (colon <= 0)
        {
            throw Error(line, "A content line must contain a property name and ':'.");
        }

        string declaration = line.Unfolded[..colon];
        string value = line.Unfolded[(colon + 1)..];
        IReadOnlyList<string> parts = SplitOutsideQuotes(declaration, ';');
        string name = parts[0].Trim().ToUpperInvariant();
        if (name.Length == 0)
        {
            throw Error(line, "A property name cannot be empty.");
        }

        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 1; index < parts.Count; index++)
        {
            int equals = FindDelimiter(parts[index], '=');
            if (equals <= 0)
            {
                throw Error(line, $"Property parameter '{parts[index]}' is invalid.");
            }

            string parameterName = parts[index][..equals].Trim();
            string parameterValue = parts[index][(equals + 1)..].Trim();
            if (parameterValue.Length >= 2
                && parameterValue[0] == '"'
                && parameterValue[^1] == '"')
            {
                parameterValue = parameterValue[1..^1];
            }

            parameters[parameterName] = DecodeParameterValue(parameterValue);
        }

        return new IcalProperty(name, value, parameters);
    }

    private static int FindDelimiter(string value, char delimiter)
    {
        bool quoted = false;
        for (int index = 0; index < value.Length; index++)
        {
            if (value[index] == '"')
            {
                quoted = !quoted;
            }
            else if (!quoted && value[index] == delimiter)
            {
                return index;
            }
        }

        return -1;
    }

    private static IReadOnlyList<string> SplitOutsideQuotes(string value, char delimiter)
    {
        var parts = new List<string>();
        int start = 0;
        bool quoted = false;
        for (int index = 0; index < value.Length; index++)
        {
            if (value[index] == '"')
            {
                quoted = !quoted;
            }
            else if (!quoted && value[index] == delimiter)
            {
                parts.Add(value[start..index]);
                start = index + 1;
            }
        }

        parts.Add(value[start..]);
        return parts;
    }

    private static IReadOnlyList<string> SplitEscaped(string value, char delimiter)
    {
        var parts = new List<string>();
        int start = 0;
        bool escaped = false;
        for (int index = 0; index < value.Length; index++)
        {
            char current = value[index];
            if (!escaped && current == delimiter)
            {
                parts.Add(value[start..index]);
                start = index + 1;
            }

            if (current == '\\' && !escaped)
            {
                escaped = true;
            }
            else
            {
                escaped = false;
            }
        }

        parts.Add(value[start..]);
        return parts;
    }

    private static string DecodeParameterValue(string value)
    {
        return value
            .Replace("^n", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("^'", "\"", StringComparison.Ordinal)
            .Replace("^^", "^", StringComparison.Ordinal);
    }

    internal static string UnescapeText(string value)
    {
        var output = new StringBuilder(value.Length);
        for (int index = 0; index < value.Length; index++)
        {
            char current = value[index];
            if (current != '\\' || index + 1 >= value.Length)
            {
                output.Append(current);
                continue;
            }

            char escaped = value[++index];
            output.Append(escaped is 'n' or 'N' ? '\n' : escaped);
        }

        return output.ToString();
    }

    private static IReadOnlyList<LogicalLine> UnfoldLines(string content)
    {
        var result = new List<LogicalLine>();
        int lineNumber = 1;
        int position = 0;
        while (position < content.Length)
        {
            int lineStart = position;
            while (position < content.Length && content[position] is not '\r' and not '\n')
            {
                position++;
            }

            string text = content[lineStart..position];
            string terminator = string.Empty;
            if (position < content.Length)
            {
                if (content[position] == '\r'
                    && position + 1 < content.Length
                    && content[position + 1] == '\n')
                {
                    terminator = "\r\n";
                    position += 2;
                }
                else
                {
                    terminator = content[position].ToString();
                    position++;
                }
            }

            if (text.Length > 0 && text[0] is ' ' or '\t')
            {
                if (result.Count == 0)
                {
                    throw new IcalFormatException("A folded line has no preceding content line.", lineNumber);
                }

                LogicalLine previous = result[^1];
                previous.Unfolded += text[1..];
                previous.Raw += text + terminator;
            }
            else
            {
                result.Add(new LogicalLine(text, text + terminator, lineNumber));
            }

            lineNumber++;
        }

        return result;
    }

    private static IcalFormatException Error(
        LogicalLine line,
        string message,
        Exception? innerException = null)
    {
        return new IcalFormatException(message, line.Number, innerException);
    }

    private sealed class LogicalLine(
        string unfolded,
        string raw,
        int number)
    {
        internal string Unfolded { get; set; } = unfolded;

        internal string Raw { get; set; } = raw;

        internal int Number { get; } = number;

    }

    private sealed class IcalProperty(
        string name,
        string value,
        IReadOnlyDictionary<string, string> parameters)
    {
        internal string Name { get; } = name;

        internal string Value { get; } = value;

        internal string? GetParameter(string name)
        {
            return parameters.TryGetValue(name, out string? value) ? value : null;
        }
    }

    private sealed class TodoBuilder(int startLine)
    {
        internal TodoTask Task { get; } = new();

        internal List<IcalUnknownEntry> Unknown { get; } = [];

        private HashSet<string> PresentProperties { get; } = new(StringComparer.Ordinal);

        internal void MarkPresent(string propertyName)
        {
            PresentProperties.Add(propertyName);
        }

        internal void AddUnknown(string raw)
        {
            Unknown.Add(new IcalUnknownEntry(IcalUnknownCodec.VTodoContext, raw));
        }

        internal void AddAlarm(FinishedAlarm alarm)
        {
            int alarmIndex = Task.Alarms.Count;
            string context = string.IsNullOrWhiteSpace(alarm.Alarm.Uid)
                ? IcalUnknownCodec.AlarmIndexContext(alarmIndex)
                : IcalUnknownCodec.AlarmContext(alarm.Alarm.Uid);
            Task.Alarms.Add(alarm.Alarm);
            foreach (string raw in alarm.Unknown)
            {
                Unknown.Add(new IcalUnknownEntry(context, raw));
            }
        }

        internal TodoTask Finish(int endLine)
        {
            if (string.IsNullOrWhiteSpace(Task.Uid))
            {
                throw new IcalFormatException("VTODO must contain a non-empty UID.", startLine);
            }

            if (string.IsNullOrWhiteSpace(Task.Summary))
            {
                throw new IcalFormatException("VTODO must contain a non-empty SUMMARY.", startLine);
            }

            if (Task.Status == TaskStatus.Completed)
            {
                Task.PercentComplete ??= 100;
            }

            Unknown.Add(new IcalUnknownEntry(
                IcalUnknownCodec.PresenceContext,
                string.Join(',', PresentProperties.Order(StringComparer.Ordinal))));
            Task.IcalUnknown = IcalUnknownCodec.Encode(Unknown);
            return Task;
        }
    }

    private sealed class AlarmBuilder(int startLine)
    {
        internal TaskAlarm Alarm { get; } = new();

        internal List<string> Unknown { get; } = [];

        internal bool HasAction { get; set; }

        internal bool HasTrigger { get; set; }

        internal FinishedAlarm Finish(int endLine)
        {
            if (!HasAction)
            {
                throw new IcalFormatException("VALARM must contain ACTION.", startLine);
            }

            if (!HasTrigger)
            {
                throw new IcalFormatException("VALARM must contain TRIGGER.", startLine);
            }

            return new FinishedAlarm(Alarm, Unknown);
        }
    }

    private readonly record struct FinishedAlarm(TaskAlarm Alarm, IReadOnlyList<string> Unknown);

    private readonly record struct ParsedDate(
        DateTimeOffset Value,
        bool IsDateOnly,
        string? TimeZoneMarker);
}
