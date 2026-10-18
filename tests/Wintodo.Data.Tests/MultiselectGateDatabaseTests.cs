using Wintodo.Core.Models;
using Wintodo.Core.Services;

namespace Wintodo.Data.Tests;

public sealed class MultiselectGateDatabaseTests
{
    [Fact]
    public async Task SeedRequestedMultiselectDatabase()
    {
        string? path = Environment.GetEnvironmentVariable("WINTODO_MULTISELECT_DB_PATH");
        if (string.IsNullOrWhiteSpace(path)) return;
        if (File.Exists(path)) throw new InvalidOperationException("Multiselect gate needs a fresh database.");
        CancellationToken token = TestContext.Current.CancellationToken;
        await using var store = new SqliteTaskStore(Path.GetFullPath(path), new FakeClock());
        await store.InitializeAsync(token);
        TodoList source = await store.CreateListAsync("Multiselect source", cancellationToken: token);
        await store.CreateListAsync("Multiselect target", cancellationToken: token);
        await store.SetSettingAsync("shell.navigation.selected", $"list:{source.Uid}", token);
        await store.SetSettingAsync(ISettingsStore.ConfirmBeforeDeleteKey, "False", token);
        for (int i = 1; i <= 12; i++)
            await store.CreateTaskAsync(new TodoTask { ListId = source.Id, Summary = $"Bulk task {i:00}" }, token);
        if (Environment.GetEnvironmentVariable("WINTODO_UNDO_COMPLETION_FIXTURE") == "1")
        {
            var task = new TodoTask
            {
                Uid = "undo-recurring", ListId = source.Id, Summary = "Undo recurring completion",
                Due = new FakeClock().UtcNow, Start = new FakeClock().UtcNow.AddHours(-1),
                RRule = "FREQ=DAILY;COUNT=2",
            };
            task.Alarms.Add(new TaskAlarm { Trigger = task.Due.Value.AddMinutes(-10) });
            await store.CreateTaskAsync(task, token);
        }
    }
}
