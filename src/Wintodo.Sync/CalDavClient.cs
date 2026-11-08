using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Wintodo.Core.Models;

namespace Wintodo.Sync;

public sealed class CalDavClient
{
    private const int MaximumXmlCharacters = 16 * 1024 * 1024;
    private const int MaximumCalendarBytes = 32 * 1024 * 1024;
    private static readonly XNamespace Dav = "DAV:";
    private static readonly XNamespace CalDav = "urn:ietf:params:xml:ns:caldav";
    private static readonly XNamespace CalendarServer = "http://calendarserver.org/ns/";
    private static readonly HttpMethod PropFind = new("PROPFIND");
    private static readonly HttpMethod Report = new("REPORT");
    private static readonly HttpMethod MkCalendar = new("MKCALENDAR");
    private static readonly HttpMethod PropPatch = new("PROPPATCH");
    private readonly HttpClient _httpClient;

    /// <summary>Creates the production CalDAV transport without implicit redirects.</summary>
    public static HttpClient CreateHttpClient() => new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    /// <summary>
    /// Uses a transport with automatic redirects disabled. Use
    /// <see cref="CreateHttpClient"/> for the production network handler.
    /// </summary>
    public CalDavClient(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
    }

    public async Task<CalDavDiscovery> DiscoverAsync(
        Account account,
        string secret,
        CancellationToken cancellationToken = default)
    {
        Uri serverUrl = ValidateServerUrl(account.ServerUrl);
        XDocument initial = await SendXmlAsync(
            PropFind,
            serverUrl,
            account,
            secret,
            depth: "0",
            CreatePropFind(Dav + "current-user-principal", CalDav + "calendar-home-set"),
            cancellationToken);
        XElement? initialProp = GetSuccessfulProperties(initial).FirstOrDefault();
        string? principalHref = initialProp?
            .Element(Dav + "current-user-principal")?
            .Element(Dav + "href")?
            .Value;
        string? homeHref = initialProp?
            .Element(CalDav + "calendar-home-set")?
            .Element(Dav + "href")?
            .Value;

        Uri principalUrl = ResolveHref(serverUrl, principalHref ?? serverUrl.AbsolutePath);
        if (string.IsNullOrWhiteSpace(homeHref))
        {
            XDocument principal = await SendXmlAsync(
                PropFind,
                principalUrl,
                account,
                secret,
                depth: "0",
                CreatePropFind(CalDav + "calendar-home-set"),
                cancellationToken);
            homeHref = GetSuccessfulProperties(principal)
                .Select(properties => properties
                    .Element(CalDav + "calendar-home-set")?
                    .Element(Dav + "href")?
                    .Value)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        }

        if (string.IsNullOrWhiteSpace(homeHref))
        {
            throw new InvalidOperationException(
                "The server did not advertise a CalDAV calendar-home-set.");
        }

        return new CalDavDiscovery
        {
            PrincipalUrl = principalUrl,
            HomeSetUrl = ResolveHref(principalUrl, homeHref),
        };
    }

    public async Task<IReadOnlyList<CalDavCollection>> GetCollectionsAsync(
        Account account,
        string secret,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(account.HomeSetUrl))
        {
            throw new InvalidOperationException("CalDAV discovery has not completed.");
        }

        Uri homeSetUrl = new(account.HomeSetUrl, UriKind.Absolute);
        XDocument document = await SendXmlAsync(
            PropFind,
            homeSetUrl,
            account,
            secret,
            depth: "1",
            CreatePropFind(
                Dav + "resourcetype",
                Dav + "displayname",
                Dav + "sync-token",
                CalendarServer + "getctag",
                CalDav + "supported-calendar-component-set"),
            cancellationToken);
        var collections = new List<CalDavCollection>();
        foreach (XElement response in document.Descendants(Dav + "response"))
        {
            XElement? properties = GetSuccessfulProperties(response).FirstOrDefault();
            if (properties is null
                || properties.Element(Dav + "resourcetype")?.Element(CalDav + "calendar") is null)
            {
                continue;
            }

            XElement? components = properties.Element(CalDav + "supported-calendar-component-set");
            bool supportsTodo = components is null
                || !components.Elements(CalDav + "comp").Any()
                || components.Elements(CalDav + "comp").Any(component =>
                    string.Equals(
                        (string?)component.Attribute("name"),
                        "VTODO",
                        StringComparison.OrdinalIgnoreCase));
            if (!supportsTodo)
            {
                continue;
            }

            string? href = response.Element(Dav + "href")?.Value;
            if (string.IsNullOrWhiteSpace(href))
            {
                continue;
            }

            Uri url = ResolveHref(homeSetUrl, href);
            collections.Add(new CalDavCollection
            {
                Url = url,
                DisplayName = properties.Element(Dav + "displayname")?.Value.Trim()
                    is { Length: > 0 } name
                    ? name
                    : Uri.UnescapeDataString(url.Segments.Last().Trim('/')),
                CTag = EmptyToNull(properties.Element(CalendarServer + "getctag")?.Value),
                SyncToken = EmptyToNull(properties.Element(Dav + "sync-token")?.Value),
            });
        }

        return collections;
    }

    public async Task<CalDavDelta> GetCollectionDeltaAsync(
        Account account,
        string secret,
        TodoList list,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(list.RemoteUrl))
        {
            throw new InvalidOperationException("The list is not linked to a CalDAV collection.");
        }

        Uri collectionUrl = new(list.RemoteUrl, UriKind.Absolute);
        if (!string.IsNullOrWhiteSpace(list.SyncToken))
        {
            using HttpResponseMessage response = await SendAsync(
                Report,
                collectionUrl,
                account,
                secret,
                depth: "1",
                new XDocument(
                    new XElement(
                        Dav + "sync-collection",
                        new XAttribute(XNamespace.Xmlns + "d", Dav.NamespaceName),
                        new XElement(Dav + "sync-token", list.SyncToken),
                        new XElement(Dav + "sync-level", "1"),
                        new XElement(Dav + "prop", new XElement(Dav + "getetag")))),
                cancellationToken);
            string body = await ReadLimitedStringAsync(response, cancellationToken);
            if (response.StatusCode == HttpStatusCode.MultiStatus)
            {
                return ParseDelta(ParseXml(body), collectionUrl, isFullSnapshot: false);
            }

            if (response.StatusCode is not HttpStatusCode.Forbidden
                and not HttpStatusCode.Conflict)
            {
                ThrowProtocol(response.StatusCode, "sync-collection REPORT failed", body);
            }
        }

        string? currentCTag = await GetCollectionCTagAsync(
            collectionUrl,
            account,
            secret,
            cancellationToken);
        if (!string.IsNullOrWhiteSpace(list.RemoteCTag)
            && string.Equals(list.RemoteCTag, currentCTag, StringComparison.Ordinal))
        {
            return new CalDavDelta
            {
                IsFullSnapshot = false,
                CTag = currentCTag,
            };
        }

        XDocument snapshot = await SendXmlAsync(
            PropFind,
            collectionUrl,
            account,
            secret,
            depth: "1",
            CreatePropFind(Dav + "getetag", Dav + "getcontenttype", Dav + "sync-token",
                CalendarServer + "getctag"),
            cancellationToken);
        CalDavDelta delta = ParseDelta(snapshot, collectionUrl, isFullSnapshot: true);
        delta.CTag ??= currentCTag;
        return delta;
    }

    public async Task<CalDavContent> GetTaskAsync(
        Account account,
        string secret,
        Uri resourceUrl,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, resourceUrl, account, secret);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/calendar"));
        using HttpResponseMessage response = await SendRequestAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string body = await ReadLimitedStringAsync(response, cancellationToken);
            ThrowProtocol(response.StatusCode, "CalDAV resource GET failed", body);
        }

        byte[] content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (content.Length > MaximumCalendarBytes)
        {
            throw new InvalidOperationException("The CalDAV resource exceeds the 32 MB limit.");
        }

        return new CalDavContent
        {
            Content = content,
            ETag = response.Headers.ETag?.Tag,
        };
    }

    public async Task<CalDavWriteResult> PutTaskAsync(
        Account account,
        string secret,
        Uri resourceUrl,
        byte[] content,
        string? expectedETag,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Put, resourceUrl, account, secret);
        request.Content = new ByteArrayContent(content);
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("text/calendar; charset=utf-8");
        AddConditionalHeader(request, expectedETag);
        using HttpResponseMessage response = await SendRequestAsync(request, cancellationToken);
        return await ReadWriteResultAsync(
            response,
            resourceUrl,
            account,
            secret,
            cancellationToken);
    }

    public async Task<CalDavWriteResult> DeleteTaskAsync(
        Account account,
        string secret,
        Uri resourceUrl,
        string? expectedETag,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Delete, resourceUrl, account, secret);
        if (!string.IsNullOrWhiteSpace(expectedETag))
        {
            request.Headers.TryAddWithoutValidation("If-Match", expectedETag);
        }

        using HttpResponseMessage response = await SendRequestAsync(request, cancellationToken);
        return new CalDavWriteResult
        {
            StatusCode = response.StatusCode,
            IsSuccess = response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound,
            IsConflict = response.StatusCode == HttpStatusCode.PreconditionFailed,
        };
    }

    public async Task<Uri> CreateCollectionAsync(
        Account account,
        string secret,
        string listUid,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(account.HomeSetUrl))
        {
            throw new InvalidOperationException("CalDAV discovery has not completed.");
        }

        Uri home = EnsureTrailingSlash(new Uri(account.HomeSetUrl, UriKind.Absolute));
        Uri url = new(home, Uri.EscapeDataString(listUid) + "/");
        var properties = new XElement(
            Dav + "prop",
            new XElement(Dav + "displayname", displayName),
            new XElement(
                Dav + "resourcetype",
                new XElement(Dav + "collection"),
                new XElement(CalDav + "calendar")),
            new XElement(
                CalDav + "supported-calendar-component-set",
                new XElement(CalDav + "comp", new XAttribute("name", "VTODO"))));
        var body = new XDocument(
            new XElement(
                CalDav + "mkcalendar",
                new XAttribute(XNamespace.Xmlns + "d", Dav.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "c", CalDav.NamespaceName),
                new XElement(Dav + "set", properties)));
        using HttpResponseMessage response = await SendAsync(
            MkCalendar,
            url,
            account,
            secret,
            depth: null,
            body: body,
            cancellationToken: cancellationToken);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.MethodNotAllowed)
        {
            string responseBody = await ReadLimitedStringAsync(response, cancellationToken);
            ThrowProtocol(response.StatusCode, "MKCALENDAR failed", responseBody);
        }

        return url;
    }

    public async Task RenameCollectionAsync(
        Account account,
        string secret,
        Uri collectionUrl,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        var body = new XDocument(
            new XElement(
                Dav + "propertyupdate",
                new XAttribute(XNamespace.Xmlns + "d", Dav.NamespaceName),
                new XElement(
                    Dav + "set",
                    new XElement(Dav + "prop", new XElement(Dav + "displayname", displayName)))));
        using HttpResponseMessage response = await SendAsync(
            PropPatch,
            collectionUrl,
            account,
            secret,
            depth: null,
            body,
            cancellationToken);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.MultiStatus)
        {
            string responseBody = await ReadLimitedStringAsync(response, cancellationToken);
            ThrowProtocol(response.StatusCode, "Collection PROPPATCH failed", responseBody);
        }
    }

    public async Task DeleteCollectionAsync(
        Account account,
        string secret,
        Uri collectionUrl,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Delete, collectionUrl, account, secret);
        using HttpResponseMessage response = await SendRequestAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
        {
            string body = await ReadLimitedStringAsync(response, cancellationToken);
            ThrowProtocol(response.StatusCode, "Collection DELETE failed", body);
        }
    }

    public static Uri ResolveHref(Uri baseUrl, string href)
    {
        return Uri.TryCreate(href, UriKind.Absolute, out Uri? absolute)
            ? absolute
            : new Uri(baseUrl, href);
    }

    private async Task<string?> GetCollectionCTagAsync(
        Uri collectionUrl,
        Account account,
        string secret,
        CancellationToken cancellationToken)
    {
        XDocument document = await SendXmlAsync(
            PropFind,
            collectionUrl,
            account,
            secret,
            depth: "0",
            CreatePropFind(CalendarServer + "getctag"),
            cancellationToken);
        return GetSuccessfulProperties(document)
            .Select(properties => EmptyToNull(properties.Element(CalendarServer + "getctag")?.Value))
            .FirstOrDefault(value => value is not null);
    }

    private async Task<CalDavWriteResult> ReadWriteResultAsync(
        HttpResponseMessage response,
        Uri resourceUrl,
        Account account,
        string secret,
        CancellationToken cancellationToken)
    {
        var result = new CalDavWriteResult
        {
            StatusCode = response.StatusCode,
            IsSuccess = response.IsSuccessStatusCode,
            IsConflict = response.StatusCode == HttpStatusCode.PreconditionFailed,
            ETag = response.Headers.ETag?.Tag,
        };
        if (result.IsSuccess && string.IsNullOrWhiteSpace(result.ETag))
        {
            XDocument document = await SendXmlAsync(
                PropFind,
                resourceUrl,
                account,
                secret,
                depth: "0",
                CreatePropFind(Dav + "getetag"),
                cancellationToken);
            result.ETag = GetSuccessfulProperties(document)
                .Select(properties => EmptyToNull(properties.Element(Dav + "getetag")?.Value))
                .FirstOrDefault(value => value is not null);
        }

        return result;
    }

    private static CalDavDelta ParseDelta(
        XDocument document,
        Uri collectionUrl,
        bool isFullSnapshot)
    {
        var delta = new CalDavDelta
        {
            IsFullSnapshot = isFullSnapshot,
            SyncToken = EmptyToNull(document.Root?.Element(Dav + "sync-token")?.Value),
        };
        foreach (XElement response in document.Descendants(Dav + "response"))
        {
            string? href = response.Element(Dav + "href")?.Value;
            if (string.IsNullOrWhiteSpace(href))
            {
                continue;
            }

            Uri url = ResolveHref(collectionUrl, href);
            XElement? properties = GetSuccessfulProperties(response).FirstOrDefault();
            delta.CTag ??= EmptyToNull(properties?.Element(CalendarServer + "getctag")?.Value);
            delta.SyncToken ??= EmptyToNull(properties?.Element(Dav + "sync-token")?.Value);
            if (SameResource(collectionUrl, url))
            {
                continue;
            }

            string? responseStatus = response.Element(Dav + "status")?.Value;
            bool deleted = responseStatus?.Contains(" 404 ", StringComparison.Ordinal) == true;
            string? etag = EmptyToNull(properties?.Element(Dav + "getetag")?.Value);
            string? contentType = EmptyToNull(properties?.Element(Dav + "getcontenttype")?.Value);
            if (!deleted && isFullSnapshot && etag is null
                && contentType?.Contains("text/calendar", StringComparison.OrdinalIgnoreCase) != true)
            {
                continue;
            }

            delta.Resources.Add(new CalDavResource
            {
                Url = url,
                Href = url.AbsoluteUri,
                ETag = etag,
                IsDeleted = deleted,
            });
        }

        return delta;
    }

    private async Task<XDocument> SendXmlAsync(
        HttpMethod method,
        Uri url,
        Account account,
        string secret,
        string? depth,
        XDocument body,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendAsync(
            method,
            url,
            account,
            secret,
            depth,
            body,
            cancellationToken);
        string responseBody = await ReadLimitedStringAsync(response, cancellationToken);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.MultiStatus)
        {
            ThrowProtocol(response.StatusCode, $"{method.Method} failed", responseBody);
        }

        return ParseXml(responseBody);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        Uri url,
        Account account,
        string secret,
        string? depth,
        XDocument body,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, url, account, secret);
        if (depth is not null)
        {
            request.Headers.TryAddWithoutValidation("Depth", depth);
        }

        request.Content = new StringContent(
            body.ToString(SaveOptions.DisableFormatting),
            Encoding.UTF8,
            "application/xml");
        return await SendRequestAsync(request, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendRequestAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response = await _httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if ((int)response.StatusCode is >= 300 and < 400)
        {
            HttpStatusCode statusCode = response.StatusCode;
            response.Dispose();
            // Reject even same-origin redirects before reading their body. The network
            // handler must never replay authenticated requests underneath origin checks.
            throw new CalDavProtocolException("The CalDAV server returned an unsupported redirect.", statusCode);
        }

        return response;
    }

    private static HttpRequestMessage CreateRequest(
        HttpMethod method,
        Uri url,
        Account account,
        string secret)
    {
        ValidateRequestUrl(account, url);
        var request = new HttpRequestMessage(method, url);
        string value = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(account.Username + ":" + secret));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", value);
        request.Headers.UserAgent.ParseAdd("Wintodo/1.0");
        return request;
    }

    private static XDocument CreatePropFind(params XName[] properties)
    {
        return new XDocument(
            new XElement(
                Dav + "propfind",
                new XAttribute(XNamespace.Xmlns + "d", Dav.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "c", CalDav.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "cs", CalendarServer.NamespaceName),
                new XElement(Dav + "prop", properties.Select(name => new XElement(name)))));
    }

    private static IEnumerable<XElement> GetSuccessfulProperties(XContainer container)
    {
        return container.Descendants(Dav + "propstat")
            .Where(propstat =>
                propstat.Element(Dav + "status")?.Value.Contains(" 200 ", StringComparison.Ordinal)
                == true)
            .Select(propstat => propstat.Element(Dav + "prop"))
            .Where(prop => prop is not null)
            .Cast<XElement>();
    }

    private static void AddConditionalHeader(HttpRequestMessage request, string? expectedETag)
    {
        if (string.IsNullOrWhiteSpace(expectedETag))
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", "*");
        }
        else
        {
            request.Headers.TryAddWithoutValidation("If-Match", expectedETag);
        }
    }

    private static Uri ValidateServerUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? url)
            || (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp)
            || !string.IsNullOrEmpty(url.UserInfo)
            || !string.IsNullOrEmpty(url.Fragment))
        {
            throw new ArgumentException("A valid HTTP or HTTPS server URL is required.", nameof(value));
        }

        if (url.Scheme == Uri.UriSchemeHttp && !url.IsLoopback)
        {
            throw new ArgumentException(
                "CalDAV requires HTTPS except for a loopback development server.",
                nameof(value));
        }

        return url;
    }

    private static void ValidateRequestUrl(Account account, Uri requestUrl)
    {
        Uri serverUrl = ValidateServerUrl(account.ServerUrl);
        if (!string.Equals(serverUrl.Scheme, requestUrl.Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(serverUrl.IdnHost, requestUrl.IdnHost, StringComparison.OrdinalIgnoreCase)
            || serverUrl.Port != requestUrl.Port
            || !string.IsNullOrEmpty(requestUrl.UserInfo)
            || !string.IsNullOrEmpty(requestUrl.Fragment))
        {
            throw new InvalidOperationException(
                "The CalDAV server returned a URL outside the configured server origin.");
        }
    }

    private static XDocument ParseXml(string value)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumXmlCharacters,
        };
        using var textReader = new StringReader(value);
        using XmlReader reader = XmlReader.Create(textReader, settings);
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }

    private static async Task<string> ReadLimitedStringAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > MaximumXmlCharacters)
        {
            throw new InvalidOperationException("The CalDAV XML response exceeds the 16 MB limit.");
        }

        string value = await response.Content.ReadAsStringAsync(cancellationToken);
        if (value.Length > MaximumXmlCharacters)
        {
            throw new InvalidOperationException("The CalDAV XML response exceeds the 16 MB limit.");
        }

        return value;
    }

    private static Uri EnsureTrailingSlash(Uri value)
    {
        return value.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? value
            : new Uri(value.AbsoluteUri + "/", UriKind.Absolute);
    }

    private static bool SameResource(Uri left, Uri right)
    {
        return string.Equals(
            left.AbsoluteUri.TrimEnd('/'),
            right.AbsoluteUri.TrimEnd('/'),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string? EmptyToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static void ThrowProtocol(
        HttpStatusCode statusCode,
        string operation,
        string? responseBody)
    {
        throw new CalDavProtocolException(
            $"{operation} with HTTP {(int)statusCode} ({statusCode}).",
            statusCode,
            responseBody);
    }
}
