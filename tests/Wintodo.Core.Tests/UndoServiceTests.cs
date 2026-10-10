using Wintodo.Core.Services;

namespace Wintodo.Core.Tests;

public sealed class UndoServiceTests
{
    [Fact]
    public async Task Stack_IsBoundedAndRevertsNewestFirst()
    {
        var undo = new UndoService();
        var reverted = new List<int>();
        for (int i = 0; i < 25; i++)
        {
            int value = i;
            undo.Record(value.ToString(System.Globalization.CultureInfo.InvariantCulture), ["task"],
                _ => { reverted.Add(value); return Task.CompletedTask; });
        }
        Assert.Equal(20, undo.Count);
        Assert.Equal("24", undo.Label);
        for (int i = 0; i < 20; i++) Assert.True(await undo.UndoAsync(TestContext.Current.CancellationToken));
        Assert.Equal(Enumerable.Range(5, 20).Reverse(), reverted);
        Assert.False(await undo.UndoAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FailedRevert_RetainsEntryForRetry()
    {
        var undo = new UndoService();
        int attempts = 0;
        undo.Record("delete", ["a"], _ => ++attempts == 1
            ? Task.FromException(new IOException("offline")) : Task.CompletedTask);
        await Assert.ThrowsAsync<IOException>(() => undo.UndoAsync(TestContext.Current.CancellationToken));
        Assert.True(undo.CanUndo);
        Assert.True(await undo.UndoAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, undo.Count);
    }

    [Fact]
    public async Task Invalidation_DropsWholeAffectedBatchAndKeepsOtherEntries()
    {
        var undo = new UndoService();
        undo.Record("safe", ["c"], _ => Task.CompletedTask);
        undo.Record("batch", ["a", "b"], _ => Task.CompletedTask);
        undo.Invalidate(["b"]);
        Assert.Equal("safe", undo.Label);
        Assert.True(await undo.UndoAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ConcurrentUndo_DoesNotRevertSameEntryTwice()
    {
        var undo = new UndoService();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        undo.Record("entry", [], _ => release.Task);
        Task<bool> first = undo.UndoAsync(TestContext.Current.CancellationToken);
        Assert.False(await undo.UndoAsync(TestContext.Current.CancellationToken));
        release.SetResult();
        Assert.True(await first);
    }
}
