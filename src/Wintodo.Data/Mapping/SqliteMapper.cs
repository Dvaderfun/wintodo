using System.Globalization;
using Microsoft.Data.Sqlite;
using Wintodo.Core.Models;

namespace Wintodo.Data.Mapping;

internal static class SqliteMapper
{
    internal const string TaskColumns = """
        t.id, t.uid, t.list_id, t.parent_uid, t.summary, t.description,
        t.status, t.percent_complete, t.priority,
        t.due_utc, t.due_is_date_only, t.due_tzid,
        t.start_utc, t.start_is_date_only, t.completed_utc,
        t.created_utc, t.modified_utc, t.sequence,
        t.rrule, t.my_day_date, t.sort_order,
        t.location, t.url, t.classification, t.ical_unknown,
        t.remote_href, t.remote_etag, t.is_dirty, t.is_deleted
        """;

    internal static TodoTask ReadTask(SqliteDataReader reader)
    {
        bool dueIsDateOnly = reader.GetBoolean(10);
        bool startIsDateOnly = reader.GetBoolean(13);

        return new TodoTask
        {
            Id = reader.GetInt64(0),
            Uid = reader.GetString(1),
            ListId = reader.GetInt64(2),
            ParentUid = GetNullableString(reader, 3),
            Summary = reader.GetString(4),
            Description = GetNullableString(reader, 5),
            Status = (Wintodo.Core.Models.TaskStatus)reader.GetInt32(6),
            PercentComplete = GetNullableInt32(reader, 7),
            Priority = reader.GetInt32(8),
            Due = ReadStoredDate(reader, 9, dueIsDateOnly),
            DueIsDateOnly = dueIsDateOnly,
            DueTzid = GetNullableString(reader, 11),
            Start = ReadStoredDate(reader, 12, startIsDateOnly),
            StartIsDateOnly = startIsDateOnly,
            CompletedAt = ReadInstant(reader, 14),
            Created = ReadRequiredInstant(reader, 15),
            LastModified = ReadRequiredInstant(reader, 16),
            Sequence = reader.GetInt32(17),
            RRule = GetNullableString(reader, 18),
            MyDayDate = ReadDateOnly(reader, 19),
            SortOrder = reader.GetDouble(20),
            Location = GetNullableString(reader, 21),
            Url = GetNullableString(reader, 22),
            Class = (Classification)reader.GetInt32(23),
            IcalUnknown = GetNullableString(reader, 24),
            RemoteHref = GetNullableString(reader, 25),
            RemoteETag = GetNullableString(reader, 26),
            IsDirty = reader.GetBoolean(27),
            IsDeleted = reader.GetBoolean(28),
        };
    }

    internal static TodoList ReadList(SqliteDataReader reader)
    {
        return new TodoList
        {
            Id = reader.GetInt64(0),
            Uid = reader.GetString(1),
            DisplayName = reader.GetString(2),
            Color = (ListColor)reader.GetInt32(3),
            BackgroundTheme = GetNullableString(reader, 4),
            GroupId = GetNullableInt64(reader, 5),
            SortOrder = reader.GetDouble(6),
            SortMode = (SortMode)reader.GetInt32(7),
            SortDescending = reader.GetBoolean(8),
            ShowCompleted = reader.GetBoolean(9),
            CompletedExpanded = reader.GetBoolean(10),
            IsDeleted = reader.GetBoolean(11),
            Created = ReadRequiredInstant(reader, 12),
            Modified = ReadRequiredInstant(reader, 13),
            AccountId = GetNullableInt64(reader, 14),
            RemoteUrl = GetNullableString(reader, 15),
            RemoteCTag = GetNullableString(reader, 16),
            SyncToken = GetNullableString(reader, 17),
        };
    }

    internal static ListGroup ReadGroup(SqliteDataReader reader)
    {
        return new ListGroup
        {
            Id = reader.GetInt64(0),
            Name = reader.GetString(1),
            SortOrder = reader.GetDouble(2),
            IsExpanded = reader.GetBoolean(3),
            Created = ReadRequiredInstant(reader, 4),
        };
    }

    internal static TaskAlarm ReadAlarm(SqliteDataReader reader)
    {
        return new TaskAlarm
        {
            Id = reader.GetInt64(0),
            TaskId = reader.GetInt64(1),
            Uid = reader.GetString(2),
            Action = (AlarmAction)reader.GetInt32(3),
            Trigger = ReadInstant(reader, 4),
            TriggerOffsetSeconds = GetNullableInt64(reader, 5),
            TriggerRelated = (AlarmRelation)reader.GetInt32(6),
            Description = GetNullableString(reader, 7),
            SnoozedUntil = ReadInstant(reader, 8),
            IsAcknowledged = reader.GetBoolean(9),
            ScheduledToastTag = GetNullableString(reader, 10),
        };
    }

    internal static TaskAttachment ReadAttachment(SqliteDataReader reader)
    {
        return new TaskAttachment
        {
            Id = reader.GetInt64(0),
            TaskId = reader.GetInt64(1),
            FileName = reader.GetString(2),
            RelativePath = reader.GetString(3),
            SizeBytes = reader.GetInt64(4),
            MimeType = GetNullableString(reader, 5),
            Added = ReadRequiredInstant(reader, 6),
        };
    }

    internal static string FormatInstant(DateTimeOffset value)
    {
        return value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
    }

    internal static string? FormatInstant(DateTimeOffset? value)
    {
        return value is null ? null : FormatInstant(value.Value);
    }

    internal static string? FormatStoredDate(DateTimeOffset? value, bool dateOnly)
    {
        if (value is null)
        {
            return null;
        }

        return dateOnly
            ? value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : FormatInstant(value.Value);
    }

    internal static string? FormatDateOnly(DateOnly? value)
    {
        return value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static string? GetNullableString(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static int? GetNullableInt32(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    private static long? GetNullableInt64(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
    }

    private static DateTimeOffset? ReadStoredDate(SqliteDataReader reader, int ordinal, bool dateOnly)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        string value = reader.GetString(ordinal);
        if (dateOnly)
        {
            DateOnly date = DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            return new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        }

        return ParseInstant(value);
    }

    private static DateTimeOffset? ReadInstant(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : ParseInstant(reader.GetString(ordinal));
    }

    private static DateTimeOffset ReadRequiredInstant(SqliteDataReader reader, int ordinal)
    {
        return ParseInstant(reader.GetString(ordinal));
    }

    private static DateOnly? ReadDateOnly(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal)
            ? null
            : DateOnly.ParseExact(reader.GetString(ordinal), "yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset ParseInstant(string value)
    {
        return DateTimeOffset.ParseExact(
            value,
            "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }
}
