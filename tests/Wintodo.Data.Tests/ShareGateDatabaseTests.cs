using System.Text.Json;
using Wintodo.Core.Queries;

namespace Wintodo.Data.Tests;

public sealed class ShareGateDatabaseTests
{
    [Fact]
    public async Task VerifyRealSharePersistence()
    {
        string? path = Environment.GetEnvironmentVariable("WINTODO_SHARE_DB_PATH");
        string? expectedPath = Environment.GetEnvironmentVariable("WINTODO_SHARE_EXPECTED");
        if (path is null || expectedPath is null) return;
        var ct = TestContext.Current.CancellationToken;
        await using var store = new SqliteTaskStore(path, new FakeClock());
        await store.InitializeAsync(ct);
        var lists = await store.GetListsAsync(ct);
        var list = Assert.Single(lists, list => list.IsDefault && list.AccountId is null);
        var tasks = await store.GetTasksAsync(ListQuery.ForList(list.Id), ct);
        using var expected = JsonDocument.Parse(await File.ReadAllTextAsync(expectedPath, ct));
        Assert.Equal(expected.RootElement.EnumerateArray().Sum(item => item.GetProperty("count").GetInt32()), tasks.Count);
        foreach (var item in expected.RootElement.EnumerateArray())
        {
            var matches = tasks.Where(task => task.Summary == item.GetProperty("title").GetString());
            Assert.Equal(item.GetProperty("count").GetInt32(), matches.Count());
            foreach (var task in matches)
            {
                Assert.Equal(item.GetProperty("note").GetString(), task.Description);
                Assert.Null(task.ParentUid);
                Assert.Null(task.MyDayDate);
                Assert.Equal(0, task.Priority);
            }
        }
    }
}
