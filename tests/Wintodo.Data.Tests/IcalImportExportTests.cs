using System.Text;
using Microsoft.Data.Sqlite;
using Wintodo.Core.Ical;
using Wintodo.Core.Models;

namespace Wintodo.Data.Tests;

public sealed class IcalImportExportTests
{
    private static readonly string[] FixtureNames =
    [
        "nextcloud.ics",
        "tasks-org.ics",
        "thunderbird.ics",
        "apple-reminders.ics",
    ];

    [Fact]
    public async Task FixtureCorpus_ImportsAtomicallyAndExportsSemanticallyEquivalentTasks()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        IReadOnlyList<IcalCalendar> calendars = ReadFixtures();
        int notifications = 0;
        fixture.Store.StoreChanged += (_, _) => notifications++;

        IcalImportPreview preview = await fixture.Store.PreviewIcalImportAsync(calendars);
        IcalImportResult result = await fixture.Store.ImportIcalAsync(calendars);

        Assert.Equal(6, preview.TaskCount);
        Assert.Equal(4, preview.ListCount);
        Assert.Equal(0, preview.ConflictCount);
        Assert.Equal(4, result.CreatedListCount);
        Assert.Equal(6, result.CreatedTaskCount);
        Assert.Equal(0, result.UpdatedTaskCount);
        Assert.Equal(1, notifications);

        IReadOnlyList<TodoList> lists = await fixture.Store.GetListsAsync();
        Assert.Equal(4, lists.Count);
        var reader = new IcalReader(fixture.Clock.LocalTimeZone);
        var writer = new IcalWriter(fixture.Clock.LocalTimeZone);
        foreach (IcalCalendar originalCalendar in calendars)
        {
            TodoList list = Assert.Single(lists, item => string.Equals(
                item.DisplayName,
                originalCalendar.Name,
                StringComparison.OrdinalIgnoreCase));
            IReadOnlyList<TodoTask> stored = await fixture.Store.GetTasksForExportAsync(list.Id);
            IcalCalendar reparsed = reader.Read(
                writer.WriteCalendar(list, stored, fixture.Clock.UtcNow));
            Assert.Equal(originalCalendar.Tasks.Count, reparsed.Tasks.Count);
            foreach (TodoTask expected in originalCalendar.Tasks)
            {
                TodoTask actual = Assert.Single(reparsed.Tasks, task => task.Uid == expected.Uid);
                AssertEquivalent(expected, actual);
            }
        }

        using var connection = new SqliteConnection($"Data Source={fixture.DatabasePath};Mode=ReadOnly");
        await connection.OpenAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM tasks WHERE ical_unknown IS NOT NULL;";
        Assert.Equal(6L, (long)(await command.ExecuteScalarAsync() ?? 0L));
    }

    [Fact]
    public async Task Import_ReportsAndUpdatesUidConflictsWithoutDuplicatingTasks()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        IcalCalendar calendar = ReadFixture("nextcloud.ics");
        await fixture.Store.ImportIcalAsync([calendar]);
        calendar.Tasks[0].Summary = "Imported replacement";

        IcalImportPreview preview = await fixture.Store.PreviewIcalImportAsync([calendar]);
        IcalImportResult result = await fixture.Store.ImportIcalAsync([calendar]);

        Assert.Equal(2, preview.ConflictCount);
        Assert.Equal(0, result.CreatedTaskCount);
        Assert.Equal(2, result.UpdatedTaskCount);
        TodoTask updated = (await fixture.Store.GetTaskAsync("nextcloud-parent"))!;
        Assert.Equal("Imported replacement", updated.Summary);
        TodoList list = Assert.Single(await fixture.Store.GetListsAsync());
        Assert.Equal(2, (await fixture.Store.GetTasksForExportAsync(list.Id)).Count);
    }

    [Fact]
    public async Task Import_ChildBeforeParent_Succeeds()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        var calendar = new IcalCalendar { Name = "Out of order" };
        calendar.Tasks.Add(new TodoTask
        {
            Uid = "child",
            ParentUid = "parent",
            Summary = "Child",
        });
        calendar.Tasks.Add(new TodoTask
        {
            Uid = "parent",
            Summary = "Parent",
        });

        await fixture.Store.ImportIcalAsync([calendar]);

        TodoTask child = (await fixture.Store.GetTaskAsync("child"))!;
        Assert.Equal("parent", child.ParentUid);
    }

    [Fact]
    public async Task Import_MissingParent_RollsBackEntireBatch()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        TodoList existing = await fixture.Store.CreateListAsync("Existing");
        var calendar = new IcalCalendar { Name = "Must roll back" };
        calendar.Tasks.Add(new TodoTask
        {
            Uid = "valid-first-row",
            Summary = "This insert must roll back",
        });
        calendar.Tasks.Add(new TodoTask
        {
            Uid = "missing-parent-child",
            ParentUid = "does-not-exist",
            Summary = "Invalid parent",
        });

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => fixture.Store.ImportIcalAsync([calendar]));

        IReadOnlyList<TodoList> lists = await fixture.Store.GetListsAsync();
        Assert.Single(lists);
        Assert.Equal(existing.Id, lists[0].Id);
        Assert.Null(await fixture.Store.GetTaskAsync("valid-first-row"));
        Assert.Null(await fixture.Store.GetTaskAsync("missing-parent-child"));
    }

    [Fact]
    public async Task Import_CyclicHierarchy_RollsBackEntireBatch()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        var calendar = new IcalCalendar { Name = "Cycle" };
        calendar.Tasks.Add(new TodoTask
        {
            Uid = "cycle-a",
            ParentUid = "cycle-b",
            Summary = "A",
        });
        calendar.Tasks.Add(new TodoTask
        {
            Uid = "cycle-b",
            ParentUid = "cycle-a",
            Summary = "B",
        });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Store.ImportIcalAsync([calendar]));

        Assert.Empty(await fixture.Store.GetListsAsync());
        Assert.Null(await fixture.Store.GetTaskAsync("cycle-a"));
        Assert.Null(await fixture.Store.GetTaskAsync("cycle-b"));
    }

    [Fact]
    public async Task Import_CannotMoveAParentAwayFromExistingChildren()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        TodoList originalList = await fixture.Store.CreateListAsync("Original");
        TodoTask parent = await fixture.Store.CreateTaskAsync(new TodoTask
        {
            ListId = originalList.Id,
            Summary = "Parent",
        });
        await fixture.Store.AddStepAsync(parent.Uid, "Existing child");
        var calendar = new IcalCalendar { Name = "Imported" };
        calendar.Tasks.Add(new TodoTask
        {
            Uid = parent.Uid,
            Summary = "Replacement",
        });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Store.ImportIcalAsync([calendar]));

        TodoTask unchanged = (await fixture.Store.GetTaskAsync(parent.Uid))!;
        Assert.Equal(originalList.Id, unchanged.ListId);
        Assert.Equal("Parent", unchanged.Summary);
        Assert.Single(await fixture.Store.GetListsAsync());
    }

    [Fact]
    public async Task ExportQuery_IncludesChildTasksAndCollections()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        TodoList list = await fixture.Store.CreateListAsync("Export");
        var parent = new TodoTask
        {
            ListId = list.Id,
            Summary = "Parent",
        };
        parent.Categories.Add("Category");
        parent.Alarms.Add(new TaskAlarm
        {
            Uid = "export-alarm",
            Action = AlarmAction.Display,
            TriggerOffsetSeconds = -600,
        });
        parent = await fixture.Store.CreateTaskAsync(parent);
        await fixture.Store.AddStepAsync(parent.Uid, "Child");

        IReadOnlyList<TodoTask> exported = await fixture.Store.GetTasksForExportAsync(list.Id);

        Assert.Equal(2, exported.Count);
        TodoTask loadedParent = Assert.Single(exported, task => task.ParentUid is null);
        Assert.Equal(["Category"], loadedParent.Categories);
        Assert.Single(loadedParent.Alarms);
        Assert.Single(exported, task => task.ParentUid == loadedParent.Uid);
    }

    private static IReadOnlyList<IcalCalendar> ReadFixtures()
    {
        return FixtureNames.Select(ReadFixture).ToArray();
    }

    private static IcalCalendar ReadFixture(string fixtureName)
    {
        byte[] bytes = File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "ical", fixtureName));
        IcalCalendar calendar = new IcalReader(TimeZoneInfo.Utc).Read(bytes);
        calendar.Name ??= Path.GetFileNameWithoutExtension(fixtureName);
        return calendar;
    }

    private static void AssertEquivalent(TodoTask expected, TodoTask actual)
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
