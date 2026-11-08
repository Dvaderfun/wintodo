using Microsoft.Data.Sqlite;
using Wintodo.Core.Models;
using Wintodo.Data.Mapping;

namespace Wintodo.Data;

public sealed partial class SqliteTaskStore
{
    private const string MoveColumns = """
        id, task_uid, group_id, predecessor_id, version,
        source_account_id, source_list_id, source_list_uid, source_href, source_etag,
        destination_account_id, destination_list_id, destination_list_uid, destination_href,
        local_sequence, change_id, phase, attempted_payload, attempted_sequence,
        attempted_change_id, destination_etag, resolution_choice, resolution_sequence,
        resolution_source_payload, resolution_source_etag, resolution_copy_uid,
        resolution_copy_href, resolution_copy_payload, resolution_copy_etag,
        resolution_copy_confirmed, resolution_destination_removed,
        resolution_is_destination_conflict, created_utc, updated_utc, source_payload, destination_removed, resolution_list_id,
        source_restored, restored_source_etag, source_binding_verified
        """;

    private static TaskMoveIntent ReadMove(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0), TaskUid = reader.GetString(1), GroupId = reader.GetString(2),
        PredecessorId = reader.IsDBNull(3) ? null : reader.GetInt64(3), Version = reader.GetInt32(4),
        SourceAccountId = reader.IsDBNull(5) ? null : reader.GetInt64(5), SourceListId = reader.GetInt64(6),
        SourceListUid = reader.GetString(7), SourceHref = reader.IsDBNull(8) ? null : reader.GetString(8),
        SourceETag = reader.IsDBNull(9) ? null : reader.GetString(9),
        DestinationAccountId = reader.IsDBNull(10) ? null : reader.GetInt64(10), DestinationListId = reader.GetInt64(11),
        DestinationListUid = reader.GetString(12), DestinationHref = reader.IsDBNull(13) ? null : reader.GetString(13),
        LocalSequence = reader.GetInt32(14), ChangeId = reader.GetInt64(15), Phase = (TaskMovePhase)reader.GetInt32(16),
        AttemptedPayload = reader.IsDBNull(17) ? null : reader.GetFieldValue<byte[]>(17),
        AttemptedSequence = reader.IsDBNull(18) ? null : reader.GetInt32(18),
        AttemptedChangeId = reader.IsDBNull(19) ? null : reader.GetInt64(19),
        DestinationETag = reader.IsDBNull(20) ? null : reader.GetString(20),
        ResolutionChoice = reader.IsDBNull(21) ? null : (SyncConflictChoice)reader.GetInt32(21),
        ResolutionSequence = reader.IsDBNull(22) ? null : reader.GetInt32(22),
        ResolutionSourcePayload = reader.IsDBNull(23) ? null : reader.GetFieldValue<byte[]>(23),
        ResolutionSourceETag = reader.IsDBNull(24) ? null : reader.GetString(24),
        ResolutionCopyUid = reader.IsDBNull(25) ? null : reader.GetString(25),
        ResolutionCopyHref = reader.IsDBNull(26) ? null : reader.GetString(26),
        ResolutionCopyPayload = reader.IsDBNull(27) ? null : reader.GetFieldValue<byte[]>(27),
        ResolutionCopyETag = reader.IsDBNull(28) ? null : reader.GetString(28),
        ResolutionCopyConfirmed = reader.GetBoolean(29), ResolutionDestinationRemoved = reader.GetBoolean(30),
        ResolutionIsDestinationConflict = reader.GetBoolean(31),
        Created = ParseSyncInstant(reader.GetString(32)), Updated = ParseSyncInstant(reader.GetString(33)),
        SourcePayload = reader.IsDBNull(34) ? null : reader.GetFieldValue<byte[]>(34),
        DestinationRemoved = reader.GetBoolean(35),
        ResolutionListId = reader.IsDBNull(36) ? null : reader.GetInt64(36),
        SourceRestored = reader.GetBoolean(37),
        RestoredSourceETag = reader.IsDBNull(38) ? null : reader.GetString(38),
        SourceBindingVerified = reader.GetBoolean(39),
    };

    private static TaskMoveIntent? ReadMoveById(SqliteConnection connection, SqliteTransaction? transaction, long id)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT {MoveColumns} FROM task_move_outbox WHERE id = @id;";
        command.Parameters.AddWithValue("@id", id);
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? ReadMove(reader) : null;
    }

    private static TaskMoveIntent? ReadLastMove(SqliteConnection connection, SqliteTransaction transaction, string uid)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT {MoveColumns} FROM task_move_outbox WHERE task_uid = @uid ORDER BY id DESC LIMIT 1;";
        command.Parameters.AddWithValue("@uid", uid);
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? ReadMove(reader) : null;
    }

    private static IReadOnlyList<TaskMoveIntent> ReadMoveGroup(SqliteConnection connection,
        SqliteTransaction transaction, string groupId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT {MoveColumns} FROM task_move_outbox WHERE group_id = @group ORDER BY id;";
        command.Parameters.AddWithValue("@group", groupId);
        using SqliteDataReader reader = command.ExecuteReader();
        var moves = new List<TaskMoveIntent>();
        while (reader.Read()) moves.Add(ReadMove(reader));
        return moves;
    }

    private static TaskMoveIntent? ReadNextMove(SqliteConnection connection, SqliteTransaction transaction, string uid, long id)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT {MoveColumns} FROM task_move_outbox WHERE task_uid = @uid AND id > @id ORDER BY id LIMIT 1;";
        command.Parameters.AddWithValue("@uid", uid);
        command.Parameters.AddWithValue("@id", id);
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? ReadMove(reader) : null;
    }

    private static IReadOnlyList<TaskMoveIntent> ReadSuccessorMoves(SqliteConnection connection,
        SqliteTransaction transaction, string uid, long id)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT {MoveColumns} FROM task_move_outbox WHERE task_uid = @uid AND id > @id ORDER BY id;";
        command.Parameters.AddWithValue("@uid", uid);
        command.Parameters.AddWithValue("@id", id);
        using SqliteDataReader reader = command.ExecuteReader();
        var moves = new List<TaskMoveIntent>();
        while (reader.Read()) moves.Add(ReadMove(reader));
        return moves;
    }

    private void SaveMove(SqliteConnection connection, SqliteTransaction transaction, TaskMoveIntent move)
    {
        bool insert = move.Id == 0;
        if (!insert) move.Version++;
        move.Updated = _clock.UtcNow;
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = insert ? """
            INSERT INTO task_move_outbox(task_uid, group_id, predecessor_id, version,
                source_account_id, source_list_id, source_list_uid, source_href, source_etag,
                destination_account_id, destination_list_id, destination_list_uid, destination_href,
                local_sequence, change_id, phase, attempted_payload, attempted_sequence, attempted_change_id,
                destination_etag, resolution_choice, resolution_sequence, resolution_source_payload,
                resolution_source_etag, resolution_copy_uid, resolution_copy_href, resolution_copy_payload,
                resolution_copy_etag, resolution_copy_confirmed, resolution_destination_removed,
                resolution_is_destination_conflict, created_utc, updated_utc, source_payload, destination_removed, resolution_list_id,
                source_restored, restored_source_etag, source_binding_verified)
            VALUES(@uid, @group, @predecessor, @version, @sourceAccount, @sourceList, @sourceUid,
                @sourceHref, @sourceEtag, @destinationAccount, @destinationList, @destinationUid,
                @destinationHref, @sequence, @change, @phase, @payload, @attemptSequence, @attemptChange,
                @destinationEtag, @choice, @resolutionSequence, @sourcePayload, @resolutionEtag,
                @copyUid, @copyHref, @copyPayload, @copyEtag, @copyConfirmed, @destinationRemoved,
                @destinationConflict, @created, @updated, @originalSourcePayload, @removed, @resolutionList,
                @sourceRestored, @restoredSourceEtag, @verifiedSource) RETURNING id;
            """ : """
            UPDATE task_move_outbox SET task_uid = @uid, group_id = @group, predecessor_id = @predecessor,
                version = @version, source_account_id = @sourceAccount, source_list_id = @sourceList,
                source_list_uid = @sourceUid, source_href = @sourceHref, source_etag = @sourceEtag,
                destination_account_id = @destinationAccount, destination_list_id = @destinationList,
                destination_list_uid = @destinationUid, destination_href = @destinationHref,
                local_sequence = @sequence, change_id = @change, phase = @phase, attempted_payload = @payload,
                attempted_sequence = @attemptSequence, attempted_change_id = @attemptChange,
                destination_etag = @destinationEtag, resolution_choice = @choice,
                resolution_sequence = @resolutionSequence, resolution_source_payload = @sourcePayload,
                resolution_source_etag = @resolutionEtag, resolution_copy_uid = @copyUid,
                resolution_copy_href = @copyHref, resolution_copy_payload = @copyPayload,
                resolution_copy_etag = @copyEtag, resolution_copy_confirmed = @copyConfirmed,
                resolution_destination_removed = @destinationRemoved,
                resolution_is_destination_conflict = @destinationConflict,
                source_payload = @originalSourcePayload, destination_removed = @removed,
                resolution_list_id = @resolutionList,
                source_restored = @sourceRestored, restored_source_etag = @restoredSourceEtag,
                source_binding_verified = @verifiedSource,
                created_utc = @created, updated_utc = @updated WHERE id = @id RETURNING id;
            """;
        AddParameter(command, "@id", move.Id);
        AddParameter(command, "@uid", move.TaskUid);
        AddParameter(command, "@group", move.GroupId);
        AddParameter(command, "@predecessor", move.PredecessorId);
        AddParameter(command, "@version", move.Version);
        AddParameter(command, "@sourceAccount", move.SourceAccountId);
        AddParameter(command, "@sourceList", move.SourceListId);
        AddParameter(command, "@sourceUid", move.SourceListUid);
        AddParameter(command, "@sourceHref", move.SourceHref);
        AddParameter(command, "@sourceEtag", move.SourceETag);
        AddParameter(command, "@destinationAccount", move.DestinationAccountId);
        AddParameter(command, "@destinationList", move.DestinationListId);
        AddParameter(command, "@destinationUid", move.DestinationListUid);
        AddParameter(command, "@destinationHref", move.DestinationHref);
        AddParameter(command, "@sequence", move.LocalSequence);
        AddParameter(command, "@change", move.ChangeId);
        AddParameter(command, "@phase", (int)move.Phase);
        AddParameter(command, "@payload", move.AttemptedPayload);
        AddParameter(command, "@attemptSequence", move.AttemptedSequence);
        AddParameter(command, "@attemptChange", move.AttemptedChangeId);
        AddParameter(command, "@destinationEtag", move.DestinationETag);
        AddParameter(command, "@choice", move.ResolutionChoice is null ? null : (int)move.ResolutionChoice.Value);
        AddParameter(command, "@resolutionSequence", move.ResolutionSequence);
        AddParameter(command, "@sourcePayload", move.ResolutionSourcePayload);
        AddParameter(command, "@resolutionEtag", move.ResolutionSourceETag);
        AddParameter(command, "@copyUid", move.ResolutionCopyUid);
        AddParameter(command, "@copyHref", move.ResolutionCopyHref);
        AddParameter(command, "@copyPayload", move.ResolutionCopyPayload);
        AddParameter(command, "@copyEtag", move.ResolutionCopyETag);
        AddParameter(command, "@copyConfirmed", move.ResolutionCopyConfirmed);
        AddParameter(command, "@destinationRemoved", move.ResolutionDestinationRemoved);
        AddParameter(command, "@destinationConflict", move.ResolutionIsDestinationConflict);
        AddParameter(command, "@originalSourcePayload", move.SourcePayload);
        AddParameter(command, "@removed", move.DestinationRemoved);
        AddParameter(command, "@resolutionList", move.ResolutionListId);
        AddParameter(command, "@sourceRestored", move.SourceRestored);
        AddParameter(command, "@restoredSourceEtag", move.RestoredSourceETag);
        AddParameter(command, "@verifiedSource", move.SourceBindingVerified);
        AddParameter(command, "@created", SqliteMapper.FormatInstant(move.Created));
        AddParameter(command, "@updated", SqliteMapper.FormatInstant(move.Updated));
        move.Id = (long)(command.ExecuteScalar() ?? throw new InvalidOperationException("Move intent was not saved."));
    }

    private static void DeleteMoveRow(SqliteConnection connection, SqliteTransaction transaction, long id)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM task_move_outbox WHERE id = @id;";
        command.Parameters.AddWithValue("@id", id);
        command.ExecuteNonQuery();
    }

    private static bool SameMoveIdentity(TaskMoveIntent a, TaskMoveIntent b)
        => a.TaskUid == b.TaskUid && a.GroupId == b.GroupId && a.PredecessorId == b.PredecessorId
        && a.SourceAccountId == b.SourceAccountId && a.SourceListId == b.SourceListId
        && a.SourceBindingVerified == b.SourceBindingVerified
        && a.SourceListUid == b.SourceListUid && a.SourceHref == b.SourceHref && a.SourceETag == b.SourceETag
        && a.DestinationAccountId == b.DestinationAccountId && a.DestinationListId == b.DestinationListId
        && a.DestinationListUid == b.DestinationListUid && a.LocalSequence == b.LocalSequence
        && a.ChangeId == b.ChangeId
        && (a.SourcePayload is null || SameBytes(a.SourcePayload, b.SourcePayload));

    private static bool SameBytes(byte[]? a, byte[]? b)
        => a is null ? b is null : b is not null && a.AsSpan().SequenceEqual(b);

    private static bool SameAttempt(TaskMoveIntent a, TaskMoveIntent b)
        => SameBytes(a.AttemptedPayload, b.AttemptedPayload) && a.AttemptedSequence == b.AttemptedSequence
        && a.AttemptedChangeId == b.AttemptedChangeId && a.DestinationHref == b.DestinationHref;

    private static bool SameResolution(TaskMoveIntent a, TaskMoveIntent b)
        => a.ResolutionChoice is null || (a.ResolutionChoice == b.ResolutionChoice
        && a.ResolutionSequence == b.ResolutionSequence
        && a.ResolutionListId == b.ResolutionListId
        && SameBytes(a.ResolutionSourcePayload, b.ResolutionSourcePayload)
        && a.ResolutionSourceETag == b.ResolutionSourceETag
        && a.ResolutionIsDestinationConflict == b.ResolutionIsDestinationConflict
        && (a.ResolutionCopyUid is null || a.ResolutionCopyUid == b.ResolutionCopyUid)
        && (a.ResolutionCopyHref is null || a.ResolutionCopyHref == b.ResolutionCopyHref)
        && (a.ResolutionCopyPayload is null || SameBytes(a.ResolutionCopyPayload, b.ResolutionCopyPayload)));
}
