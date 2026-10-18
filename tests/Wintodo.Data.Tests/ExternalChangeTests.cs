using Wintodo.Core.Models;
using Wintodo.Core.Queries;

namespace Wintodo.Data.Tests;

public sealed class ExternalChangeTests
{
    [Fact]
    public async Task OtherProcessCommit_ChangesVersionAndBecomesReadable_LocalWriteDoesNot()
    {
        await using StoreFixture f = await StoreFixture.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        var list = await f.Store.EnsureDefaultListAsync("Tasks", ct);
        long before = await f.Store.GetExternalChangeVersionAsync(ct);
        await f.Store.CreateTaskAsync(new TodoTask { ListId = list.Id, Summary = "Local" }, ct);
        Assert.Equal(before, await f.Store.GetExternalChangeVersionAsync(ct));
        await using var share = new SqliteTaskStore(f.DatabasePath, f.Clock);
        await share.InitializeAsync(ct);
        await share.CreateTaskAsync(new TodoTask { ListId = list.Id, Summary = "Shared" }, ct);
        long after = await f.Store.GetExternalChangeVersionAsync(ct);
        Assert.NotEqual(before, after);
        Assert.Equal(after, await f.Store.GetExternalChangeVersionAsync(ct));
        Assert.Contains(await f.Store.GetTasksAsync(ListQuery.ForList(list.Id), ct), task => task.Summary == "Shared");
    }
}
