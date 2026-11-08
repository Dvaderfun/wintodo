using Microsoft.Data.Sqlite;
using Wintodo.Core.Models;
using Wintodo.Core.Queries;
using Wintodo.Core.Services;
using Wintodo.Data;

namespace Wintodo.Data.Tests;

public sealed class MigrationAndListTests
{
    [Fact]
    public async Task Initialize_Twice_CreatesCurrentSchemaAndIndexes()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();

        await fixture.Store.InitializeAsync(TestContext.Current.CancellationToken);
        StoreStats stats = await fixture.Store.GetStatsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(5, stats.SchemaVersion);
        Assert.Equal(0, stats.ListCount);
        Assert.Equal(0, stats.TaskCount);
        Assert.True(stats.DatabaseSizeBytes > 0);

        await using var connection = TestSqliteConnections.Create(fixture.DatabasePath);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        Assert.Equal("wal", await ScalarStringAsync(connection, "PRAGMA journal_mode;"));
        Assert.Equal(
            14,
            await ScalarLongAsync(
                connection,
                """
                SELECT COUNT(*)
                FROM sqlite_master
                WHERE type = 'table'
                  AND name IN (
                    'schema_version', 'settings', 'list_groups', 'lists', 'tasks',
                    'tags', 'task_tags', 'alarms', 'attachments',
                    'completion_history', 'change_log', 'saved_views', 'accounts',
                    'task_move_outbox'
                  );
                """));
        Assert.Equal(
            17,
            await ScalarLongAsync(
                connection,
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name LIKE 'ix_%';"));
    }

    [Fact]
    public async Task ReopenExistingDatabase_DoesNotReapplyMigration()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        TodoList list = await fixture.Store.CreateListAsync("Inbox", cancellationToken: TestContext.Current.CancellationToken);
        await fixture.Store.DisposeAsync();

        await using var reopened = new SqliteTaskStore(fixture.DatabasePath, fixture.Clock);
        await reopened.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(list.Uid, Assert.Single(await reopened.GetListsAsync(TestContext.Current.CancellationToken)).Uid);
        Assert.Equal(5, (await reopened.GetStatsAsync(TestContext.Current.CancellationToken)).SchemaVersion);
    }

    [Fact]
    public async Task ListAndGroupLifecycle_PersistsAndCoalescesNotifications()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        var changes = new List<StoreChangedEventArgs>();
        fixture.Store.StoreChanged += (_, change) => changes.Add(change);

        ListGroup group = await fixture.Store.CreateGroupAsync("Work", TestContext.Current.CancellationToken);
        TodoList list = await fixture.Store.CreateListAsync("Inbox", ListColor.Red, group.Id, TestContext.Current.CancellationToken);
        await fixture.Store.RenameGroupAsync(group.Id, "Projects", TestContext.Current.CancellationToken);
        await fixture.Store.RenameListAsync(list.Id, "Priority", TestContext.Current.CancellationToken);
        await fixture.Store.SetListColorAsync(list.Id, ListColor.Green, TestContext.Current.CancellationToken);
        await fixture.Store.SetListIconEmojiAsync(list.Id, "\U0001F680", TestContext.Current.CancellationToken);
        await fixture.Store.SetListBackgroundThemeAsync(list.Id, "Forest", TestContext.Current.CancellationToken);
        await fixture.Store.SetListSortAsync(list.Id, SortMode.DueDate, descending: true, TestContext.Current.CancellationToken);
        await fixture.Store.SetListShowCompletedAsync(list.Id, showCompleted: false, TestContext.Current.CancellationToken);
        await fixture.Store.SetListCompletedExpandedAsync(list.Id, completedExpanded: true, TestContext.Current.CancellationToken);
        await fixture.Store.ReorderListAsync(list.Id, 20, TestContext.Current.CancellationToken);
        await fixture.Store.MoveListToGroupAsync(list.Id, group.Id, TestContext.Current.CancellationToken);

        ListGroup storedGroup = Assert.Single(await fixture.Store.GetGroupsAsync(TestContext.Current.CancellationToken));
        TodoList storedList = Assert.Single(await fixture.Store.GetListsAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Projects", storedGroup.Name);
        Assert.Equal("Priority", storedList.DisplayName);
        Assert.Equal(ListColor.Green, storedList.Color);
        Assert.Equal("\U0001F680", storedList.IconEmoji);
        Assert.Equal("Forest", storedList.BackgroundTheme);
        Assert.Equal(SortMode.DueDate, storedList.SortMode);
        Assert.True(storedList.SortDescending);
        Assert.False(storedList.ShowCompleted);
        Assert.True(storedList.CompletedExpanded);
        Assert.Equal(20, storedList.SortOrder);
        Assert.Equal(group.Id, storedList.GroupId);
        Assert.Contains(changes, change => change.ListIds.Contains(list.Id));

        await fixture.Store.DeleteGroupAsync(group.Id, TestContext.Current.CancellationToken);
        Assert.Empty(await fixture.Store.GetGroupsAsync(TestContext.Current.CancellationToken));
        Assert.Null(Assert.Single(await fixture.Store.GetListsAsync(TestContext.Current.CancellationToken)).GroupId);
    }

    [Fact]
    public async Task IncompleteTaskCounts_ExcludeCompletedDeletedAndChildTasks()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        TodoList firstList = await fixture.Store.CreateListAsync("First", cancellationToken: TestContext.Current.CancellationToken);
        TodoList secondList = await fixture.Store.CreateListAsync("Second", cancellationToken: TestContext.Current.CancellationToken);
        TodoTask firstActive = await fixture.Store.CreateTaskAsync(new TodoTask
        {
            ListId = firstList.Id,
            Summary = "First active",
        }, TestContext.Current.CancellationToken);
        await fixture.Store.CreateTaskAsync(new TodoTask
        {
            ListId = firstList.Id,
            Summary = "Second active",
        }, TestContext.Current.CancellationToken);
        await fixture.Store.CreateTaskAsync(new TodoTask
        {
            ListId = firstList.Id,
            ParentUid = firstActive.Uid,
            Summary = "Child step",
        }, TestContext.Current.CancellationToken);
        TodoTask completed = await fixture.Store.CreateTaskAsync(new TodoTask
        {
            ListId = firstList.Id,
            Summary = "Completed",
        }, TestContext.Current.CancellationToken);
        await fixture.Store.CompleteTaskAsync(completed.Uid, TestContext.Current.CancellationToken);
        TodoTask deleted = await fixture.Store.CreateTaskAsync(new TodoTask
        {
            ListId = firstList.Id,
            Summary = "Deleted",
        }, TestContext.Current.CancellationToken);
        await fixture.Store.DeleteTaskAsync(deleted.Uid, TestContext.Current.CancellationToken);
        await fixture.Store.CreateTaskAsync(new TodoTask
        {
            ListId = secondList.Id,
            Summary = "Other list",
        }, TestContext.Current.CancellationToken);

        IReadOnlyDictionary<long, int> counts =
            await fixture.Store.GetIncompleteTaskCountsAsync(TestContext.Current.CancellationToken);
        IReadOnlyList<TodoList> lists = await fixture.Store.GetListsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, counts[firstList.Id]);
        Assert.Equal(1, counts[secondList.Id]);
        Assert.Equal(2, Assert.Single(lists, list => list.Id == firstList.Id).IncompleteTaskCount);
        Assert.Equal(1, Assert.Single(lists, list => list.Id == secondList.Id).IncompleteTaskCount);
    }

    [Fact]
    public async Task SmartListTaskCounts_ApplyEachPredicateAndExcludeChildrenAndDeletedTasks()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        TodoList list = await fixture.Store.CreateListAsync("Inbox", cancellationToken: TestContext.Current.CancellationToken);
        TodoTask matching = await fixture.Store.CreateTaskAsync(new TodoTask
        {
            ListId = list.Id,
            Summary = "Matches every active smart list",
            Priority = (int)TaskPriority.High,
            Due = fixture.Clock.UtcNow.AddDays(1),
            MyDayDate = fixture.Clock.Today,
        }, TestContext.Current.CancellationToken);
        await fixture.Store.CreateTaskAsync(new TodoTask
        {
            ListId = list.Id,
            Summary = "All only",
        }, TestContext.Current.CancellationToken);
        await fixture.Store.CreateTaskAsync(new TodoTask
        {
            ListId = list.Id,
            ParentUid = matching.Uid,
            Summary = "Excluded child",
            Priority = (int)TaskPriority.High,
            Due = fixture.Clock.UtcNow.AddDays(1),
            MyDayDate = fixture.Clock.Today,
        }, TestContext.Current.CancellationToken);
        TodoTask completed = await fixture.Store.CreateTaskAsync(new TodoTask
        {
            ListId = list.Id,
            Summary = "Completed",
            Priority = (int)TaskPriority.High,
            Due = fixture.Clock.UtcNow.AddDays(1),
            MyDayDate = fixture.Clock.Today,
        }, TestContext.Current.CancellationToken);
        await fixture.Store.CompleteTaskAsync(completed.Uid, TestContext.Current.CancellationToken);
        TodoTask deleted = await fixture.Store.CreateTaskAsync(new TodoTask
        {
            ListId = list.Id,
            Summary = "Deleted",
            Priority = (int)TaskPriority.High,
            Due = fixture.Clock.UtcNow.AddDays(1),
            MyDayDate = fixture.Clock.Today,
        }, TestContext.Current.CancellationToken);
        await fixture.Store.DeleteTaskAsync(deleted.Uid, TestContext.Current.CancellationToken);

        IReadOnlyDictionary<ListQueryKind, int> counts =
            await fixture.Store.GetSmartListTaskCountsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, counts[ListQueryKind.MyDay]);
        Assert.Equal(1, counts[ListQueryKind.Important]);
        Assert.Equal(1, counts[ListQueryKind.Planned]);
        Assert.Equal(2, counts[ListQueryKind.All]);
        Assert.Equal(1, counts[ListQueryKind.Completed]);
    }

    [Fact]
    public async Task Initialize_VersionOneDatabase_AddsListIconAndCreatesBackup()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "Wintodo.Tests",
            Guid.NewGuid().ToString("N"));
        string databasePath = Path.Combine(directory, "wintodo.db");
        Directory.CreateDirectory(directory);

        try
        {
            await using (var connection = TestSqliteConnections.Create(databasePath))
            {
                await connection.OpenAsync(TestContext.Current.CancellationToken);
                await using Stream stream = typeof(SqliteTaskStore).Assembly.GetManifestResourceStream(
                        "Wintodo.Data.Migrations.001_init.sql")
                    ?? throw new InvalidOperationException("Version one migration is missing.");
                using var reader = new StreamReader(stream);
                await using SqliteCommand migration = connection.CreateCommand();
                migration.CommandText = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
                await migration.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

                await using SqliteCommand seed = connection.CreateCommand();
                seed.CommandText = """
                    INSERT INTO lists(
                        uid, display_name, color, background_theme, group_id,
                        sort_order, sort_mode, sort_descending, show_completed,
                        completed_expanded, is_default, is_deleted, created_utc, modified_utc)
                    VALUES(
                        'legacy-list', 'Legacy', 0, NULL, NULL,
                        1024, 0, 0, 1, 0, 0, 0,
                        '2026-01-05T12:00:00.0000000Z', '2026-01-05T12:00:00.0000000Z');
                    """;
                await seed.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            }

            await using (var store = new SqliteTaskStore(databasePath, new FakeClock()))
            {
                await store.InitializeAsync(TestContext.Current.CancellationToken);
                StoreStats stats = await store.GetStatsAsync(TestContext.Current.CancellationToken);
                TodoList list = Assert.Single(await store.GetListsAsync(TestContext.Current.CancellationToken));

                Assert.Equal(5, stats.SchemaVersion);
                Assert.Equal("Legacy", list.DisplayName);
                Assert.Null(list.IconEmoji);
            }

            Assert.True(File.Exists(databasePath + ".bak-2"));
            Assert.True(File.Exists(databasePath + ".bak-3"));
            Assert.True(File.Exists(databasePath + ".bak-4"));
            Assert.True(File.Exists(databasePath + ".bak-5"));
        }
        finally
        {
            TestSqliteConnections.ClearStorePools(databasePath);
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DeleteList_SoftDeletesItsTasksAndWritesChangeLog()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        TodoList list = await fixture.Store.CreateListAsync("Disposable", cancellationToken: TestContext.Current.CancellationToken);
        TodoTask task = await fixture.Store.CreateTaskAsync(new TodoTask
        {
            ListId = list.Id,
            Summary = "Remove me",
        }, TestContext.Current.CancellationToken);

        await fixture.Store.DeleteListAsync(list.Id, TestContext.Current.CancellationToken);

        Assert.Empty(await fixture.Store.GetListsAsync(TestContext.Current.CancellationToken));
        Assert.Null(await fixture.Store.GetTaskAsync(task.Uid, TestContext.Current.CancellationToken));
        StoreStats stats = await fixture.Store.GetStatsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, stats.ListCount);
        Assert.Equal(0, stats.TaskCount);
        Assert.Equal(4, stats.PendingChangeCount);

        await using var connection = TestSqliteConnections.Create(fixture.DatabasePath);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
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
            () => fixture.Store.RenameListAsync(999, "Missing", TestContext.Current.CancellationToken));

        Assert.Equal(0, notifications);
    }

    [Fact]
    public async Task ConcurrentReads_ReturnConnectionCountToBaseline()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        await fixture.Store.CreateListAsync("Inbox", cancellationToken: TestContext.Current.CancellationToken);

        Task<IReadOnlyList<TodoList>>[] reads = Enumerable.Range(0, 12)
            .Select(_ => fixture.Store.GetListsAsync())
            .ToArray();
        await Task.WhenAll(reads);

        Assert.Equal(0, (await fixture.Store.GetStatsAsync(TestContext.Current.CancellationToken)).ActiveReadConnections);
    }

    [Fact]
    public async Task Settings_RoundTripAndDelete()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();

        Assert.Null(await fixture.Store.GetSettingAsync("shell.test", TestContext.Current.CancellationToken));
        await fixture.Store.SetSettingAsync("shell.test", "value", TestContext.Current.CancellationToken);
        Assert.Equal("value", await fixture.Store.GetSettingAsync("shell.test", TestContext.Current.CancellationToken));
        await fixture.Store.SetSettingAsync("shell.test", "updated", TestContext.Current.CancellationToken);
        Assert.Equal("updated", await fixture.Store.GetSettingAsync("shell.test", TestContext.Current.CancellationToken));
        await fixture.Store.DeleteSettingAsync("shell.test", TestContext.Current.CancellationToken);
        Assert.Null(await fixture.Store.GetSettingAsync("shell.test", TestContext.Current.CancellationToken));
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
