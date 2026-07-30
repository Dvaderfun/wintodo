using Microsoft.Data.Sqlite;
using Wintodo.Core.Models;
using Wintodo.Core.Services;
using Wintodo.Data;

namespace Wintodo.Data.Tests;

public sealed class MigrationAndListTests
{
    [Fact]
    public async Task Initialize_Twice_CreatesVersionOneSchemaAndIndexes()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();

        await fixture.Store.InitializeAsync();
        StoreStats stats = await fixture.Store.GetStatsAsync();

        Assert.Equal(1, stats.SchemaVersion);
        Assert.Equal(0, stats.ListCount);
        Assert.Equal(0, stats.TaskCount);
        Assert.True(stats.DatabaseSizeBytes > 0);

        await using var connection = new SqliteConnection($"Data Source={fixture.DatabasePath}");
        await connection.OpenAsync();
        Assert.Equal("wal", await ScalarStringAsync(connection, "PRAGMA journal_mode;"));
        Assert.Equal(
            13,
            await ScalarLongAsync(
                connection,
                """
                SELECT COUNT(*)
                FROM sqlite_master
                WHERE type = 'table'
                  AND name IN (
                    'schema_version', 'settings', 'list_groups', 'lists', 'tasks',
                    'tags', 'task_tags', 'alarms', 'attachments',
                    'completion_history', 'change_log', 'saved_views', 'accounts'
                  );
                """));
        Assert.Equal(
            11,
            await ScalarLongAsync(
                connection,
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name LIKE 'ix_%';"));
    }

    [Fact]
    public async Task ReopenExistingDatabase_DoesNotReapplyMigration()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        TodoList list = await fixture.Store.CreateListAsync("Inbox");
        await fixture.Store.DisposeAsync();

        await using var reopened = new SqliteTaskStore(fixture.DatabasePath, fixture.Clock);
        await reopened.InitializeAsync();

        Assert.Equal(list.Uid, Assert.Single(await reopened.GetListsAsync()).Uid);
        Assert.Equal(1, (await reopened.GetStatsAsync()).SchemaVersion);
    }

    [Fact]
    public async Task ListAndGroupLifecycle_PersistsAndCoalescesNotifications()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        var changes = new List<StoreChangedEventArgs>();
        fixture.Store.StoreChanged += (_, change) => changes.Add(change);

        ListGroup group = await fixture.Store.CreateGroupAsync("Work");
        TodoList list = await fixture.Store.CreateListAsync("Inbox", ListColor.Red, group.Id);
        await fixture.Store.RenameGroupAsync(group.Id, "Projects");
        await fixture.Store.RenameListAsync(list.Id, "Priority");
        await fixture.Store.SetListColorAsync(list.Id, ListColor.Green);
        await fixture.Store.SetListSortAsync(list.Id, SortMode.DueDate, descending: true);
        await fixture.Store.SetListShowCompletedAsync(list.Id, showCompleted: false);
        await fixture.Store.SetListCompletedExpandedAsync(list.Id, completedExpanded: true);
        await fixture.Store.ReorderListAsync(list.Id, 20);
        await fixture.Store.MoveListToGroupAsync(list.Id, group.Id);

        ListGroup storedGroup = Assert.Single(await fixture.Store.GetGroupsAsync());
        TodoList storedList = Assert.Single(await fixture.Store.GetListsAsync());
        Assert.Equal("Projects", storedGroup.Name);
        Assert.Equal("Priority", storedList.DisplayName);
        Assert.Equal(ListColor.Green, storedList.Color);
        Assert.Equal(SortMode.DueDate, storedList.SortMode);
        Assert.True(storedList.SortDescending);
        Assert.False(storedList.ShowCompleted);
        Assert.True(storedList.CompletedExpanded);
        Assert.Equal(20, storedList.SortOrder);
        Assert.Equal(group.Id, storedList.GroupId);
        Assert.Contains(changes, change => change.ListIds.Contains(list.Id));

        await fixture.Store.DeleteGroupAsync(group.Id);
        Assert.Empty(await fixture.Store.GetGroupsAsync());
        Assert.Null(Assert.Single(await fixture.Store.GetListsAsync()).GroupId);
    }

    [Fact]
    public async Task DeleteList_SoftDeletesItsTasksAndWritesChangeLog()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        TodoList list = await fixture.Store.CreateListAsync("Disposable");
        TodoTask task = await fixture.Store.CreateTaskAsync(new TodoTask
        {
            ListId = list.Id,
            Summary = "Remove me",
        });

        await fixture.Store.DeleteListAsync(list.Id);

        Assert.Empty(await fixture.Store.GetListsAsync());
        Assert.Null(await fixture.Store.GetTaskAsync(task.Uid));
        StoreStats stats = await fixture.Store.GetStatsAsync();
        Assert.Equal(0, stats.ListCount);
        Assert.Equal(0, stats.TaskCount);
        Assert.Equal(4, stats.PendingChangeCount);

        await using var connection = new SqliteConnection($"Data Source={fixture.DatabasePath}");
        await connection.OpenAsync();
        Assert.Equal(
            1,
            await ScalarLongAsync(
                connection,
                "SELECT is_deleted FROM tasks WHERE uid = @uid;",
                ("@uid", task.Uid)));
    }

    [Fact]
    public async Task InvalidListMutation_RollsBackWithoutNotification()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        int notifications = 0;
        fixture.Store.StoreChanged += (_, _) => notifications++;

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => fixture.Store.RenameListAsync(999, "Missing"));

        Assert.Equal(0, notifications);
    }

    [Fact]
    public async Task ConcurrentReads_ReturnConnectionCountToBaseline()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        await fixture.Store.CreateListAsync("Inbox");

        Task<IReadOnlyList<TodoList>>[] reads = Enumerable.Range(0, 12)
            .Select(_ => fixture.Store.GetListsAsync())
            .ToArray();
        await Task.WhenAll(reads);

        Assert.Equal(0, (await fixture.Store.GetStatsAsync()).ActiveReadConnections);
    }

    [Fact]
    public async Task Settings_RoundTripAndDelete()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();

        Assert.Null(await fixture.Store.GetSettingAsync("shell.test"));
        await fixture.Store.SetSettingAsync("shell.test", "value");
        Assert.Equal("value", await fixture.Store.GetSettingAsync("shell.test"));
        await fixture.Store.SetSettingAsync("shell.test", "updated");
        Assert.Equal("updated", await fixture.Store.GetSettingAsync("shell.test"));
        await fixture.Store.DeleteSettingAsync("shell.test");
        Assert.Null(await fixture.Store.GetSettingAsync("shell.test"));
    }

    private static async Task<long> ScalarLongAsync(
        SqliteConnection connection,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return Convert.ToInt64(
            await command.ExecuteScalarAsync(),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<string> ScalarStringAsync(SqliteConnection connection, string sql)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return (string)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("Query returned no value."));
    }
}
