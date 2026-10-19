using Wintodo.Import.MicrosoftTodo;

namespace Wintodo.Import.Tests;

public sealed class ImportDiagnosticTests
{
    [Fact]
    public async Task MappingReportsPreservedRepeatEmptyTitlesAndAcknowledgedReminders()
    {
        var export = new MicrosoftTodoExport { User = new GraphUser { Id = "fixture" } };
        var list = new MicrosoftTodoListData { List = new GraphTodoList { Id = "list", DisplayName = "Tasks" } };
        list.Tasks.Add(new GraphTodoTask
        {
            Id = "task", Title = " ", Recurrence = new GraphPatternedRecurrence(),
            IsReminderOn = true, Status = "completed",
            ReminderDateTime = new GraphDateTimeTimeZone { DateTime = "2027-01-01T12:00:00", TimeZone = "UTC" },
            ChecklistItems = [new GraphChecklistItem { Id = "step", DisplayName = "" }],
        });
        export.Lists.Add(list);
        await using TempStore temp = await TempStore.CreateAsync();
        var snapshot = GraphTodoMapper.Map(export, TestSupport.Options(temp.Clock.UtcNow));
        var preview = await temp.Store.PreviewImportAsync(snapshot, new(), TestContext.Current.CancellationToken);
        Assert.Equal(1, preview.PreservedRepeats);
        Assert.Equal(2, preview.RenamedEmptyTitles);
        Assert.Equal(1, preview.AcknowledgedReminders);
    }
}
