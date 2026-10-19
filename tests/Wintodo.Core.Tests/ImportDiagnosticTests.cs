using Wintodo.Core.Import;
using Wintodo.Core.Models;

namespace Wintodo.Core.Tests;

public sealed class ImportDiagnosticTests
{
    [Fact]
    public void SnapshotCountsRenamedStepsAndOnlyAcknowledgedReminders()
    {
        var snapshot = new ImportSnapshot();
        var list = new ImportList();
        var task = new ImportTask { HasPreservedRepeat = true, HasRenamedTitle = true };
        task.Steps.Add(new ImportTask { HasRenamedTitle = true });
        task.Task.Alarms.Add(new TaskAlarm { IsAcknowledged = true });
        task.Task.Alarms.Add(new TaskAlarm { IsAcknowledged = false });
        list.Tasks.Add(task);
        snapshot.Lists.Add(list);
        Assert.Equal(1, snapshot.PreservedRepeats);
        Assert.Equal(2, snapshot.RenamedEmptyTitles);
        Assert.Equal(1, snapshot.AcknowledgedReminders);
    }
}
