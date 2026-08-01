using System.Globalization;
using Microsoft.Data.Sqlite;
using Wintodo.Core.Models;
using Wintodo.Data.Mapping;

namespace Wintodo.Data;

public sealed partial class SqliteTaskStore
{
    public async Task<IReadOnlyList<TaskAlarm>> GetPendingAlarmsAsync(
        DateTimeOffset throughUtc,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PendingReminder> reminders =
            await GetPendingRemindersAsync(throughUtc, cancellationToken);
        return reminders.Select(reminder => reminder.Alarm).ToArray();
    }

    public Task<IReadOnlyList<PendingReminder>> GetPendingRemindersAsync(
        DateTimeOffset throughUtc,
        CancellationToken cancellationToken = default)
    {
        return ExecuteReadAsync<IReadOnlyList<PendingReminder>>(
            connection =>
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = """
                    SELECT a.id, a.task_id, a.uid, a.action, a.trigger_utc,
                           a.trigger_offset_seconds, a.trigger_related, a.description,
                           a.snoozed_until_utc, a.is_acknowledged, a.scheduled_toast_tag,
                           t.start_utc, t.due_utc, t.uid, t.summary
                    FROM alarms a
                    JOIN tasks t ON t.id = a.task_id
                    JOIN lists l ON l.id = t.list_id
                    WHERE a.is_acknowledged = 0
                      AND t.is_deleted = 0
                      AND t.status IN (0, 1)
                      AND l.is_deleted = 0;
                    """;
                using SqliteDataReader reader = command.ExecuteReader();
                var pending = new List<PendingReminder>();
                while (reader.Read())
                {
                    TaskAlarm alarm = SqliteMapper.ReadAlarm(reader);
                    DateTimeOffset? related = alarm.TriggerRelated == AlarmRelation.Start
                        ? ReadNullableInstant(reader, 11)
                        : ReadNullableInstant(reader, 12);
                    DateTimeOffset? effectiveTrigger = alarm.SnoozedUntil
                        ?? alarm.Trigger
                        ?? (related is null || alarm.TriggerOffsetSeconds is null
                            ? null
                            : related.Value.AddSeconds(alarm.TriggerOffsetSeconds.Value));

                    if (effectiveTrigger is not null && effectiveTrigger.Value <= throughUtc)
                    {
                        pending.Add(new PendingReminder
                        {
                            TaskUid = reader.GetString(13),
                            TaskSummary = reader.GetString(14),
                            Alarm = alarm,
                            EffectiveTrigger = effectiveTrigger.Value,
                        });
                    }
                }

                return pending
                    .OrderBy(reminder => reminder.EffectiveTrigger)
                    .ThenBy(reminder => reminder.Alarm.Id)
                    .ToArray();
            },
            cancellationToken);
    }

    public Task<TaskAlarm> SetAlarmAsync(
        string taskUid,
        TaskAlarm alarm,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskUid);
        ValidateAlarm(alarm);
        return ExecuteWriteAsync(
            (connection, transaction, context) =>
            {
                TaskIdentity identity = GetTaskIdentity(connection, transaction, taskUid);
                alarm.TaskId = identity.Id;
                alarm.Uid = string.IsNullOrWhiteSpace(alarm.Uid)
                    ? Guid.NewGuid().ToString("D")
                    : alarm.Uid;

                using SqliteCommand find = connection.CreateCommand();
                find.Transaction = transaction;
                find.CommandText = "SELECT id FROM alarms WHERE task_id = @taskId AND uid = @uid;";
                find.Parameters.AddWithValue("@taskId", identity.Id);
                find.Parameters.AddWithValue("@uid", alarm.Uid);
                object? existingId = find.ExecuteScalar();

                if (existingId is null)
                {
                    alarm.Id = InsertAlarm(connection, transaction, alarm);
                }
                else
                {
                    alarm.Id = Convert.ToInt64(existingId, CultureInfo.InvariantCulture);
                    UpdateAlarm(connection, transaction, alarm);
                }

                TouchTask(connection, transaction, identity.Id);
                LogChange(connection, transaction, TaskEntityType, taskUid, ChangeOperation.Update);
                context.AddTask(identity.ListId, taskUid);
                return alarm;
            },
            cancellationToken);
    }

    public Task SnoozeAlarmAsync(
        string alarmUid,
        DateTimeOffset untilUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alarmUid);
        return UpdateAlarmStateAsync(
            alarmUid,
            """
            snoozed_until_utc = @snoozedUntilUtc,
            is_acknowledged = 0,
            scheduled_toast_tag = NULL
            """,
            command => command.Parameters.AddWithValue(
                "@snoozedUntilUtc",
                SqliteMapper.FormatInstant(untilUtc)),
            cancellationToken);
    }

    public Task AcknowledgeAlarmAsync(
        string alarmUid,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alarmUid);
        return UpdateAlarmStateAsync(
            alarmUid,
            """
            is_acknowledged = 1,
            snoozed_until_utc = NULL,
            scheduled_toast_tag = NULL
            """,
            configure: null,
            cancellationToken);
    }

    public Task SetAlarmScheduledToastTagAsync(
        string alarmUid,
        string? scheduledToastTag,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alarmUid);
        return ExecuteWriteAsync(
            (connection, transaction, _) =>
            {
                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    UPDATE alarms
                    SET scheduled_toast_tag = @scheduledToastTag
                    WHERE uid = @uid;
                    """;
                AddParameter(command, "@scheduledToastTag", scheduledToastTag);
                command.Parameters.AddWithValue("@uid", alarmUid);
                if (command.ExecuteNonQuery() == 0)
                {
                    throw new KeyNotFoundException($"Alarm '{alarmUid}' was not found.");
                }
            },
            cancellationToken);
    }

    private Task UpdateAlarmStateAsync(
        string alarmUid,
        string assignment,
        Action<SqliteCommand>? configure,
        CancellationToken cancellationToken)
    {
        return ExecuteWriteAsync(
            (connection, transaction, context) =>
            {
                using SqliteCommand identityCommand = connection.CreateCommand();
                identityCommand.Transaction = transaction;
                identityCommand.CommandText = """
                    SELECT a.id, t.id, t.uid, t.list_id
                    FROM alarms a
                    JOIN tasks t ON t.id = a.task_id
                    WHERE a.uid = @uid AND t.is_deleted = 0;
                    """;
                identityCommand.Parameters.AddWithValue("@uid", alarmUid);
                using SqliteDataReader reader = identityCommand.ExecuteReader();
                if (!reader.Read())
                {
                    throw new KeyNotFoundException($"Alarm '{alarmUid}' was not found.");
                }

                long alarmId = reader.GetInt64(0);
                long taskId = reader.GetInt64(1);
                string taskUid = reader.GetString(2);
                long listId = reader.GetInt64(3);
                reader.Close();

                using SqliteCommand update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = $"UPDATE alarms SET {assignment} WHERE id = @id;";
                update.Parameters.AddWithValue("@id", alarmId);
                configure?.Invoke(update);
                update.ExecuteNonQuery();

                TouchTask(connection, transaction, taskId);
                LogChange(connection, transaction, TaskEntityType, taskUid, ChangeOperation.Update);
                context.AddTask(listId, taskUid);
            },
            cancellationToken);
    }

    private static void UpdateAlarm(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TaskAlarm alarm)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE alarms
            SET action = @action, trigger_utc = @triggerUtc,
                trigger_offset_seconds = @triggerOffsetSeconds,
                trigger_related = @triggerRelated, description = @description,
                snoozed_until_utc = @snoozedUntilUtc,
                is_acknowledged = @isAcknowledged,
                scheduled_toast_tag = @scheduledToastTag
            WHERE id = @id;
            """;
        command.Parameters.AddWithValue("@action", (int)alarm.Action);
        AddParameter(command, "@triggerUtc", SqliteMapper.FormatInstant(alarm.Trigger));
        AddParameter(command, "@triggerOffsetSeconds", alarm.TriggerOffsetSeconds);
        command.Parameters.AddWithValue("@triggerRelated", (int)alarm.TriggerRelated);
        AddParameter(command, "@description", alarm.Description);
        AddParameter(command, "@snoozedUntilUtc", SqliteMapper.FormatInstant(alarm.SnoozedUntil));
        command.Parameters.AddWithValue("@isAcknowledged", alarm.IsAcknowledged);
        AddParameter(command, "@scheduledToastTag", alarm.ScheduledToastTag);
        command.Parameters.AddWithValue("@id", alarm.Id);
        command.ExecuteNonQuery();
    }

    private void TouchTask(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long taskId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE tasks
            SET modified_utc = @modifiedUtc, sequence = sequence + 1, is_dirty = 1
            WHERE id = @id;
            """;
        command.Parameters.AddWithValue("@modifiedUtc", SqliteMapper.FormatInstant(_clock.UtcNow));
        command.Parameters.AddWithValue("@id", taskId);
        command.ExecuteNonQuery();
    }

    private static DateTimeOffset? ReadNullableInstant(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal)
            ? null
            : DateTimeOffset.Parse(
                reader.GetString(ordinal),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }
}
