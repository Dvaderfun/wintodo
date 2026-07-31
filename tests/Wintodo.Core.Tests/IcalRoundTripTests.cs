using System.Text;
using Wintodo.Core.Ical;
using Wintodo.Core.Models;

namespace Wintodo.Core.Tests;

public sealed class IcalRoundTripTests
{
    private static readonly DateTimeOffset ExportTimestamp =
        new(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);

    public static TheoryData<string> FixtureNames => new()
    {
        "nextcloud.ics",
        "tasks-org.ics",
        "thunderbird.ics",
        "apple-reminders.ics",
    };

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void Fixture_RoundTripsSemantically(string fixtureName)
    {
        byte[] source = File.ReadAllBytes(FixturePath(fixtureName));
        var reader = new IcalReader(TimeZoneInfo.Utc);
        IcalCalendar imported = reader.Read(source);

        byte[] exported = new IcalWriter(TimeZoneInfo.Utc)
            .WriteCalendar(imported, ExportTimestamp);
        IcalCalendar reparsed = reader.Read(exported);

        Assert.Equal(imported.Name, reparsed.Name);
        Assert.Equal(imported.Tasks.Count, reparsed.Tasks.Count);
        for (int index = 0; index < imported.Tasks.Count; index++)
        {
            AssertTaskEquivalent(imported.Tasks[index], reparsed.Tasks[index]);
        }

        IReadOnlyDictionary<string, string[]> sourceProperties =
            CanonicalizeVTodoProperties(source);
        IReadOnlyDictionary<string, string[]> exportedProperties =
            CanonicalizeVTodoProperties(exported);
        Assert.Equal(
            sourceProperties.Keys.Order(StringComparer.Ordinal),
            exportedProperties.Keys.Order(StringComparer.Ordinal));
        foreach ((string uid, string[] properties) in sourceProperties)
        {
            Assert.Equal(properties, exportedProperties[uid]);
        }
    }

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void Fixture_UnknownPropertiesSurviveByteForByte(string fixtureName)
    {
        byte[] source = File.ReadAllBytes(FixturePath(fixtureName));
        string sourceText = Encoding.UTF8.GetString(source);
        var reader = new IcalReader(TimeZoneInfo.Utc);
        IcalCalendar imported = reader.Read(source);
        string exported = Encoding.UTF8.GetString(
            new IcalWriter(TimeZoneInfo.Utc).WriteCalendar(imported, ExportTimestamp));

        foreach (string propertyName in UnknownPropertyNamesFor(fixtureName))
        {
            string rawProperty = ExtractRawProperty(sourceText, propertyName);
            Assert.Contains(rawProperty, exported, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Writer_FoldsAtSeventyFiveUtf8Octets_AndReaderUnfolds()
    {
        string summary = string.Concat(Enumerable.Repeat("Café ", 30));
        var calendar = new IcalCalendar();
        calendar.Tasks.Add(new TodoTask
        {
            Uid = "utf8-folding",
            Summary = summary,
            Status = Models.TaskStatus.NeedsAction,
        });

        byte[] content = new IcalWriter(TimeZoneInfo.Utc)
            .WriteCalendar(calendar, ExportTimestamp);
        string text = Encoding.UTF8.GetString(content);
        foreach (string line in text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            Assert.InRange(Encoding.UTF8.GetByteCount(line), 1, 75);
        }

        TodoTask reparsed = Assert.Single(new IcalReader(TimeZoneInfo.Utc).Read(content).Tasks);
        Assert.Equal(summary, reparsed.Summary);
    }

    [Fact]
    public void Reader_ParsesDateKindsParametersEscapesAndAlarm()
    {
        const string content = """
            BEGIN:VCALENDAR
            VERSION:2.0
            BEGIN:VTODO
            UID:mapping
            SUMMARY:Escaped\, value\; with\\slash\nand newline
            DUE;VALUE=DATE:20260802
            DTSTART;TZID="Europe/Berlin":20260801T093000
            CATEGORIES:One\,Two,Three
            BEGIN:VALARM
            UID:alarm
            ACTION:DISPLAY
            TRIGGER;RELATED=END:-PT15M
            DESCRIPTION:Reminder
            END:VALARM
            END:VTODO
            END:VCALENDAR

            """;

        TodoTask task = Assert.Single(
            new IcalReader(TimeZoneInfo.Utc).Read(Encoding.UTF8.GetBytes(content)).Tasks);

        Assert.Equal("Escaped, value; with\\slash\nand newline", task.Summary);
        Assert.True(task.DueIsDateOnly);
        Assert.Equal(new DateOnly(2026, 8, 2), DateOnly.FromDateTime(task.Due!.Value.Date));
        Assert.False(task.StartIsDateOnly);
        Assert.Equal(["One,Two", "Three"], task.Categories);
        TaskAlarm alarm = Assert.Single(task.Alarms);
        Assert.Equal(-900, alarm.TriggerOffsetSeconds);
        Assert.Equal(AlarmRelation.End, alarm.TriggerRelated);
    }

    [Fact]
    public void Reader_RejectsMalformedInputWithLineNumber()
    {
        const string content = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nBEGIN:VTODO\r\nUID:x\r\nSUMMARY:y\r\n";

        IcalFormatException exception = Assert.Throws<IcalFormatException>(
            () => new IcalReader(TimeZoneInfo.Utc).Read(Encoding.UTF8.GetBytes(content)));

        Assert.Contains("not closed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Reader_RejectsInvalidUtf8()
    {
        byte[] content = [0x42, 0x45, 0x47, 0x49, 0x4E, 0x3A, 0xFF];

        IcalFormatException exception = Assert.Throws<IcalFormatException>(
            () => new IcalReader(TimeZoneInfo.Utc).Read(content));

        Assert.Contains("UTF-8", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Reader_ParsesRfcDurationWeeks_AndRejectsMonthUnits()
    {
        const string weekAlarm = """
            BEGIN:VCALENDAR
            VERSION:2.0
            BEGIN:VTODO
            UID:week-alarm
            SUMMARY:Week alarm
            BEGIN:VALARM
            ACTION:DISPLAY
            TRIGGER:-P2W
            END:VALARM
            END:VTODO
            END:VCALENDAR

            """;
        TodoTask task = Assert.Single(
            new IcalReader(TimeZoneInfo.Utc).Read(weekAlarm).Tasks);
        Assert.Equal(-1_209_600, Assert.Single(task.Alarms).TriggerOffsetSeconds);

        string invalidMonth = weekAlarm.Replace("-P2W", "-P1M", StringComparison.Ordinal);
        Assert.Throws<IcalFormatException>(
            () => new IcalReader(TimeZoneInfo.Utc).Read(invalidMonth));
    }

    [Fact]
    public void Writer_RecomputesImportedStartWithoutLosingTimeZone()
    {
        const string content = """
            BEGIN:VCALENDAR
            VERSION:2.0
            BEGIN:VTODO
            UID:start-edit
            SUMMARY:Start edit
            DTSTART;TZID=Europe/Berlin:20260801T093000
            END:VTODO
            END:VCALENDAR

            """;
        var reader = new IcalReader(TimeZoneInfo.Utc);
        IcalCalendar calendar = reader.Read(content);
        TodoTask task = Assert.Single(calendar.Tasks);
        task.Start = task.Start!.Value.AddDays(1);

        string exported = Encoding.UTF8.GetString(
            new IcalWriter(TimeZoneInfo.Utc).WriteCalendar(calendar, ExportTimestamp));

        Assert.Contains(
            "DTSTART;TZID=Europe/Berlin:20260802T093000",
            exported,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "DTSTART;TZID=Europe/Berlin:20260801T093000",
            exported,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TextIdentifiersAndMyDayDate_RoundTripWithRequiredEscapingAndFormat()
    {
        var calendar = new IcalCalendar();
        calendar.Tasks.Add(new TodoTask
        {
            Uid = "parent,one",
            Summary = "Parent",
            MyDayDate = new DateOnly(2026, 8, 2),
        });
        calendar.Tasks.Add(new TodoTask
        {
            Uid = "child;two",
            ParentUid = "parent,one",
            Summary = "Child",
        });

        byte[] exported = new IcalWriter(TimeZoneInfo.Utc)
            .WriteCalendar(calendar, ExportTimestamp);
        string text = Encoding.UTF8.GetString(exported);
        Assert.Contains("UID:parent\\,one", text, StringComparison.Ordinal);
        Assert.Contains(
            "RELATED-TO;RELTYPE=PARENT:parent\\,one",
            text,
            StringComparison.Ordinal);
        Assert.Contains("X-WINTODO-MYDAY:2026-08-02", text, StringComparison.Ordinal);

        IcalCalendar reparsed = new IcalReader(TimeZoneInfo.Utc).Read(exported);
        Assert.Equal("parent,one", reparsed.Tasks[0].Uid);
        Assert.Equal(new DateOnly(2026, 8, 2), reparsed.Tasks[0].MyDayDate);
        Assert.Equal("parent,one", reparsed.Tasks[1].ParentUid);
    }

    [Fact]
    public void Attachments_ExportConfinedLocalFilesAndPreserveRemoteUris()
    {
        string attachmentRoot = Path.Combine(Path.GetTempPath(), "wintodo-attachments");
        var calendar = new IcalCalendar();
        var task = new TodoTask
        {
            Uid = "attachments",
            Summary = "Attachments",
        };
        task.Attachments.Add(new TaskAttachment
        {
            RelativePath = Path.Combine("attachments", "photo.png"),
            MimeType = "image/png",
        });
        task.Attachments.Add(new TaskAttachment
        {
            RelativePath = Path.Combine("..", "outside.txt"),
            MimeType = "text/plain",
        });
        task.Attachments.Add(new TaskAttachment
        {
            RelativePath = Path.GetFullPath(Path.Combine(attachmentRoot, "..", "absolute.txt")),
            MimeType = "text/plain",
        });
        calendar.Tasks.Add(task);

        string localUri = new Uri(Path.GetFullPath(Path.Combine(
            attachmentRoot,
            "attachments",
            "photo.png"))).AbsoluteUri;
        byte[] exported = new IcalWriter(TimeZoneInfo.Utc)
            .WriteCalendar(calendar, ExportTimestamp, attachmentRoot);
        IReadOnlyList<string> exportedLines = Unfold(exported);
        Assert.Contains("ATTACH;FMTTYPE=image/png:" + localUri, exportedLines);
        Assert.DoesNotContain(
            exportedLines,
            line => line.Contains("outside.txt", StringComparison.Ordinal));
        Assert.DoesNotContain(
            exportedLines,
            line => line.Contains("absolute.txt", StringComparison.Ordinal));

        const string remoteCalendar = """
            BEGIN:VCALENDAR
            VERSION:2.0
            BEGIN:VTODO
            UID:remote-attachment
            SUMMARY:Remote attachment
            ATTACH;FMTTYPE=image/jpeg:https://example.test/image.jpg
            END:VTODO
            END:VCALENDAR

            """;
        IcalCalendar imported = new IcalReader(TimeZoneInfo.Utc).Read(remoteCalendar);
        IReadOnlyList<string> remoteExport = Unfold(
            new IcalWriter(TimeZoneInfo.Utc).WriteCalendar(imported, ExportTimestamp));
        Assert.Contains(
            "ATTACH;FMTTYPE=image/jpeg:https://example.test/image.jpg",
            remoteExport);
    }

    private static string FixturePath(string fixtureName)
    {
        return Path.Combine(AppContext.BaseDirectory, "fixtures", "ical", fixtureName);
    }

    private static IReadOnlyList<string> UnknownPropertyNamesFor(string fixtureName)
    {
        return fixtureName switch
        {
            "nextcloud.ics" => ["X-OC-HIDESUBTASKS"],
            "tasks-org.ics" => ["X-TASKS-ORG-ORDER"],
            "thunderbird.ics" =>
            [
                "X-MOZ-LASTACK",
                "X-LIC-ERROR",
            ],
            "apple-reminders.ics" =>
            [
                "X-WR-ALARMUID",
                "X-APPLE-PROXIMITY",
                "X-APPLE-STRUCTURED-LOCATION",
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(fixtureName)),
        };
    }

    private static string ExtractRawProperty(string content, string propertyName)
    {
        int start = content.IndexOf(propertyName, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Property {propertyName} was not found in the fixture.");
        int position = start;
        while (position < content.Length)
        {
            int lineFeed = content.IndexOf('\n', position);
            if (lineFeed < 0)
            {
                return content[start..];
            }

            position = lineFeed + 1;
            if (position >= content.Length || content[position] is not ' ' and not '\t')
            {
                return content[start..position];
            }
        }

        return content[start..];
    }

    private static IReadOnlyDictionary<string, string[]> CanonicalizeVTodoProperties(
        byte[] content)
    {
        var result = new Dictionary<string, string[]>(StringComparer.Ordinal);
        List<string>? properties = null;
        int alarmDepth = 0;
        foreach (string line in Unfold(content))
        {
            if (line.Equals("BEGIN:VTODO", StringComparison.OrdinalIgnoreCase))
            {
                properties = [];
                continue;
            }

            if (properties is null)
            {
                continue;
            }

            if (line.Equals("END:VTODO", StringComparison.OrdinalIgnoreCase))
            {
                string uidProperty = Assert.Single(
                    properties,
                    property => property.StartsWith("UID:", StringComparison.Ordinal));
                string uid = uidProperty[4..];
                result.Add(uid, properties.Order(StringComparer.Ordinal).ToArray());
                properties = null;
                continue;
            }

            if (line.Equals("BEGIN:VALARM", StringComparison.OrdinalIgnoreCase))
            {
                alarmDepth++;
                properties.Add("VALARM:BEGIN");
                continue;
            }

            if (line.Equals("END:VALARM", StringComparison.OrdinalIgnoreCase))
            {
                properties.Add("VALARM:END");
                alarmDepth--;
                continue;
            }

            string canonical = CanonicalizeContentLine(line);
            if (!canonical.StartsWith("DTSTAMP:", StringComparison.Ordinal))
            {
                properties.Add(alarmDepth > 0 ? "VALARM:" + canonical : canonical);
            }
        }

        return result;
    }

    private static IReadOnlyList<string> Unfold(byte[] content)
    {
        string normalized = Encoding.UTF8.GetString(content)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var lines = new List<string>();
        foreach (string physicalLine in normalized.Split('\n'))
        {
            if (physicalLine.Length > 0
                && physicalLine[0] is ' ' or '\t'
                && lines.Count > 0)
            {
                lines[^1] += physicalLine[1..];
            }
            else if (physicalLine.Length > 0)
            {
                lines.Add(physicalLine);
            }
        }

        return lines;
    }

    private static string CanonicalizeContentLine(string line)
    {
        int colon = FindUnquoted(line, ':');
        Assert.True(colon > 0, $"Invalid content line: {line}");
        string declaration = line[..colon];
        string value = line[(colon + 1)..];
        IReadOnlyList<string> parts = SplitUnquoted(declaration, ';');
        string name = parts[0].ToUpperInvariant();
        string[] parameters = parts
            .Skip(1)
            .Select(parameter =>
            {
                int equals = FindUnquoted(parameter, '=');
                Assert.True(equals > 0, $"Invalid property parameter: {parameter}");
                string parameterName = parameter[..equals].ToUpperInvariant();
                string parameterValue = parameter[(equals + 1)..];
                if (parameterValue.Length >= 2
                    && parameterValue[0] == '"'
                    && parameterValue[^1] == '"')
                {
                    parameterValue = parameterValue[1..^1];
                }

                return parameterName + "=" + parameterValue;
            })
            .Order(StringComparer.Ordinal)
            .ToArray();
        string canonicalDeclaration = parameters.Length == 0
            ? name
            : name + ";" + string.Join(';', parameters);
        return canonicalDeclaration + ":" + value;
    }

    private static IReadOnlyList<string> SplitUnquoted(string value, char delimiter)
    {
        var result = new List<string>();
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
                result.Add(value[start..index]);
                start = index + 1;
            }
        }

        result.Add(value[start..]);
        return result;
    }

    private static int FindUnquoted(string value, char delimiter)
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

    private static void AssertTaskEquivalent(TodoTask expected, TodoTask actual)
    {
        Assert.Equal(expected.Uid, actual.Uid);
        Assert.Equal(expected.ParentUid, actual.ParentUid);
        Assert.Equal(expected.Summary, actual.Summary);
        Assert.Equal(expected.Description, actual.Description);
        Assert.Equal(expected.Status, actual.Status);
        Assert.Equal(expected.PercentComplete, actual.PercentComplete);
        Assert.Equal(expected.Priority, actual.Priority);
        Assert.Equal(expected.Due, actual.Due);
        Assert.Equal(expected.DueIsDateOnly, actual.DueIsDateOnly);
        Assert.Equal(expected.DueTzid, actual.DueTzid);
        Assert.Equal(expected.Start, actual.Start);
        Assert.Equal(expected.StartIsDateOnly, actual.StartIsDateOnly);
        Assert.Equal(expected.CompletedAt, actual.CompletedAt);
        Assert.Equal(expected.Created, actual.Created);
        Assert.Equal(expected.LastModified, actual.LastModified);
        Assert.Equal(expected.Sequence, actual.Sequence);
        Assert.Equal(expected.RRule, actual.RRule);
        Assert.Equal(expected.SortOrder, actual.SortOrder);
        Assert.Equal(expected.Location, actual.Location);
        Assert.Equal(expected.Url, actual.Url);
        Assert.Equal(expected.Class, actual.Class);
        Assert.Equal(expected.Categories, actual.Categories);
        Assert.Equal(expected.Alarms.Count, actual.Alarms.Count);
        for (int index = 0; index < expected.Alarms.Count; index++)
        {
            TaskAlarm expectedAlarm = expected.Alarms[index];
            TaskAlarm actualAlarm = actual.Alarms[index];
            Assert.Equal(expectedAlarm.Uid, actualAlarm.Uid);
            Assert.Equal(expectedAlarm.Action, actualAlarm.Action);
            Assert.Equal(expectedAlarm.Trigger, actualAlarm.Trigger);
            Assert.Equal(expectedAlarm.TriggerOffsetSeconds, actualAlarm.TriggerOffsetSeconds);
            Assert.Equal(expectedAlarm.TriggerRelated, actualAlarm.TriggerRelated);
            Assert.Equal(expectedAlarm.Description, actualAlarm.Description);
        }
    }
}
