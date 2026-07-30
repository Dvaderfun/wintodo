using Microsoft.Data.Sqlite;
using Wintodo.Core.Models;

namespace Wintodo.Data.Tests;

public sealed class M3PersistenceGateTests
{
    [Fact]
    public async Task RequestedUiGateDatabaseHasExactPersistedState()
    {
        string? requestedPath =
            Environment.GetEnvironmentVariable("WINTODO_M3_GATE_DB_PATH");
        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            return;
        }

        await using var connection = new SqliteConnection(
            $"Data Source={Path.GetFullPath(requestedPath)};Mode=ReadOnly");
        await connection.OpenAsync();
        await using (SqliteCommand listCommand = connection.CreateCommand())
        {
            listCommand.CommandText = """
                SELECT color
                FROM lists
                WHERE display_name = 'M3 Gate' AND is_deleted = 0;
                """;
            Assert.Equal(
                (int)ListColor.Red,
                Convert.ToInt32(await listCommand.ExecuteScalarAsync()));
        }

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT t.summary, t.status
            FROM tasks t
            JOIN lists l ON l.id = t.list_id
            WHERE l.display_name = 'M3 Gate'
              AND l.is_deleted = 0
              AND t.is_deleted = 0
              AND t.parent_uid IS NULL
            ORDER BY t.sort_order, t.id;
            """;
        var active = new List<string>();
        var completed = new List<string>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            string summary = reader.GetString(0);
            if (reader.GetInt32(1) == (int)Wintodo.Core.Models.TaskStatus.Completed)
            {
                completed.Add(summary);
            }
            else
            {
                active.Add(summary);
            }
        }

        Assert.Equal(["Gate task 5", "Gate task 3", "Gate task 4"], active);
        Assert.Equal(
            ["Gate task 1", "Gate task 2"],
            completed.Order(StringComparer.Ordinal));
    }
}
