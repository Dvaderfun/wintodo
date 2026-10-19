using Wintodo.Core.Ical;
using Wintodo.Core.Import;
using Wintodo.Core.Models;
using Wintodo.Import.MicrosoftTodo;
using TaskStatus = Wintodo.Core.Models.TaskStatus;

namespace Wintodo.Import.Tests;

public sealed class GraphTodoMapperTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Lists_MapKindsEmojiAndSharing()
    {
        ImportSnapshot snapshot = GraphTodoMapper.Map(await TestSupport.ReadFixtureAsync(), TestSupport.Options(Now));

        Assert.Equal("mstodo", snapshot.Provider);
        Assert.Equal("user-0001", snapshot.AccountKey);
        Assert.Equal("fixture.person@example.com", snapshot.AccountDisplayName);
        Assert.Equal(
            [ImportListKind.Default, ImportListKind.User, ImportListKind.FlaggedEmail, ImportListKind.User],
            snapshot.Lists.Select(list => list.Kind));
        ImportList groceries = snapshot.Lists[1];
        Assert.Equal("Groceries", groceries.DisplayName);
        Assert.Equal("🛒", groceries.IconEmoji);
        Assert.True(snapshot.Lists[3].IsShared);
        Assert.False(snapshot.Lists[3].IsOwner);
    }

    [Fact]
    public async Task Task_KeepsNoteDatesReminderRecurrenceStepsAndUnknownFields()
    {
        ImportSnapshot snapshot = GraphTodoMapper.Map(await TestSupport.ReadFixtureAsync(), TestSupport.Options(Now));
        ImportTask item = snapshot.Lists[0].Tasks[0];
        TodoTask task = item.Task;

        Assert.Equal("Water the plants", task.Summary);
        Assert.Equal("Use the blue can.\nNot too much.", task.Description);
        Assert.Equal(TaskStatus.NeedsAction, task.Status);
        Assert.Equal(0, task.Priority);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 8, 15, 30, 123, TimeSpan.Zero).AddTicks(4567), task.Created);
        Assert.Equal(new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero), task.LastModified);
        Assert.Equal(new DateTimeOffset(2026, 10, 21, 0, 0, 0, TimeSpan.Zero), task.Due);
        Assert.True(task.DueIsDateOnly);
        Assert.Equal("FREQ=WEEKLY;BYDAY=MO,WE;WKST=SU", task.RRule);
        TaskAlarm alarm = Assert.Single(task.Alarms);
        Assert.Equal(new DateTimeOffset(2026, 10, 21, 7, 30, 0, TimeSpan.Zero), alarm.Trigger);
        Assert.False(alarm.IsAcknowledged);
        Assert.Equal(["T1"], IcalExtensionProperties.ReadValues(task.IcalUnknown, "X-MSTODO-ID"));
        Assert.Equal(["L-default"], IcalExtensionProperties.ReadValues(task.IcalUnknown, "X-MSTODO-LIST-ID"));
        Assert.Contains("\"futureProperty\"", Assert.Single(IcalExtensionProperties.ReadValues(task.IcalUnknown, "X-MSTODO-EXTRA")), StringComparison.Ordinal);

        Assert.Equal(["C1", "C2"], item.Steps.Select(step => step.ExternalId));
        TodoTask checkedStep = item.Steps[0].Task;
        Assert.Equal("Fill the can", checkedStep.Summary);
        Assert.Equal(TaskStatus.Completed, checkedStep.Status);
        Assert.Equal(new DateTimeOffset(2026, 10, 9, 18, 0, 0, TimeSpan.Zero), checkedStep.CompletedAt);
        Assert.Equal(TaskStatus.NeedsAction, item.Steps[1].Task.Status);
    }

    [Fact]
    public async Task Task_MapsCompletionImportanceStatusHtmlLinksAndAttachments()
    {
        ImportSnapshot snapshot = GraphTodoMapper.Map(await TestSupport.ReadFixtureAsync(), TestSupport.Options(Now));
        TodoTask completed = snapshot.Lists[0].Tasks[1].Task;
        ImportTask reportItem = snapshot.Lists[0].Tasks[2];
        TodoTask report = reportItem.Task;

        Assert.Equal(TaskStatus.Completed, completed.Status);
        Assert.Equal(100, completed.PercentComplete);
        Assert.Equal(new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero), completed.CompletedAt);
        Assert.Equal(9, completed.Priority);
        Assert.Null(completed.Description);

        Assert.Equal(1, report.Priority);
        Assert.Equal(TaskStatus.NeedsAction, report.Status);
        Assert.Equal(["waitingOnOthers"], IcalExtensionProperties.ReadValues(report.IcalUnknown, "X-MSTODO-STATUS"));
        Assert.Equal("Numbers from finance & sales\n\n• Revenue\n• Costs", report.Description);
        Assert.StartsWith("<html>", Assert.Single(IcalExtensionProperties.ReadValues(report.IcalUnknown, "X-ALT-DESC")), StringComparison.Ordinal);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero), report.Due);
        Assert.Equal(["Work", "Red category"], report.Categories);
        Assert.Equal("https://example.com/report", report.Url);
        Assert.Contains("doc-77", Assert.Single(IcalExtensionProperties.ReadValues(report.IcalUnknown, "X-MSTODO-LINKED-RESOURCE")), StringComparison.Ordinal);
        TaskAlarm pastReminder = Assert.Single(report.Alarms);
        Assert.True(pastReminder.IsAcknowledged);
        ImportAttachment attachment = Assert.Single(reportItem.Attachments);
        Assert.Equal("figures.csv", attachment.FileName);
        Assert.Equal("text/csv", attachment.MimeType);
    }

    [Fact]
    public async Task UnconvertibleRecurrence_IsKeptRawAndInProgressMapsToInProcess()
    {
        ImportSnapshot snapshot = GraphTodoMapper.Map(await TestSupport.ReadFixtureAsync(), TestSupport.Options(Now));
        TodoTask rent = snapshot.Lists[1].Tasks[1].Task;

        Assert.Null(rent.RRule);
        Assert.Equal(TaskStatus.InProcess, rent.Status);
        string raw = Assert.Single(IcalExtensionProperties.ReadValues(rent.IcalUnknown, "X-MSTODO-RECURRENCE"));
        Assert.Contains("relativeMonthly", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Options_FilterListsCompletedTasksAndAttachments()
    {
        MicrosoftTodoExport export = await TestSupport.ReadFixtureAsync();
        ImportSnapshot snapshot = GraphTodoMapper.Map(export, new MicrosoftTodoMapOptions
        {
            SelectedListIds = new HashSet<string>(["L-default"]),
            IncludeCompleted = false,
            IncludeAttachments = false,
            Now = Now,
        });

        ImportList list = Assert.Single(snapshot.Lists);
        Assert.Equal(["T1", "T3"], list.Tasks.Select(task => task.ExternalId));
        Assert.All(list.Tasks, task => Assert.Empty(task.Attachments));
    }
}
