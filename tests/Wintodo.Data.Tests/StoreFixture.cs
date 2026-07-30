using Microsoft.Data.Sqlite;
using Wintodo.Core.Services;
using Wintodo.Data;

namespace Wintodo.Data.Tests;

internal sealed class StoreFixture : IAsyncDisposable
{
    private StoreFixture(string directoryPath, FakeClock clock, SqliteTaskStore store)
    {
        DirectoryPath = directoryPath;
        DatabasePath = Path.Combine(directoryPath, "wintodo.db");
        Clock = clock;
        Store = store;
    }

    internal string DirectoryPath { get; }

    internal string DatabasePath { get; }

    internal FakeClock Clock { get; }

    internal SqliteTaskStore Store { get; }

    internal static async Task<StoreFixture> CreateAsync()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "Wintodo.Tests",
            Guid.NewGuid().ToString("N"));
        var clock = new FakeClock();
        var store = new SqliteTaskStore(Path.Combine(directory, "wintodo.db"), clock);
        var fixture = new StoreFixture(directory, clock, store);
        await store.InitializeAsync();
        return fixture;
    }

    public async ValueTask DisposeAsync()
    {
        await Store.DisposeAsync();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(DirectoryPath))
        {
            Directory.Delete(DirectoryPath, recursive: true);
        }
    }
}

internal sealed class FakeClock : IClock
{
    internal FakeClock()
    {
        UtcNow = new DateTimeOffset(2026, 1, 5, 12, 0, 0, TimeSpan.Zero);
    }

    public DateTimeOffset UtcNow { get; set; }

    public TimeZoneInfo LocalTimeZone { get; set; } = TimeZoneInfo.Utc;

    public DateOnly Today => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTime(UtcNow, LocalTimeZone).DateTime);
}
