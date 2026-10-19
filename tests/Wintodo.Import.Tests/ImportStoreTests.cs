using Microsoft.Data.Sqlite;
using Wintodo.Core.Import;
using Wintodo.Core.Models;
using Wintodo.Core.Queries;
using Wintodo.Import.MicrosoftTodo;

namespace Wintodo.Import.Tests;

/// <summary>Real fixture → mapper → SQLite store, run more than once.</summary>
public sealed class ImportStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Preview_MatchesTheImportAndWritesNothing()
    {
        await using TempStore temp = await TempStore.CreateAsync();
        ImportSnapshot snapshot = await MapFixtureAsync();
        int changesBefore = await CountAsync(temp, "SELECT COUNT(*) FROM change_log;");

        ImportOutcome preview = await temp.Store.PreviewImportAsync(snapshot, Options(temp), Token);
        ImportOutcome previewAgain = await temp.Store.PreviewImportAsync(snapshot, Options(temp), Token);

        Assert.Equal(3, preview.NewLists);
        Assert.Equal(1, preview.ExistingLists);
        Assert.Equal(6, preview.NewTasks);
        Assert.Equal(2, preview.NewSteps);
        Assert.Equal(1, preview.NewAttachments);
        Assert.Equal(["A1"], preview.PendingAttachmentIds);
        Assert.Equal(string.Empty, preview.RunId);
        Assert.Equivalent(preview, previewAgain);
        Assert.Equal(changesBefore, await CountAsync(temp, "SELECT COUNT(*) FROM change_log;"));
        Assert.Equal(0, await CountAsync(temp, "SELECT COUNT(*) FROM import_links;"));

        await StageAttachmentsAsync(snapshot, temp);
        ImportOutcome applied = await temp.Store.ApplyImportAsync(snapshot, Options(temp), Token);
        Assert.Equal(preview.NewTasks, applied.NewTasks);
        Assert.Equal(preview.NewLists, applied.NewLists);
        Assert.Equal(preview.NewAttachments, applied.NewAttachments);

        ImportSnapshot again = await MapFixtureAsync();
        ImportOutcome second = await temp.Store.PreviewImportAsync(again, Options(temp), Token);
        Assert.Equal(0, second.NewAttachments);
        Assert.Empty(second.PendingAttachmentIds);
    }

    [Fact]
    public async Task Import_PlacesEverythingAndMergesTheDefaultList()
    {
        await using TempStore temp = await TempStore.CreateAsync();
        ImportSnapshot snapshot = await MapFixtureAsync();
        await StageAttachmentsAsync(snapshot, temp);

        ImportOutcome outcome = await temp.Store.ApplyImportAsync(snapshot, Options(temp), Token);

        Assert.Equal(1, outcome.NewAttachments);
        Assert.NotEmpty(outcome.RunId);
        IReadOnlyList<TodoList> lists = await temp.Store.GetListsAsync(Token);
        Assert.Equal(["Tasks", "Groceries", "Flagged email", "Team plans"], lists.Select(list => list.DisplayName));
        Assert.Equal("🛒", lists[1].IconEmoji);

        TodoList defaults = lists.Single(list => list.IsDefault);
        IReadOnlyList<TodoTask> tasks = await temp.Store.GetTasksAsync(ListQuery.ForList(defaults.Id), Token);
        Assert.Equal(
            ["Book dentist", "Send the quarterly report", "Water the plants"],
            tasks.Select(task => task.Summary).Order(StringComparer.Ordinal));
        Assert.All(tasks, task => Assert.True(task.IsDirty));

        TodoTask water = (await temp.Store.GetTaskAsync(tasks.Single(task => task.Summary == "Water the plants").Uid, Token))!;
        Assert.Equal("FREQ=WEEKLY;BYDAY=MO,WE;WKST=SU", water.RRule);
        Assert.Single(water.Alarms);
        IReadOnlyList<TodoTask> steps = await temp.Store.GetChildrenAsync(water.Uid, Token);
        Assert.Equal(["Fill the can", "Kitchen window"], steps.Select(step => step.Summary));

        TodoTask report = (await temp.Store.GetTaskAsync(tasks.Single(task => task.Summary.StartsWith("Send", StringComparison.Ordinal)).Uid, Token))!;
        TaskAttachment attachment = Assert.Single(report.Attachments);
        string copied = Path.Combine(temp.Directory, attachment.RelativePath);
        Assert.Equal("quarter,revenue\nq3,1200\n", await File.ReadAllTextAsync(copied, Token));
        Assert.Equal(["Red category", "Work"], report.Categories);

        Assert.Single(await temp.Store.SearchAsync("quarterly", cancellationToken: Token));
        IReadOnlyList<ImportSourceInfo> sources = await temp.Store.GetImportSourcesAsync("mstodo", Token);
        Assert.Equal("fixture.person@example.com", Assert.Single(sources).DisplayName);
    }

    [Fact]
    public async Task SecondRun_DuplicatesNothing()
    {
        await using TempStore temp = await TempStore.CreateAsync();
        ImportSnapshot first = await MapFixtureAsync();
        await StageAttachmentsAsync(first, temp);
        await temp.Store.ApplyImportAsync(first, Options(temp), Token);
        int tasksAfterFirst = await CountAsync(temp, "SELECT COUNT(*) FROM tasks WHERE is_deleted = 0;");
        int listsAfterFirst = await CountAsync(temp, "SELECT COUNT(*) FROM lists WHERE is_deleted = 0;");
        int attachmentsAfterFirst = await CountAsync(temp, "SELECT COUNT(*) FROM attachments;");

        ImportSnapshot second = await MapFixtureAsync();
        await StageAttachmentsAsync(second, temp);
        ImportOutcome outcome = await temp.Store.ApplyImportAsync(second, Options(temp), Token);

        Assert.False(outcome.HasChanges);
        Assert.Equal(6, outcome.UnchangedTasks);
        Assert.Equal(4, outcome.ExistingLists);
        Assert.Equal(tasksAfterFirst, await CountAsync(temp, "SELECT COUNT(*) FROM tasks WHERE is_deleted = 0;"));
        Assert.Equal(listsAfterFirst, await CountAsync(temp, "SELECT COUNT(*) FROM lists WHERE is_deleted = 0;"));
        Assert.Equal(attachmentsAfterFirst, await CountAsync(temp, "SELECT COUNT(*) FROM attachments;"));
    }

    [Fact]
    public async Task SourceEdit_UpdatesWhileLocalEditsWinAndLocalDeletesStayDeleted()
    {
        await using TempStore temp = await TempStore.CreateAsync();
        await temp.Store.ApplyImportAsync(await MapFixtureAsync(), Options(temp), Token);
        TodoTask water = await FindAsync(temp, "Water the plants");
        TodoTask dentist = await FindAsync(temp, "Book dentist");
        TodoTask milk = await FindAsync(temp, "Milk");

        // Local: rename the dentist task, delete milk. Microsoft: edit all three.
        temp.Clock.UtcNow = Now.AddHours(1);
        dentist.Summary = "Book dentist (local)";
        await temp.Store.UpdateTaskAsync(dentist, Token);
        await temp.Store.DeleteTaskAsync(milk.Uid, Token);

        MicrosoftTodoExport export = await TestSupport.ReadFixtureAsync();
        foreach (GraphTodoTask task in export.Lists.SelectMany(list => list.Tasks)
                     .Where(task => task.Id is "T1" or "T2" or "T4"))
        {
            task.Title += " v2";
            task.LastModifiedDateTime = Now.AddHours(2);
        }

        ImportOutcome outcome = await temp.Store.ApplyImportAsync(
            GraphTodoMapper.Map(export, TestSupport.Options(Now)),
            Options(temp),
            Token);

        Assert.Equal(1, outcome.UpdatedTasks);
        Assert.Equal(1, outcome.KeptLocalTasks);
        Assert.Equal(1, outcome.SkippedDeletedTasks);
        Assert.Equal("Water the plants v2", (await temp.Store.GetTaskAsync(water.Uid, Token))!.Summary);
        Assert.Equal("Book dentist (local)", (await temp.Store.GetTaskAsync(dentist.Uid, Token))!.Summary);
        Assert.Null(await temp.Store.GetTaskAsync(milk.Uid, Token));
        Assert.Equal(0, await CountAsync(temp, "SELECT COUNT(*) FROM tasks WHERE summary LIKE 'Milk%' AND is_deleted = 0;"));

        ImportOutcome overwrite = await temp.Store.ApplyImportAsync(
            GraphTodoMapper.Map(export, TestSupport.Options(Now)),
            Options(temp, overwriteLocalEdits: true),
            Token);
        Assert.Equal(1, overwrite.UpdatedTasks);
        Assert.Equal("Book dentist v2", (await temp.Store.GetTaskAsync(dentist.Uid, Token))!.Summary);
    }

    [Fact]
    public async Task TaskMovedInToDo_GetsANewIdButIsMovedNotDuplicated()
    {
        await using TempStore temp = await TempStore.CreateAsync();
        await temp.Store.ApplyImportAsync(await MapFixtureAsync(), Options(temp), Token);
        TodoTask milk = await FindAsync(temp, "Milk");

        MicrosoftTodoExport export = await TestSupport.ReadFixtureAsync();
        GraphTodoTask moved = export.Lists[1].Tasks.Single(task => task.Id == "T4");
        export.Lists[1].Tasks.Remove(moved);
        moved.Id = "T4-after-move";
        moved.LastModifiedDateTime = Now.AddHours(3);
        export.Lists[0].Tasks.Add(moved);

        ImportOutcome outcome = await temp.Store.ApplyImportAsync(
            GraphTodoMapper.Map(export, TestSupport.Options(Now)),
            Options(temp),
            Token);

        Assert.Equal(0, outcome.NewTasks);
        Assert.Equal(1, outcome.UpdatedTasks);
        TodoTask after = (await temp.Store.GetTaskAsync(milk.Uid, Token))!;
        long defaultListId = (await temp.Store.GetListsAsync(Token)).Single(list => list.IsDefault).Id;
        Assert.Equal(defaultListId, after.ListId);
        Assert.Equal(1, await CountAsync(temp, "SELECT COUNT(*) FROM tasks WHERE summary = 'Milk' AND is_deleted = 0;"));

        ImportOutcome again = await temp.Store.ApplyImportAsync(
            GraphTodoMapper.Map(export, TestSupport.Options(Now)),
            Options(temp),
            Token);
        Assert.False(again.HasChanges);
    }

    [Fact]
    public async Task HandTypedTaskWithSameTitleAndDue_IsReportedAndSkipped()
    {
        await using TempStore temp = await TempStore.CreateAsync();
        TodoList groceries = await temp.Store.CreateListAsync("groceries", cancellationToken: Token);
        await temp.Store.CreateTaskAsync(new TodoTask { ListId = groceries.Id, Summary = "  milk " }, Token);

        ImportOutcome outcome = await temp.Store.ApplyImportAsync(await MapFixtureAsync(), Options(temp), Token);

        Assert.Equal(1, outcome.PossibleDuplicates);
        Assert.Equal(2, outcome.ExistingLists);
        Assert.Equal(1, await CountAsync(temp, "SELECT COUNT(*) FROM tasks WHERE lower(trim(summary)) = 'milk' AND is_deleted = 0;"));
        Assert.Equal(1, await CountAsync(temp, "SELECT COUNT(*) FROM lists WHERE display_name LIKE 'groceries' AND is_deleted = 0;"));
    }

    [Fact]
    public async Task RemoveRun_SoftDeletesWhatItCreatedAndAllowsAFreshImport()
    {
        await using TempStore temp = await TempStore.CreateAsync();
        ImportOutcome first = await temp.Store.ApplyImportAsync(await MapFixtureAsync(), Options(temp), Token);

        int removed = await temp.Store.RemoveImportRunAsync(first.RunId, Token);

        Assert.Equal(8, removed);
        Assert.Equal(0, await CountAsync(temp, "SELECT COUNT(*) FROM tasks WHERE is_deleted = 0;"));
        Assert.Equal(["Tasks"], (await temp.Store.GetListsAsync(Token)).Select(list => list.DisplayName));
        Assert.True(await CountAsync(temp, "SELECT COUNT(*) FROM change_log WHERE operation = 2;") >= 11);

        ImportOutcome again = await temp.Store.ApplyImportAsync(await MapFixtureAsync(), Options(temp), Token);
        Assert.Equal(6, again.NewTasks);
        Assert.Equal(6, await CountAsync(temp, "SELECT COUNT(*) FROM tasks WHERE is_deleted = 0 AND parent_uid IS NULL;"));
    }

    [Fact]
    public async Task LostLinkTable_AdoptsTheDeterministicUidInsteadOfDuplicating()
    {
        await using TempStore temp = await TempStore.CreateAsync();
        await temp.Store.ApplyImportAsync(await MapFixtureAsync(), Options(temp), Token);
        await ExecuteAsync(temp, "DELETE FROM import_links;");

        await temp.Store.ApplyImportAsync(await MapFixtureAsync(), Options(temp), Token);

        Assert.Equal(6, await CountAsync(temp, "SELECT COUNT(*) FROM tasks WHERE is_deleted = 0 AND parent_uid IS NULL;"));
    }

    [Fact]
    public async Task DeletedImportedList_IsNotRecreated()
    {
        await using TempStore temp = await TempStore.CreateAsync();
        await temp.Store.ApplyImportAsync(await MapFixtureAsync(), Options(temp), Token);
        TodoList groceries = (await temp.Store.GetListsAsync(Token)).Single(list => list.DisplayName == "Groceries");
        await temp.Store.DeleteListAsync(groceries.Id, Token);

        ImportOutcome outcome = await temp.Store.ApplyImportAsync(await MapFixtureAsync(), Options(temp), Token);

        Assert.Equal(1, outcome.SkippedLists);
        Assert.DoesNotContain(await temp.Store.GetListsAsync(Token), list => list.DisplayName == "Groceries");
    }

    private static async Task<ImportSnapshot> MapFixtureAsync()
    {
        return GraphTodoMapper.Map(await TestSupport.ReadFixtureAsync(), TestSupport.Options(Now));
    }

    private static ImportOptions Options(TempStore temp, bool overwriteLocalEdits = false)
    {
        return new ImportOptions { AttachmentRoot = temp.Directory, OverwriteLocalEdits = overwriteLocalEdits };
    }

    private static async Task StageAttachmentsAsync(ImportSnapshot snapshot, TempStore temp)
    {
        ImportOutcome preview = await temp.Store.PreviewImportAsync(snapshot, Options(temp), Token);
        await new MicrosoftTodoReader(TestSupport.FixtureClient()).DownloadAttachmentsAsync(
            snapshot,
            Path.Combine(temp.Directory, "staging", Guid.NewGuid().ToString("N")),
            preview.PendingAttachmentIds,
            cancellationToken: Token);
    }

    [Fact]
    public async Task SecondDownloadMakesNoAttachmentRequestsAndDeletedTaskIsNotPending()
    {
        await using TempStore temp = await TempStore.CreateAsync();
        ImportSnapshot first = await MapFixtureAsync();
        await StageAttachmentsAsync(first, temp);
        await temp.Store.ApplyImportAsync(first, Options(temp), Token);
        ImportSnapshot second = await MapFixtureAsync();
        ImportOutcome preview = await temp.Store.PreviewImportAsync(second, Options(temp), Token);
        var handler = new FixtureGraphHandler(TestSupport.FixturePath("basic"));
        var reader = new MicrosoftTodoReader(new GraphTodoClient(new HttpClient(handler), "fixture"));
        await reader.DownloadAttachmentsAsync(second, Path.Combine(temp.Directory, "second"),
            preview.PendingAttachmentIds, cancellationToken: Token);
        Assert.Equal(0, handler.RequestCount);

        TodoTask report = await FindAsync(temp, "Send the quarterly report");
        await temp.Store.DeleteTaskAsync(report.Uid, Token);
        MicrosoftTodoExport export = await TestSupport.ReadFixtureAsync();
        GraphTodoTask source = export.Lists.SelectMany(list => list.Tasks).Single(task => task.Id == "T3");
        source.Attachments.Add(new GraphAttachment { Id = "new-attachment", Name = "new.txt", Size = 10 });
        ImportOutcome deleted = await temp.Store.PreviewImportAsync(
            GraphTodoMapper.Map(export, TestSupport.Options(Now)), Options(temp), Token);
        Assert.Empty(deleted.PendingAttachmentIds);
        Assert.Equal(0, deleted.NewAttachments);
    }

    private static async Task<TodoTask> FindAsync(TempStore temp, string summary)
    {
        return (await temp.Store.SearchAsync(summary, cancellationToken: Token))
            .Single(task => task.Summary == summary);
    }

    private static async Task<int> CountAsync(TempStore temp, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={Path.Combine(temp.Directory, "wintodo.db")};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync(Token);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync(Token), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteAsync(TempStore temp, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={Path.Combine(temp.Directory, "wintodo.db")};Pooling=False");
        await connection.OpenAsync(Token);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(Token);
    }
}
