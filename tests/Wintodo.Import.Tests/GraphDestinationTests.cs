using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Wintodo.Import.MicrosoftTodo;

namespace Wintodo.Import.Tests;

public sealed class GraphDestinationTests
{
    private const string SyntheticToken = "synthetic-origin-test-token";

    [Theory]
    [InlineData("//foreign.example/v1.0/page")]
    [InlineData("https://foreign.example/v1.0/page")]
    [InlineData("http://graph.microsoft.com/v1.0/page")]
    [InlineData("https://graph.microsoft.com:444/v1.0/page")]
    [InlineData("https://user:password@graph.microsoft.com/v1.0/page")]
    [InlineData("https://graph.microsoft.com/v1.0/page#fragment")]
    [InlineData("/v1.0/page#fragment")]
    [InlineData("//user@graph.microsoft.com/v1.0/page")]
    [InlineData("//graph.microsoft.com:444/v1.0/page")]
    [InlineData("https://graph.microsoft.com.foreign.example/v1.0/page")]
    [InlineData("ftp://graph.microsoft.com/v1.0/page")]
    public async Task UnapprovedPageNeverReachesAuthenticatedHandler(string nextLink)
    {
        var handler = PagingHandler(nextLink);
        using var http = new HttpClient(handler);
        var client = new GraphTodoClient(http, SyntheticToken);

        GraphRequestException exception = await Assert.ThrowsAsync<GraphRequestException>(
            () => client.GetListsAsync(TestContext.Current.CancellationToken));

        Assert.Equal("unexpectedHost", exception.Code);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(GraphTodoClient.DefaultBaseUri.Host, request.Destination.Host);
        Assert.Equal("Bearer " + SyntheticToken, request.Authorization);
        Assert.Equal(HttpMethod.Get, request.Method);
    }

    [Theory]
    [InlineData("me/todo/lists/delta?$skiptoken=next")]
    [InlineData("/v1.0/me/todo/lists/delta?$skiptoken=next")]
    [InlineData("//graph.microsoft.com/v1.0/me/todo/lists/delta?$skiptoken=next")]
    [InlineData("https://GRAPH.MICROSOFT.COM:443/v1.0/me/todo/lists/delta?$skiptoken=next")]
    [InlineData("https://graph.microsoft.com/v1.0/me/todo/lists/delta?$skiptoken=next")]
    public async Task SameOriginPagingPreservesAuthorizationAndGetOnlyRequests(string nextLink)
    {
        var handler = PagingHandler(nextLink);
        using var http = new HttpClient(handler);
        var client = new GraphTodoClient(http, SyntheticToken);

        List<GraphTodoList> lists = await client.GetListsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["L1", "L2"], lists.Select(list => list.Id));
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(new Uri(GraphTodoClient.DefaultBaseUri, nextLink), handler.Requests[1].Destination);
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal("graph.microsoft.com", request.Destination.IdnHost, ignoreCase: true);
            Assert.Equal("https", request.Destination.Scheme);
            Assert.Equal(443, request.Destination.Port);
            Assert.Equal("Bearer " + SyntheticToken, request.Authorization);
            Assert.Equal(HttpMethod.Get, request.Method);
        });
    }

    [Theory]
    [InlineData("me/todo/lists/")]
    [InlineData("ftp://graph.microsoft.com/v1.0/")]
    [InlineData("https://user@graph.microsoft.com/v1.0/")]
    [InlineData("https://graph.microsoft.com/v1.0/#fragment")]
    public void UnsupportedBaseUriIsRejectedBeforeAnyRequest(string baseUri)
    {
        var handler = PagingHandler("me/todo/lists/delta?$skiptoken=next");
        using var http = new HttpClient(handler);

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new GraphTodoClient(http, SyntheticToken, new Uri(baseUri, UriKind.RelativeOrAbsolute)));

        Assert.Equal("baseUri", exception.ParamName);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.MovedPermanently, false)]
    [InlineData(HttpStatusCode.Found, false)]
    [InlineData(HttpStatusCode.SeeOther, false)]
    [InlineData(HttpStatusCode.TemporaryRedirect, false)]
    [InlineData(HttpStatusCode.PermanentRedirect, false)]
    [InlineData(HttpStatusCode.MovedPermanently, true)]
    [InlineData(HttpStatusCode.Found, true)]
    [InlineData(HttpStatusCode.SeeOther, true)]
    [InlineData(HttpStatusCode.TemporaryRedirect, true)]
    [InlineData(HttpStatusCode.PermanentRedirect, true)]
    public async Task ProductionHandlerRejectsRedirectsWithoutFollowingLocation(HttpStatusCode status, bool sameOrigin)
    {
        await using var foreign = new LoopbackServer(() => HttpResponse(HttpStatusCode.OK));
        string location = sameOrigin ? "/v1.0/redirected" : new Uri(foreign.BaseUri, "redirected").AbsoluteUri;
        await using var approved = new LoopbackServer(() => HttpResponse(status, location));
        using HttpClient http = GraphTodoClient.CreateHttpClient();
        var client = new GraphTodoClient(http, SyntheticToken, approved.BaseUri);

        GraphRequestException exception = await Assert.ThrowsAsync<GraphRequestException>(
            () => client.GetMeAsync(TestContext.Current.CancellationToken));

        Assert.Equal(status, exception.StatusCode);
        var request = Assert.Single(approved.Requests);
        Assert.StartsWith("GET /v1.0/me?", request.Target);
        Assert.Equal("Bearer " + SyntheticToken, request.Authorization);
        Assert.Empty(foreign.Requests);
    }

    [Fact]
    public async Task ProductionHandlerReadsAnApprovedOriginNormally()
    {
        await using var approved = new LoopbackServer(() => HttpResponse(HttpStatusCode.OK));
        using HttpClient http = GraphTodoClient.CreateHttpClient();
        Assert.Equal(TimeSpan.FromSeconds(100), http.Timeout);
        var client = new GraphTodoClient(http, SyntheticToken, approved.BaseUri);

        GraphUser user = await client.GetMeAsync(TestContext.Current.CancellationToken);

        Assert.Equal("approved-user", user.Id);
        var request = Assert.Single(approved.Requests);
        Assert.StartsWith("GET /v1.0/me?", request.Target);
        Assert.Equal("Bearer " + SyntheticToken, request.Authorization);
    }

    private static string HttpResponse(HttpStatusCode status, string? location = null)
    {
        const string body = "{\"id\":\"approved-user\"}";
        string locationHeader = location is null ? string.Empty : "Location: " + location + "\r\n";
        return $"HTTP/1.1 {(int)status} Response\r\n{locationHeader}" +
            $"Content-Type: application/json\r\nContent-Length: {Encoding.ASCII.GetByteCount(body)}\r\n" +
            $"Connection: close\r\n\r\n{body}";
    }

    private static RecordingHandler PagingHandler(string nextLink)
    {
        string encodedLink = JsonEncodedText.Encode(nextLink).ToString();
        return new RecordingHandler(
            _ => ScriptedHandler.Json(HttpStatusCode.OK,
                $$"""{"@odata.nextLink":"{{encodedLink}}","value":[{"id":"L1"}]}"""),
            _ => ScriptedHandler.Json(HttpStatusCode.OK, """{"value":[{"id":"L2"}]}"""));
    }

    private sealed class RecordingHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        internal List<(HttpMethod Method, Uri Destination, string? Authorization)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add((request.Method, request.RequestUri!, request.Headers.Authorization?.ToString()));
            return Task.FromResult(responses[Math.Min(Requests.Count - 1, responses.Length - 1)](request));
        }
    }

    private sealed class LoopbackServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _lifetime =
            CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        private readonly Func<string> _response;
        private readonly Task _serve;

        internal Uri BaseUri { get; }

        internal ConcurrentQueue<(string Target, string? Authorization)> Requests { get; } = new();

        internal LoopbackServer(Func<string> response)
        {
            _response = response;
            _lifetime.CancelAfter(TimeSpan.FromSeconds(10));
            _listener.Start();
            BaseUri = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/v1.0/");
            _serve = ServeAsync();
        }

        private async Task ServeAsync()
        {
            try
            {
                while (!_lifetime.IsCancellationRequested)
                {
                    using TcpClient client = await _listener.AcceptTcpClientAsync(_lifetime.Token);
                    await using NetworkStream stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                    string target = await reader.ReadLineAsync(_lifetime.Token) ?? string.Empty;
                    string? authorization = null;
                    string? header;
                    while (!string.IsNullOrEmpty(header = await reader.ReadLineAsync(_lifetime.Token)))
                    {
                        if (header.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase))
                        {
                            authorization = header["Authorization:".Length..].Trim();
                        }
                    }
                    Requests.Enqueue((target, authorization));
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(_response()), _lifetime.Token);
                }
            }
            catch (Exception exception) when (_lifetime.IsCancellationRequested
                && exception is OperationCanceledException or SocketException or ObjectDisposedException)
            {
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _lifetime.CancelAsync();
            _listener.Stop();
            try
            {
                await _serve;
            }
            finally
            {
                _lifetime.Dispose();
            }
        }
    }
}
