using Wintodo.Core.IO;

namespace Wintodo.Core.Tests;

public sealed class BoundedReadStreamTests
{
    [Fact]
    public async Task LongMaxValueBudgetDoesNotOverflow()
    {
        using var source = new MemoryStream([1, 2, 3]);
        await using var bounded = new BoundedReadStream(source, long.MaxValue, TestContext.Current.CancellationToken);
        using var destination = new MemoryStream();
        await bounded.CopyToAsync(destination, TestContext.Current.CancellationToken);
        Assert.Equal([1, 2, 3], destination.ToArray());
    }

    [Theory]
    [InlineData(0L, 0, false)]
    [InlineData(0L, 1, true)]
    [InlineData(4L, 4, false)]
    [InlineData(4L, 5, true)]
    public async Task BoundaryDistinguishesExactEofFromOverrun(long maximum, int size, bool rejects)
    {
        using var source = new MemoryStream(new byte[size]);
        await using var bounded = new BoundedReadStream(source, maximum, TestContext.Current.CancellationToken);
        using var destination = new MemoryStream();
        if (rejects)
            await Assert.ThrowsAsync<InvalidDataException>(() => bounded.CopyToAsync(destination, TestContext.Current.CancellationToken));
        else
        {
            await bounded.CopyToAsync(destination, TestContext.Current.CancellationToken);
            Assert.Equal(size, destination.Length);
        }
        Assert.True(destination.Length <= maximum);
    }

    [Fact]
    public async Task CancelledLifetimeStopsReadsWithoutRequiringTheConsumerToken()
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var source = new MemoryStream([1]);
        await using var bounded = new BoundedReadStream(source, 10, stop.Token);
        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            Assert.Equal(0, await bounded.ReadAsync(new byte[4], TestContext.Current.CancellationToken)));
        Assert.Equal(0, source.Position);
    }
}
