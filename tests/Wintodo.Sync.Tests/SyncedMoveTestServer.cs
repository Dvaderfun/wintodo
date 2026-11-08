using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using Wintodo.Core.Ical;
using Wintodo.Core.Models;

namespace Wintodo.Sync.Tests;

internal sealed class SyncedMoveTestServer : HttpMessageHandler
{
    private static readonly XNamespace Dav = "DAV:";
    private static readonly XNamespace CalDav = "urn:ietf:params:xml:ns:caldav";
    private static readonly XNamespace CalendarServer = "http://calendarserver.org/ns/";
    private readonly Dictionary<string, string> _credentials = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _collections = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StoredMoveResource> _resources = new(StringComparer.Ordinal);
    private readonly List<MoveJournalEntry> _journal = [];
    private int _version;

    internal List<MoveServerRequest> Requests { get; } = [];
    internal Func<MoveServerRequest, CancellationToken, Task>? BeforeRequest { get; set; }
    internal Func<MoveServerRequest, CancellationToken, Task>? AfterWrite { get; set; }
    internal bool ReverseCollections { get; set; }
    internal bool OmitETags { get; set; }

    internal void AddAccount(string host, string secret) =>
        _credentials[host] = Convert.ToBase64String(Encoding.UTF8.GetBytes("synthetic:" + secret));

    internal void AddCollection(string url, string name) => _collections[url] = name;

    internal string Seed(string url, byte[] content)
    {
        string etag = '"' + "v" + (++_version).ToString(CultureInfo.InvariantCulture) + '"';
        _resources[url] = new StoredMoveResource(content, etag);
        _journal.Add(new MoveJournalEntry(_version, url, false));
        return etag;
    }

    internal void Remove(string url)
    {
        if (_resources.Remove(url)) _journal.Add(new MoveJournalEntry(++_version, url, true));
    }

    internal bool Contains(string url) => _resources.ContainsKey(url);
    internal string? ETag(string url) => _resources.GetValueOrDefault(url)?.ETag;
    internal byte[] Content(string url) => _resources[url].Content;
    internal IReadOnlyList<TodoTask> Tasks(string collection) => _resources
        .Where(pair => pair.Key.StartsWith(collection, StringComparison.Ordinal))
        .SelectMany(pair => new IcalReader(TimeZoneInfo.Utc).Read(pair.Value.Content).Tasks).ToArray();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Uri uri = request.RequestUri!;
        byte[]? payload = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        var observed = new MoveServerRequest(request.Method.Method, uri.AbsoluteUri,
            Header(request, "If-Match"), Header(request, "If-None-Match"), payload);
        Requests.Add(observed);
        if (request.Headers.Authorization?.Scheme != "Basic"
            || request.Headers.Authorization.Parameter != _credentials.GetValueOrDefault(uri.Host))
        {
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }

        if (BeforeRequest is not null) await BeforeRequest(observed, cancellationToken);
        if (observed.Method == "GET")
        {
            if (!_resources.TryGetValue(observed.Url, out StoredMoveResource? resource))
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(resource.Content) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/calendar");
            if (!OmitETags) response.Headers.ETag = new EntityTagHeaderValue(resource.ETag);
            return response;
        }

        if (observed.Method == "PUT")
        {
            _resources.TryGetValue(observed.Url, out StoredMoveResource? previous);
            if (observed.IfNoneMatch == "*" && previous is not null
                || observed.IfMatch is not null && previous?.ETag != observed.IfMatch)
                return new HttpResponseMessage(HttpStatusCode.PreconditionFailed);
            string etag = Seed(observed.Url, payload!);
            if (AfterWrite is not null) await AfterWrite(observed, cancellationToken);
            var response = new HttpResponseMessage(HttpStatusCode.Created);
            if (!OmitETags) response.Headers.ETag = new EntityTagHeaderValue(etag);
            return response;
        }

        if (observed.Method == "DELETE" && _collections.ContainsKey(observed.Url))
        {
            foreach (string url in _resources.Keys.Where(url => url.StartsWith(observed.Url, StringComparison.Ordinal)).ToArray())
                Remove(url);
            _collections.Remove(observed.Url);
            if (AfterWrite is not null) await AfterWrite(observed, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        if (observed.Method == "DELETE")
        {
            if (!_resources.TryGetValue(observed.Url, out StoredMoveResource? previous))
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            if (observed.IfMatch is not null && observed.IfMatch != previous.ETag)
                return new HttpResponseMessage(HttpStatusCode.PreconditionFailed);
            Remove(observed.Url);
            if (AfterWrite is not null) await AfterWrite(observed, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        if (observed.Method == "PROPFIND" && uri.AbsolutePath == "/cal/")
        {
            IEnumerable<KeyValuePair<string, string>> collections = _collections.Where(pair => new Uri(pair.Key).Host == uri.Host);
            if (ReverseCollections) collections = collections.Reverse();
            return Xml(collections.Select(pair => Collection(pair.Key, pair.Value)));
        }

        if (observed.Method == "PROPFIND" && _collections.TryGetValue(observed.Url, out string? name))
        {
            var responses = new List<XElement> { Collection(observed.Url, name) };
            if (Header(request, "Depth") != "0")
                responses.AddRange(_resources.Where(pair => pair.Key.StartsWith(observed.Url, StringComparison.Ordinal))
                    .Select(pair => Resource(pair.Key, pair.Value)));
            return Xml(responses);
        }

        if (observed.Method == "PROPFIND" && _resources.TryGetValue(observed.Url, out StoredMoveResource? stored))
            return Xml([Resource(observed.Url, stored)]);

        if (observed.Method == "REPORT")
        {
            string token = XDocument.Parse(Encoding.UTF8.GetString(payload!)).Descendants(Dav + "sync-token").Single().Value;
            int since = int.Parse(token[6..], CultureInfo.InvariantCulture);
            IEnumerable<XElement> responses = _journal.Where(entry => entry.Version > since
                    && entry.Url.StartsWith(observed.Url, StringComparison.Ordinal))
                .GroupBy(entry => entry.Url, StringComparer.Ordinal).Select(group => group.Last())
                .Select(entry => entry.Deleted
                    ? new XElement(Dav + "response", new XElement(Dav + "href", entry.Url),
                        new XElement(Dav + "status", "HTTP/1.1 404 Not Found"))
                    : Resource(entry.Url, _resources[entry.Url]));
            return Xml(responses);
        }

        if (observed.Method == "MKCALENDAR")
        {
            string collectionName = payload is null ? "Created collection"
                : XDocument.Parse(Encoding.UTF8.GetString(payload)).Descendants(Dav + "displayname")
                    .Select(element => element.Value).FirstOrDefault() ?? "Created collection";
            AddCollection(observed.Url, collectionName);
            return new HttpResponseMessage(HttpStatusCode.Created);
        }

        if (observed.Method == "PROPPATCH") return Xml([Collection(observed.Url, _collections.GetValueOrDefault(observed.Url) ?? "Collection")]);
        return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
    }

    private XElement Collection(string url, string name) => Response(url,
        new XElement(Dav + "displayname", name),
        new XElement(Dav + "resourcetype", new XElement(Dav + "collection"), new XElement(CalDav + "calendar")),
        new XElement(CalDav + "supported-calendar-component-set", new XElement(CalDav + "comp", new XAttribute("name", "VTODO"))),
        new XElement(CalendarServer + "getctag", "ctag-" + _version.ToString(CultureInfo.InvariantCulture)));

    private XElement Resource(string url, StoredMoveResource resource) => Response(url,
        new XElement(Dav + "getcontenttype", "text/calendar"),
        OmitETags ? null : new XElement(Dav + "getetag", resource.ETag));

    private static XElement Response(string url, params XElement?[] properties) => new(Dav + "response",
        new XElement(Dav + "href", url), new XElement(Dav + "propstat", new XElement(Dav + "prop", properties),
            new XElement(Dav + "status", "HTTP/1.1 200 OK")));

    private HttpResponseMessage Xml(IEnumerable<XElement> responses) => new(HttpStatusCode.MultiStatus)
    {
        Content = new StringContent(new XDocument(new XElement(Dav + "multistatus", responses,
            new XElement(Dav + "sync-token", "token-" + _version.ToString(CultureInfo.InvariantCulture))))
            .ToString(SaveOptions.DisableFormatting), Encoding.UTF8, "application/xml"),
    };

    private static string? Header(HttpRequestMessage request, string name) =>
        request.Headers.TryGetValues(name, out IEnumerable<string>? values) ? values.Single() : null;

    private sealed record StoredMoveResource(byte[] Content, string ETag);
    private sealed record MoveJournalEntry(int Version, string Url, bool Deleted);
}

internal sealed record MoveServerRequest(string Method, string Url, string? IfMatch, string? IfNoneMatch, byte[]? Payload);
