using Wintodo.Core.Models;
using Wintodo.Core.Services;

namespace Wintodo.Data.Tests;

public sealed class ListStabilityGateDatabaseTests
{
    [Fact]
    public async Task SeedRequestedStabilityDatabase()
    {
        string? path = Environment.GetEnvironmentVariable("WINTODO_STABILITY_DB_PATH");
        if (string.IsNullOrWhiteSpace(path)) return;
        if (File.Exists(path)) throw new InvalidOperationException("Stability gate needs a fresh database.");
        await using var store = new SqliteTaskStore(Path.GetFullPath(path), new FakeClock());
        CancellationToken token = TestContext.Current.CancellationToken;
        await store.InitializeAsync(token);
        TodoList list = await store.CreateListAsync("List stability", cancellationToken: token);
        await store.SetSettingAsync("shell.navigation.selected", $"list:{list.Uid}", token);
        await store.SetSettingAsync(ISettingsStore.MoveStarredToTopKey, "False", token);
        for (int i = 1; i <= 200; i++)
        {
            await store.CreateTaskAsync(new TodoTask
            {
                ListId = list.Id, Summary = $"Stability task {i:000}",
            }, token);
        }
    }
}
