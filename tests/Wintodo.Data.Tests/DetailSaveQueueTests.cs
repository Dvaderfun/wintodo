using Wintodo.Core.Models;
using Wintodo.ViewModels;

namespace Wintodo.Data.Tests;

public sealed class DetailSaveQueueTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task LeavingEditorDrainsLatestTitleAndNote(bool closingWindow, bool olderSaveInFlight)
    {
        var ct = TestContext.Current.CancellationToken;
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        var list = await fixture.Store.EnsureDefaultListAsync("Tasks", ct);
        var a = await fixture.Store.CreateTaskAsync(new TodoTask { ListId = list.Id, Summary = "A" }, ct);
        var b = await fixture.Store.CreateTaskAsync(new TodoTask { ListId = list.Id, Summary = "B", Description = "B note" }, ct);
        var delay = NewSignal();
        var olderSaveStarted = NewSignal();
        var releaseOlderSave = NewSignal();
        var queue = new DetailSaveQueue { DelayAsync = (_, token) => delay.Task.WaitAsync(token) };

        async Task<bool> SaveAsync(TodoTask owner, string title, string note, bool pause)
        {
            if (pause)
            {
                olderSaveStarted.SetResult();
                await releaseOlderSave.Task.WaitAsync(ct);
            }
            await fixture.Store.SetTaskTextAsync(owner.Uid, title, note, ct);
            return true;
        }

        if (olderSaveInFlight)
        {
            queue.Schedule(TimeSpan.FromMilliseconds(300), () => SaveAsync(a, "Older A", "Older note", true));
            delay.SetResult();
            await olderSaveStarted.Task.WaitAsync(ct);
            delay = NewSignal();
        }

        queue.Schedule(TimeSpan.FromMilliseconds(500), () => SaveAsync(a, "Latest A", "Latest note", false));
        Task<bool> drain = queue.FlushAsync();
        bool waitedForOlderSave = !drain.IsCompleted;
        releaseOlderSave.SetResult();
        Assert.True(await drain);
        if (!closingWindow)
        {
            // Selection reads the new model only after the edit boundary.
            Assert.Equal("B", (await fixture.Store.GetTaskAsync(b.Uid, ct))!.Summary);
        }

        await fixture.Store.DisposeAsync();
        await using var reopened = new SqliteTaskStore(fixture.DatabasePath, fixture.Clock);
        await reopened.InitializeAsync(ct);
        var savedA = (await reopened.GetTaskAsync(a.Uid, ct))!;
        var unchangedB = (await reopened.GetTaskAsync(b.Uid, ct))!;
        Assert.Equal("Latest A", savedA.Summary);
        Assert.Equal("Latest note", savedA.Description);
        Assert.Equal("B", unchangedB.Summary);
        Assert.Equal("B note", unchangedB.Description);
        if (olderSaveInFlight)
        {
            Assert.True(waitedForOlderSave);
            Assert.Equal(a.Sequence + 2, savedA.Sequence);
        }
    }

    [Fact]
    public async Task TypingAfterARepeatEditDoesNotReplaceItsSave()
    {
        var ct = TestContext.Current.CancellationToken;
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        var list = await fixture.Store.EnsureDefaultListAsync("Tasks", ct);
        var owner = await fixture.Store.CreateTaskAsync(new TodoTask { ListId = list.Id, Summary = "Before" }, ct);
        var delay = NewSignal();
        var queue = new DetailSaveQueue { DelayAsync = (_, token) => delay.Task.WaitAsync(token) };
        owner.RRule = "FREQ=WEEKLY;INTERVAL=2;BYDAY=TU";
        queue.Schedule(TimeSpan.FromMilliseconds(200), async () =>
        {
            await fixture.Store.UpdateTaskAsync(owner, ct);
            return true;
        }, writesWholeTask: true);

        owner.Summary = "Latest title";
        owner.Description = "Latest note";
        queue.Schedule(TimeSpan.FromMilliseconds(500), async () =>
        {
            await fixture.Store.SetTaskTextAsync(owner.Uid, owner.Summary, owner.Description, ct);
            return true;
        });
        Assert.True(await queue.FlushAsync());
        var saved = (await fixture.Store.GetTaskAsync(owner.Uid, ct))!;
        Assert.Equal("Latest title", saved.Summary);
        Assert.Equal("Latest note", saved.Description);
        Assert.Equal("FREQ=WEEKLY;INTERVAL=2;BYDAY=TU", saved.RRule);
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
