using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Wintodo.Core.Models;
using Wintodo.Sync;

namespace Wintodo.Sync.Tests;

public sealed class CalDavClientTests
{
    [Fact]
    public async Task DiscoveryAndCollectionEnumeration_ResolveRelativeHrefsAndRequireVtodo()
    {
        var requests = new List<CapturedRequest>();
        using var handler = new ScriptedHandler(
            request => Respond(request, requests, """
                <?xml version="1.0" encoding="utf-8"?>
                <d:multistatus xmlns:d="DAV:">
                  <d:response><d:href>/dav/</d:href><d:propstat><d:prop>
                    <d:current-user-principal><d:href>/principals/alice/</d:href></d:current-user-principal>
                  </d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>
                </d:multistatus>
                """),
            request => Respond(request, requests, """
                <d:multistatus xmlns:d="DAV:" xmlns:c="urn:ietf:params:xml:ns:caldav">
                  <d:response><d:href>/principals/alice/</d:href><d:propstat><d:prop>
                    <c:calendar-home-set><d:href>/calendars/alice/</d:href></c:calendar-home-set>
                  </d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>
                </d:multistatus>
                """),
            request => Respond(request, requests, """
                <d:multistatus xmlns:d="DAV:" xmlns:c="urn:ietf:params:xml:ns:caldav" xmlns:cs="http://calendarserver.org/ns/">
                  <d:response><d:href>/calendars/alice/tasks/</d:href><d:propstat><d:prop>
                    <d:resourcetype><d:collection/><c:calendar/></d:resourcetype>
                    <d:displayname>Tasks</d:displayname><d:sync-token>token-1</d:sync-token><cs:getctag>ctag-1</cs:getctag>
                    <c:supported-calendar-component-set><c:comp name="VTODO"/></c:supported-calendar-component-set>
                  </d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>
                  <d:response><d:href>/calendars/alice/events/</d:href><d:propstat><d:prop>
                    <d:resourcetype><d:collection/><c:calendar/></d:resourcetype>
                    <d:displayname>Events</d:displayname>
                    <c:supported-calendar-component-set><c:comp name="VEVENT"/></c:supported-calendar-component-set>
                  </d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>
                </d:multistatus>
                """));
        using var httpClient = new HttpClient(handler);
        var client = new CalDavClient(httpClient);
        var account = CreateAccount();

        CalDavDiscovery discovery = await client.DiscoverAsync(account, "secret");
        account.PrincipalUrl = discovery.PrincipalUrl.AbsoluteUri;
        account.HomeSetUrl = discovery.HomeSetUrl.AbsoluteUri;
        IReadOnlyList<CalDavCollection> collections =
            await client.GetCollectionsAsync(account, "secret");

        CalDavCollection collection = Assert.Single(collections);
        Assert.Equal("https://dav.example/calendars/alice/tasks/", collection.Url.AbsoluteUri);
        Assert.Equal("Tasks", collection.DisplayName);
        Assert.Equal("token-1", collection.SyncToken);
        Assert.Equal("ctag-1", collection.CTag);
        Assert.All(requests, request => Assert.Equal("Basic", request.Authorization?.Scheme));
        Assert.Equal(["0", "0", "1"], requests.Select(request => request.Depth));
    }

    [Fact]
    public async Task SyncCollectionAndConditionalWrites_UseTokensEtagsAndDeletedResponses()
    {
        var requests = new List<CapturedRequest>();
        using var handler = new ScriptedHandler(
            request => Respond(request, requests, """
                <d:multistatus xmlns:d="DAV:">
                  <d:response><d:href>/cal/tasks/one.ics</d:href><d:propstat><d:prop><d:getetag>"v2"</d:getetag></d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>
                  <d:response><d:href>/cal/tasks/gone.ics</d:href><d:status>HTTP/1.1 404 Not Found</d:status></d:response>
                  <d:sync-token>token-2</d:sync-token>
                </d:multistatus>
                """),
            request => Respond(request, requests, string.Empty, HttpStatusCode.NoContent, "\"v3\""),
            request => Respond(request, requests, string.Empty, HttpStatusCode.NoContent));
        using var httpClient = new HttpClient(handler);
        var client = new CalDavClient(httpClient);
        Account account = CreateAccount();
        var list = new TodoList
        {
            RemoteUrl = "https://dav.example/cal/tasks/",
            SyncToken = "token-1",
        };

        CalDavDelta delta = await client.GetCollectionDeltaAsync(account, "secret", list);
        CalDavWriteResult put = await client.PutTaskAsync(
            account,
            "secret",
            new Uri("https://dav.example/cal/tasks/one.ics"),
            Encoding.UTF8.GetBytes("BEGIN:VCALENDAR\r\nEND:VCALENDAR\r\n"),
            "\"v2\"");
        CalDavWriteResult delete = await client.DeleteTaskAsync(
            account,
            "secret",
            new Uri("https://dav.example/cal/tasks/one.ics"),
            "\"v3\"");

        Assert.Equal("token-2", delta.SyncToken);
        Assert.Equal(2, delta.Resources.Count);
        Assert.Contains(delta.Resources, resource => resource.IsDeleted);
        Assert.True(put.IsSuccess);
        Assert.Equal("\"v3\"", put.ETag);
        Assert.True(delete.IsSuccess);
        Assert.Equal("\"v2\"", requests[1].IfMatch);
        Assert.Equal("\"v3\"", requests[2].IfMatch);
        Assert.Contains("token-1", requests[0].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidSyncToken_FallsBackToCtagSnapshot()
    {
        using var handler = new ScriptedHandler(
            request => XmlResponse("<d:error xmlns:d=\"DAV:\"/>", HttpStatusCode.Forbidden),
            request => XmlResponse("""
                <d:multistatus xmlns:d="DAV:" xmlns:cs="http://calendarserver.org/ns/">
                  <d:response><d:href>/cal/tasks/</d:href><d:propstat><d:prop><cs:getctag>ctag-2</cs:getctag></d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>
                </d:multistatus>
                """),
            request => XmlResponse("""
                <d:multistatus xmlns:d="DAV:" xmlns:cs="http://calendarserver.org/ns/">
                  <d:response><d:href>/cal/tasks/</d:href><d:propstat><d:prop><cs:getctag>ctag-2</cs:getctag></d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>
                  <d:response><d:href>/cal/tasks/one.ics</d:href><d:propstat><d:prop><d:getetag>"v1"</d:getetag></d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>
                </d:multistatus>
                """));
        using var httpClient = new HttpClient(handler);
        var client = new CalDavClient(httpClient);
        var list = new TodoList
        {
            RemoteUrl = "https://dav.example/cal/tasks/",
            SyncToken = "expired",
            RemoteCTag = "ctag-1",
        };

        CalDavDelta delta = await client.GetCollectionDeltaAsync(CreateAccount(), "secret", list);

        Assert.True(delta.IsFullSnapshot);
        Assert.Equal("ctag-2", delta.CTag);
        Assert.Single(delta.Resources);
    }

    private static Account CreateAccount()
    {
        return new Account
        {
            ServerUrl = "https://dav.example/dav/",
            Username = "alice",
            HomeSetUrl = "https://dav.example/calendars/alice/",
        };
    }

    private static HttpResponseMessage Respond(
        HttpRequestMessage request,
        List<CapturedRequest> requests,
        string body,
        HttpStatusCode statusCode = HttpStatusCode.MultiStatus,
        string? etag = null)
    {
        requests.Add(CapturedRequest.From(request));
        HttpResponseMessage response = XmlResponse(body, statusCode);
        if (etag is not null)
        {
            response.Headers.ETag = new EntityTagHeaderValue(etag);
        }

        return response;
    }

    private static HttpResponseMessage XmlResponse(
        string body,
        HttpStatusCode statusCode = HttpStatusCode.MultiStatus)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/xml"),
        };
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses;

        internal ScriptedHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
        {
            _responses = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>(responses);
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.NotEmpty(_responses);
            return Task.FromResult(_responses.Dequeue()(request));
        }
    }

    private sealed class CapturedRequest
    {
        internal AuthenticationHeaderValue? Authorization { get; init; }

        internal string? Depth { get; init; }

        internal string? IfMatch { get; init; }

        internal string Body { get; init; } = string.Empty;

        internal static CapturedRequest From(HttpRequestMessage request)
        {
            return new CapturedRequest
            {
                Authorization = request.Headers.Authorization,
                Depth = request.Headers.TryGetValues("Depth", out IEnumerable<string>? depths)
                    ? depths.Single()
                    : null,
                IfMatch = request.Headers.TryGetValues("If-Match", out IEnumerable<string>? matches)
                    ? matches.Single()
                    : null,
                Body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty,
            };
        }
    }
}
