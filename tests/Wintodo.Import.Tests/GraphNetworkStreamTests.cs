using System.Net;
using System.Net.Sockets;
using System.Text;
using Wintodo.Import.MicrosoftTodo;

namespace Wintodo.Import.Tests;

public sealed class GraphNetworkStreamTests
{
    [Fact]
    public async Task ActualImportBudgetStopsMisleadingMetadataAndPreservesCompleteDownloads()
    {
        await using var temp = await TempStore.CreateAsync();
        using var http = new HttpClient(new ScriptedHandler(_ => GraphNetworkLimitTests.Response(
            new GraphNetworkLimitTests.GeneratedStream(25 * 1024 * 1024, 42), null)));
        using var client = new GraphTodoClient(http, "synthetic-token");
        var snapshot = GraphNetworkLimitTests.Snapshot(11, 0);
        string stage = Path.Combine(temp.Directory, "stage");
        await Assert.ThrowsAnyAsync<InvalidDataException>(() => new MicrosoftTodoReader(client).DownloadAttachmentsAsync(
            snapshot, stage, snapshot.Lists[0].Tasks[0].Attachments.Select(a => a.ExternalId).ToHashSet(),
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(10, Directory.GetFiles(stage).Length);
        Assert.Equal(250L * 1024 * 1024, Directory.GetFiles(stage).Sum(path => new FileInfo(path).Length));
        Assert.All(snapshot.Lists[0].Tasks[0].Attachments.Take(10), attachment => Assert.NotNull(attachment.StagedPath));
        Assert.Null(snapshot.Lists[0].Tasks[0].Attachments[^1].StagedPath);
        Assert.False(File.Exists(Path.Combine(stage, "10")));
    }

    [Fact]
    public async Task DestinationIoFailureStopsFurtherReads()
    {
        using var source = new GraphNetworkLimitTests.GeneratedStream(1024 * 1024, 42);
        using var http = new HttpClient(new ScriptedHandler(_ => GraphNetworkLimitTests.Response(source, null)));
        using var client = new GraphTodoClient(http, "synthetic-token");
        using var destination = new FailingDestination();
        await Assert.ThrowsAnyAsync<IOException>(() => client.CopyAttachmentContentAsync("l", "t", "a", destination,
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.InRange(source.BytesRead, 1, 64 * 1024);
    }

    [Theory]
    [InlineData("raw")]
    [InlineData("json")]
    [InlineData("fallback")]
    public async Task ProductionHandlerBodyStillHasADeadlineAfterHeaders(string route)
    {
        await using var server = new BodyServer(async (stream, request, token) =>
        {
            if (route == "fallback" && request.Contains("/$value", StringComparison.Ordinal))
            {
                await WriteAsync(stream, "HTTP/1.1 404 Not Found\r\nContent-Length: 2\r\nConnection: close\r\n\r\n{}", token);
                return;
            }
            string prefix = route == "fallback" ? "{\"contentBytes\":\"" : route == "json" ? "{\"id\":\"" : "abc";
            await WriteAsync(stream, "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\nConnection: close\r\n\r\n"
                + Encoding.UTF8.GetByteCount(prefix).ToString("X") + "\r\n" + prefix + "\r\n", token);
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        });
        using HttpClient http = GraphTodoClient.CreateHttpClient();
        http.Timeout = TimeSpan.FromMilliseconds(250);
        using var client = new GraphTodoClient(http, "synthetic-token", server.Origin);
        using var destination = new MemoryStream();
        Task operation = route == "json" ? client.GetMeAsync(TestContext.Current.CancellationToken)
            : client.CopyAttachmentContentAsync("l", "t", "a", destination,
                cancellationToken: TestContext.Current.CancellationToken);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.True(server.RequestCount >= 1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProductionHandlerBoundsChunkedAndUnknownLengthErrorBodies(bool chunked)
    {
        await using var server = new BodyServer(async (stream, _, token) =>
        {
            await WriteAsync(stream, "HTTP/1.1 401 Unauthorized\r\nConnection: close\r\n"
                + (chunked ? "Transfer-Encoding: chunked\r\n" : "") + "\r\n", token);
            byte[] bytes = Encoding.UTF8.GetBytes("{}" + new string(' ', 128 * 1024));
            if (chunked) await WriteAsync(stream, bytes.Length.ToString("X") + "\r\n", token);
            await stream.WriteAsync(bytes, token);
            if (chunked) await WriteAsync(stream, "\r\n0\r\n\r\n", token);
        });
        using HttpClient http = GraphTodoClient.CreateHttpClient();
        using var client = new GraphTodoClient(http, "synthetic-token", server.Origin);
        GraphAuthorizationException exception = await Assert.ThrowsAsync<GraphAuthorizationException>(
            () => client.GetMeAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        Assert.Null(exception.Code);
    }

    [Fact]
    public async Task ProductionHandlerTruncatedFileCleansItsPartialStagingFile()
    {
        await using var temp = await TempStore.CreateAsync();
        await using var server = new BodyServer((stream, _, token) =>
            WriteAsync(stream, "HTTP/1.1 200 OK\r\nContent-Length: 100\r\nConnection: close\r\n\r\npartial", token));
        using HttpClient http = GraphTodoClient.CreateHttpClient();
        using var client = new GraphTodoClient(http, "synthetic-token", server.Origin);
        string stage = Path.Combine(temp.Directory, "stage");
        await Assert.ThrowsAnyAsync<IOException>(() => new MicrosoftTodoReader(client).DownloadAttachmentsAsync(
            GraphNetworkLimitTests.Snapshot(1, 100), stage, new HashSet<string> { "a0" },
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Empty(Directory.GetFiles(stage));
    }

    [Fact]
    public async Task DuplicateDecodedFieldsShareOneOutputBudget()
    {
        byte[] payload = new byte[14 * 1024 * 1024];
        string encoded = Convert.ToBase64String(payload);
        using var http = new HttpClient(new ScriptedHandler(
            _ => ScriptedHandler.Json(HttpStatusCode.NotFound, "{}"),
            _ => ScriptedHandler.Json(HttpStatusCode.OK, "{\"contentBytes\":\"" + encoded + "\",\"contentBytes\":\"" + encoded + "\"}")));
        using var client = new GraphTodoClient(http, "synthetic-token");
        using var destination = new GraphNetworkLimitTests.CountingStream();
        await Assert.ThrowsAnyAsync<InvalidDataException>(() => client.CopyAttachmentContentAsync("l", "t", "a", destination,
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.True(destination.BytesWritten <= 25 * 1024 * 1024);
    }

    [Theory]
    [InlineData("{\"contentBytes\":null,\"contentBytes\":\"AQID\"}")]
    [InlineData("{\"contentBytes\":null,\"contentBytes\":null}")]
    public async Task DuplicateNullContentFieldsAreAlsoRejected(string json)
    {
        using var http = new HttpClient(new ScriptedHandler(
            _ => ScriptedHandler.Json(HttpStatusCode.NotFound, "{}"), _ => ScriptedHandler.Json(HttpStatusCode.OK, json)));
        using var client = new GraphTodoClient(http, "synthetic-token");
        using var destination = new MemoryStream();
        await Assert.ThrowsAnyAsync<InvalidDataException>(() => client.CopyAttachmentContentAsync("l", "t", "a", destination,
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(0, destination.Length);
    }

    [Theory]
    [InlineData("{\"contentBytes\":\"AQID\",BROKEN")]
    [InlineData("{\"contentBytes\":\"AQID\"}")]
    public async Task FallbackReadsAndValidatesTheWholeEnvelope(string json)
    {
        using var http = new HttpClient(new ScriptedHandler(
            _ => ScriptedHandler.Json(HttpStatusCode.NotFound, "{}"), _ => ScriptedHandler.Json(HttpStatusCode.OK, json)));
        using var client = new GraphTodoClient(http, "synthetic-token");
        using var destination = new MemoryStream();
        if (json.EndsWith('}'))
            Assert.Equal(3, await client.CopyAttachmentContentAsync("l", "t", "a", destination, cancellationToken: TestContext.Current.CancellationToken));
        else
            await Assert.ThrowsAnyAsync<Exception>(() => client.CopyAttachmentContentAsync("l", "t", "a", destination, cancellationToken: TestContext.Current.CancellationToken));
    }

    private static async Task WriteAsync(Stream stream, string text, CancellationToken token) =>
        await stream.WriteAsync(Encoding.UTF8.GetBytes(text), token);

    private sealed class FailingDestination : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new IOException("Synthetic disk-full failure.");
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromException(new IOException("Synthetic disk-full failure."));
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    private sealed class BodyServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        private readonly Task _run;
        private readonly List<Task> _connections = [];
        private int _requests;
        internal int RequestCount => _requests;
        internal Uri Origin { get; }

        internal BodyServer(Func<Stream, string, CancellationToken, Task> write)
        {
            _listener.Start();
            Origin = new Uri("http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port + "/v1.0/");
            _run = RunAsync(write);
        }

        private async Task RunAsync(Func<Stream, string, CancellationToken, Task> write)
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    TcpClient client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    _connections.Add(ServeAsync(client, write));
                }
            }
            catch (OperationCanceledException) { }
        }

        private async Task ServeAsync(TcpClient client, Func<Stream, string, CancellationToken, Task> write)
        {
            using (client)
            {
                try
                {
                    using NetworkStream stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                    string request = await reader.ReadLineAsync(_stop.Token) ?? "";
                    while (await reader.ReadLineAsync(_stop.Token) is { Length: > 0 }) { }
                    Interlocked.Increment(ref _requests);
                    await write(stream, request, _stop.Token);
                }
                catch (Exception exception) when (exception is IOException or OperationCanceledException) { }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Stop();
            await _run;
            await Task.WhenAll(_connections);
            _stop.Dispose();
        }
    }
}
