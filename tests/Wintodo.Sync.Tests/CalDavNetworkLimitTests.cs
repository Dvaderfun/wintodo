using System.Net;
using System.Text;
using System.Xml;
using Wintodo.Core.Models;
using Wintodo.Sync;

namespace Wintodo.Sync.Tests;

public sealed class CalDavNetworkLimitTests
{
    private const int CalendarLimit = 32 * 1024 * 1024;
    private const int XmlLimit = 16 * 1024 * 1024;

    [Theory]
    [InlineData(null)]
    [InlineData(1L)]
    [InlineData(33554433L)]
    public async Task CalendarRejectsBeforeReadingTheEntireOversizedBody(long? declared)
    {
        using var source = new RepeatingStream(CalendarLimit + 128 * 1024);
        using var http = new HttpClient(new Handler(_ => Response(source, declared)));
        var client = new CalDavClient(http);
        await Assert.ThrowsAnyAsync<Exception>(() => client.GetTaskAsync(Account(), "synthetic",
            new Uri("https://dav.example/task.ics"), TestContext.Current.CancellationToken));
        Assert.True(source.BytesRead <= CalendarLimit + 1L);
        if (declared > CalendarLimit) Assert.Equal(0, source.BytesRead);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1L)]
    [InlineData(16777217L)]
    public async Task XmlRejectsBeforeReadingTheEntireOversizedBody(long? declared)
    {
        using var source = new RepeatingStream(XmlLimit + 128 * 1024, "<d:multistatus xmlns:d=\"DAV:\"/>");
        using var http = new HttpClient(new Handler(_ => Response(source, declared, HttpStatusCode.MultiStatus)));
        var client = new CalDavClient(http);
        await Assert.ThrowsAnyAsync<Exception>(() => client.GetCollectionsAsync(Account(), "synthetic",
            TestContext.Current.CancellationToken));
        Assert.True(source.BytesRead <= XmlLimit + 1L);
        if (declared > XmlLimit) Assert.Equal(0, source.BytesRead);
    }

    [Fact]
    public async Task ExactCalendarLimitRemainsSupported()
    {
        using var source = new RepeatingStream(CalendarLimit);
        using var http = new HttpClient(new Handler(_ => Response(source, CalendarLimit)));
        CalDavContent content = await new CalDavClient(http).GetTaskAsync(Account(), "synthetic",
            new Uri("https://dav.example/task.ics"), TestContext.Current.CancellationToken);
        Assert.Equal(CalendarLimit, content.Content.Length);
    }

    [Fact]
    public async Task ShortCalendarBodyCannotBeAcceptedAsComplete()
    {
        using var source = new RepeatingStream(10);
        using var http = new HttpClient(new Handler(_ => Response(source, 20)));
        await Assert.ThrowsAnyAsync<Exception>(() => new CalDavClient(http).GetTaskAsync(Account(), "synthetic",
            new Uri("https://dav.example/task.ics"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task XmlStillProhibitsDtdEntities()
    {
        using var source = new MemoryStream(Encoding.UTF8.GetBytes("<!DOCTYPE x [<!ENTITY e 'private'>]><x>&e;</x>"));
        using var http = new HttpClient(new Handler(_ => Response(source, null, HttpStatusCode.MultiStatus)));
        await Assert.ThrowsAnyAsync<XmlException>(() => new CalDavClient(http).GetCollectionsAsync(Account(), "synthetic",
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EncodedContentIsRejectedBeforeReading()
    {
        using var source = new RepeatingStream(1024);
        using var http = new HttpClient(new Handler(_ =>
        {
            HttpResponseMessage response = Response(source, null);
            response.Content.Headers.ContentEncoding.Add("gzip");
            return response;
        }));
        await Assert.ThrowsAnyAsync<Exception>(() => new CalDavClient(http).GetTaskAsync(Account(), "synthetic",
            new Uri("https://dav.example/task.ics"), TestContext.Current.CancellationToken));
        Assert.Equal(0, source.BytesRead);
    }

    private static Account Account() => new() { ServerUrl = "https://dav.example/", Username = "synthetic", HomeSetUrl = "https://dav.example/cal/" };

    private static HttpResponseMessage Response(Stream stream, long? length, HttpStatusCode status = HttpStatusCode.OK)
    {
        var response = new HttpResponseMessage(status) { Content = new StreamContent(stream) };
        response.Content.Headers.ContentLength = length;
        return response;
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request));
    }

    private sealed class RepeatingStream(long size, string prefix = "") : Stream
    {
        private readonly byte[] _prefix = Encoding.UTF8.GetBytes(prefix);
        internal long BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => BytesRead; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = (int)Math.Min(count, _prefix.Length + size - BytesRead);
            buffer.AsSpan(offset, read).Fill((byte)' ');
            int prefixCount = (int)Math.Min(read, Math.Max(0, _prefix.Length - BytesRead));
            if (prefixCount > 0) _prefix.AsSpan((int)BytesRead, prefixCount).CopyTo(buffer.AsSpan(offset));
            BytesRead += read;
            return read;
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] temporary = new byte[buffer.Length];
            int read = Read(temporary, 0, temporary.Length);
            temporary.AsSpan(0, read).CopyTo(buffer.Span);
            return ValueTask.FromResult(read);
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
