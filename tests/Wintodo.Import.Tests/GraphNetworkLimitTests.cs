using System.Net;
using System.Text;
using Wintodo.Core.Import;
using Wintodo.Import.MicrosoftTodo;

namespace Wintodo.Import.Tests;

public sealed class GraphNetworkLimitTests
{
    private const int FileLimit = 25 * 1024 * 1024;
    private const int JsonLimit = 16 * 1024 * 1024;

    [Theory]
    [InlineData(null)]
    [InlineData(1L)]
    [InlineData(26214401L)]
    public async Task OversizedRawFileStopsBeforeWritingBeyondLimit(long? declared)
    {
        using var source = new GeneratedStream(FileLimit + 128 * 1024, (byte)'x');
        using var http = new HttpClient(new ScriptedHandler(_ => Response(source, declared)));
        using var target = new CountingStream();
        using var client = new GraphTodoClient(http, "synthetic-token");
        await Assert.ThrowsAnyAsync<InvalidDataException>(() => client.CopyAttachmentContentAsync(
            "l", "t", "a", target, cancellationToken: TestContext.Current.CancellationToken));
        Assert.True(source.BytesRead <= FileLimit + 1L);
        Assert.True(target.BytesWritten <= FileLimit);
        if (declared > FileLimit) Assert.Equal(0, source.BytesRead);
    }

    [Fact]
    public async Task ExactFileLimitRemainsSupported()
    {
        using var source = new GeneratedStream(FileLimit, 42);
        using var http = new HttpClient(new ScriptedHandler(_ => Response(source, FileLimit)));
        using var target = new CountingStream();
        using var client = new GraphTodoClient(http, "synthetic-token");
        Assert.Equal(FileLimit, await client.CopyAttachmentContentAsync("l", "t", "a", target,
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(FileLimit, target.BytesWritten);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1L)]
    [InlineData(16777217L)]
    public async Task JsonWhitespaceCannotBypassTheBodyLimit(long? declared)
    {
        using var source = new GeneratedStream(JsonLimit + 64 * 1024, (byte)' ', "{\"id\":\"synthetic\"}");
        using var http = new HttpClient(new ScriptedHandler(_ => Response(source, declared)));
        using var client = new GraphTodoClient(http, "synthetic-token");
        await Assert.ThrowsAnyAsync<InvalidDataException>(() => client.GetMeAsync(TestContext.Current.CancellationToken));
        Assert.True(source.BytesRead <= JsonLimit + 1L);
        if (declared > JsonLimit) Assert.Equal(0, source.BytesRead);
    }

    [Fact]
    public async Task DecodedFallbackHasAnIndependentOutputLimit()
    {
        // Base64 input remains under the 40 MiB envelope budget.
        using var source = new GeneratedStream(((FileLimit + 3L) / 3 * 4), (byte)'A', "{\"contentBytes\":\"", "\"}");
        using var http = new HttpClient(new ScriptedHandler(
            _ => ScriptedHandler.Json(HttpStatusCode.NotFound, "{}"), _ => Response(source, null)));
        using var target = new CountingStream();
        using var client = new GraphTodoClient(http, "synthetic-token");
        await Assert.ThrowsAnyAsync<InvalidDataException>(() => client.CopyAttachmentContentAsync("l", "t", "a", target,
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.True(target.BytesWritten <= FileLimit);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(500)]
    public async Task OversizedErrorsKeepStatusWithoutReadingTheWholeBody(int status)
    {
        using var source = new GeneratedStream(1024 * 1024, (byte)' ', "{}");
        using var http = new HttpClient(new ScriptedHandler(_ => Response(source, null, (HttpStatusCode)status)));
        using var client = new GraphTodoClient(http, "synthetic-token");
        GraphRequestException exception = await Assert.ThrowsAnyAsync<GraphRequestException>(
            () => client.GetMeAsync(TestContext.Current.CancellationToken));
        Assert.Equal((HttpStatusCode)status, exception.StatusCode);
        Assert.Equal(status is 401 or 403, exception is GraphAuthorizationException);
        Assert.True(source.BytesRead <= 64 * 1024 + 1);
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("cancel")]
    [InlineData("truncated")]
    public async Task StagingFailureRemovesOnlyTheFileCreatedByThisAttempt(string failure)
    {
        await using var temp = await TempStore.CreateAsync();
        string stage = Path.Combine(temp.Directory, "stage");
        Directory.CreateDirectory(stage);
        string sentinel = Path.Combine(stage, "existing");
        await File.WriteAllTextAsync(sentinel, "preserve", TestContext.Current.CancellationToken);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var source = new GeneratedStream(100, 42);
        var handler = failure == "malformed"
            ? new ScriptedHandler(_ => ScriptedHandler.Json(HttpStatusCode.NotFound, "{}"),
                _ => ScriptedHandler.Json(HttpStatusCode.OK, "{\"contentBytes\":\"AQIDinvalid!\"}"))
            : new ScriptedHandler(_ => Response(source, failure == "truncated" ? 200 : null));
        using var http = new HttpClient(handler);
        using var client = new GraphTodoClient(http, "synthetic-token");
        ImportSnapshot snapshot = Snapshot(1, 100);
        var progress = new ImmediateProgress(value => { if (failure == "cancel" && value.CompletedBytes > 0) cancellation.Cancel(); });
        await Assert.ThrowsAnyAsync<Exception>(() => new MicrosoftTodoReader(client).DownloadAttachmentsAsync(
            snapshot, stage, new HashSet<string> { "a0" }, progress, cancellation.Token));
        Assert.Null(snapshot.Lists[0].Tasks[0].Attachments[0].StagedPath);
        Assert.Equal([sentinel], Directory.GetFiles(stage));
        Assert.Equal("preserve", await File.ReadAllTextAsync(sentinel, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FailedCreateNewPreservesAnExistingNumberedFile()
    {
        await using var temp = await TempStore.CreateAsync();
        string sentinel = Path.Combine(temp.Directory, "0");
        await File.WriteAllTextAsync(sentinel, "preserve", TestContext.Current.CancellationToken);
        using var http = new HttpClient(new ScriptedHandler(_ => ScriptedHandler.Json(HttpStatusCode.OK, "{}")));
        using var client = new GraphTodoClient(http, "synthetic-token");
        await Assert.ThrowsAnyAsync<IOException>(() => new MicrosoftTodoReader(client).DownloadAttachmentsAsync(
            Snapshot(1, 10), temp.Directory, new HashSet<string> { "a0" }, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("preserve", await File.ReadAllTextAsync(sentinel, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeclaredImportBudgetRejectsBeforeCreatingFilesOrMakingRequests()
    {
        await using var temp = await TempStore.CreateAsync();
        var handler = new ScriptedHandler(_ => ScriptedHandler.Json(HttpStatusCode.OK, "{}"));
        using var http = new HttpClient(handler);
        using var client = new GraphTodoClient(http, "synthetic-token");
        ImportSnapshot snapshot = Snapshot(11, FileLimit);
        await Assert.ThrowsAnyAsync<InvalidDataException>(() => new MicrosoftTodoReader(client).DownloadAttachmentsAsync(
            snapshot, Path.Combine(temp.Directory, "stage"), snapshot.Lists[0].Tasks[0].Attachments.Select(a => a.ExternalId).ToHashSet(),
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Empty(handler.Requests);
        Assert.False(Directory.Exists(Path.Combine(temp.Directory, "stage")));
    }

    internal static ImportSnapshot Snapshot(int count, long declared)
    {
        var snapshot = new ImportSnapshot();
        var list = new ImportList();
        var task = new ImportTask();
        for (int i = 0; i < count; i++) task.Attachments.Add(new ImportAttachment
            { ExternalId = "a" + i, SourcePath = "l\nt\na" + i, SizeBytes = declared });
        list.Tasks.Add(task);
        snapshot.Lists.Add(list);
        return snapshot;
    }

    internal static HttpResponseMessage Response(Stream stream, long? length, HttpStatusCode status = HttpStatusCode.OK)
    {
        var response = new HttpResponseMessage(status) { Content = new StreamContent(stream) };
        response.Content.Headers.ContentLength = length;
        return response;
    }

    private sealed class ImmediateProgress(Action<MicrosoftTodoProgress> action) : IProgress<MicrosoftTodoProgress>
    {
        public void Report(MicrosoftTodoProgress value) => action(value);
    }

    internal sealed class CountingStream : Stream
    {
        internal long BytesWritten { get; private set; }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => BytesWritten;
        public override long Position { get => BytesWritten; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => BytesWritten += count;
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BytesWritten += buffer.Length;
            return ValueTask.CompletedTask;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    internal sealed class GeneratedStream(long repeatLength, byte repeated, string prefix = "", string suffix = "") : Stream
    {
        private readonly byte[] _prefix = Encoding.UTF8.GetBytes(prefix);
        private readonly byte[] _suffix = Encoding.UTF8.GetBytes(suffix);
        internal long BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => BytesRead; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            int count = (int)Math.Min(buffer.Length, _prefix.Length + repeatLength + _suffix.Length - BytesRead);
            buffer[..count].Fill(repeated);
            int prefixCount = (int)Math.Min(count, Math.Max(0, _prefix.Length - BytesRead));
            if (prefixCount > 0) _prefix.AsSpan((int)BytesRead, prefixCount).CopyTo(buffer);
            long suffixStart = _prefix.Length + repeatLength;
            long suffixOffset = Math.Max(0, suffixStart - BytesRead);
            if (suffixOffset < count)
                _suffix.AsSpan((int)Math.Max(0, BytesRead - suffixStart), count - (int)suffixOffset).CopyTo(buffer[(int)suffixOffset..]);
            BytesRead += count;
            return count;
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Read(buffer.Span));
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
