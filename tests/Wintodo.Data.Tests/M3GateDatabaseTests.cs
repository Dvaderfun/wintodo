using Wintodo.Core.Services;

namespace Wintodo.Data.Tests;

public sealed class M3GateDatabaseTests
{
    [Fact]
    public async Task SeedRequestedUiGateDatabase()
    {
        string? requestedPath = Environment.GetEnvironmentVariable("WINTODO_M3_GATE_DB_PATH");
        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            return;
        }
        string databasePath = Path.GetFullPath(requestedPath);
        if (File.Exists(databasePath))
        {
            throw new InvalidOperationException("M3 setup requires a fresh database.");
        }
        await using var store = new SqliteTaskStore(databasePath, new FakeClock());
        await store.InitializeAsync(TestContext.Current.CancellationToken);
        await store.SetSettingAsync(ISettingsStore.ConfirmBeforeDeleteKey, "False", TestContext.Current.CancellationToken);
        Assert.Equal("False", await store.GetSettingAsync(ISettingsStore.ConfirmBeforeDeleteKey, TestContext.Current.CancellationToken));
    }
}
