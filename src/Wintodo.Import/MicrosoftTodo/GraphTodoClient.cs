using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Wintodo.Core.Services;

namespace Wintodo.Import.MicrosoftTodo;

/// <summary>Read-only Microsoft Graph client for the To Do API.</summary>
public sealed class GraphTodoClient
{
    public static readonly Uri DefaultBaseUri = new("https://graph.microsoft.com/v1.0/");
    private const int MaxAttempts = 6;
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(60);

    private readonly HttpClient _httpClient;
    private readonly string _accessToken;
    private readonly Uri _baseUri;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly IClock _clock;
    private bool _expandUnsupported;
    private readonly object _throttleLock = new();
    private Task _throttleWait = Task.CompletedTask;

    public Action<TimeSpan>? Retrying { get; set; }

    /// <summary>Only a tenant claim from this sign-in can enable administrator consent.</summary>
    public string? WorkTenantId { get; }

    /// <summary>Creates the production Graph transport without implicit redirects.</summary>
    public static HttpClient CreateHttpClient() => new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(100),
    };

    /// <summary>
    /// Uses an HTTP transport with automatic redirects disabled. Use
    /// <see cref="CreateHttpClient"/> for the production network handler.
    /// </summary>
    public GraphTodoClient(
        HttpClient httpClient,
        string accessToken,
        Uri? baseUri = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        IClock? clock = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        _httpClient = httpClient;
        _accessToken = accessToken;
        _baseUri = baseUri ?? DefaultBaseUri;
        if (!_baseUri.IsAbsoluteUri
            || (_baseUri.Scheme != Uri.UriSchemeHttps && _baseUri.Scheme != Uri.UriSchemeHttp)
            || _baseUri.UserInfo.Length != 0
            || _baseUri.Fragment.Length != 0)
        {
            throw new ArgumentException("Graph base URI must be an absolute HTTP(S) origin without user information or fragments.", nameof(baseUri));
        }
        _delay = delay ?? Task.Delay;
        _clock = clock ?? new SystemClock();
        WorkTenantId = ReadWorkTenantId(accessToken);
    }

    private static string? ReadWorkTenantId(string token)
    {
        string[] parts = token.Split('.');
        if (parts.Length != 3) return null;
        try
        {
            string encoded = parts[1].Replace('-', '+').Replace('_', '/');
            encoded = encoded.PadRight((encoded.Length + 3) / 4 * 4, '=');
            var claims = JsonSerializer.Deserialize(Convert.FromBase64String(encoded), GraphJsonContext.Default.MicrosoftTokenClaims);
            return Guid.TryParse(claims?.TenantId, out Guid tenant)
                && tenant != Guid.Parse("9188040d-6c67-4c5b-b112-36a304b66dad") ? tenant.ToString("D") : null;
        }
        catch (Exception exception) when (exception is FormatException or JsonException) { return null; }
    }

    public Task<GraphUser> GetMeAsync(CancellationToken cancellationToken = default)
    {
        return GetJsonAsync(
            new Uri(_baseUri, "me?$select=id,displayName,mail,userPrincipalName"),
            GraphJsonContext.Default.GraphUser,
            cancellationToken);
    }

    /// <summary>
    /// Reads every list through the delta function. For some personal
    /// accounts the plain <c>me/todo/lists</c> call returns no lists, or only
    /// "Flagged email", while delta returns all of them (found in the owner's
    /// manual gate, 2026-10-19). A first delta round has no removals, but any
    /// <c>@removed</c> entry is skipped.
    /// </summary>
    public async Task<List<GraphTodoList>> GetListsAsync(CancellationToken cancellationToken = default)
    {
        List<GraphTodoList> lists = await GetAllPagesAsync(
            new Uri(_baseUri, "me/todo/lists/delta"),
            GraphJsonContext.Default.GraphCollectionGraphTodoList,
            cancellationToken).ConfigureAwait(false);
        lists.RemoveAll(list => list.Removed is not null || string.IsNullOrEmpty(list.Id));
        return lists;
    }

    public async Task<List<GraphTodoTask>> GetTasksAsync(
        string listId,
        CancellationToken cancellationToken = default)
    {
        string listPath = "me/todo/lists/" + Escape(listId) + "/tasks";
        if (!_expandUnsupported)
        {
            try
            {
                List<GraphTodoTask> expandedTasks = await GetAllPagesAsync(
                    new Uri(_baseUri, listPath + "?$top=100&$expand=checklistItems,linkedResources"),
                    GraphJsonContext.Default.GraphCollectionGraphTodoTask,
                    cancellationToken).ConfigureAwait(false);
                TraceTaskRead(expandedTasks, "success");
                return expandedTasks;
            }
            catch (GraphRequestException exception) when (exception.StatusCode == HttpStatusCode.BadRequest)
            {
                _expandUnsupported = true;
                Trace.WriteLine("WINTODO_IMPORT_READ expand=fallback status=400");
            }
        }

        List<GraphTodoTask> tasks = await GetAllPagesAsync(
            new Uri(_baseUri, listPath + "?$top=100"),
            GraphJsonContext.Default.GraphCollectionGraphTodoTask,
            cancellationToken).ConfigureAwait(false);
        foreach (GraphTodoTask task in tasks)
        {
            string taskPath = listPath + "/" + Escape(task.Id);
            task.ChecklistItems = await GetAllPagesAsync(
                new Uri(_baseUri, taskPath + "/checklistItems"),
                GraphJsonContext.Default.GraphCollectionGraphChecklistItem,
                cancellationToken).ConfigureAwait(false);
            task.LinkedResources = await GetAllPagesAsync(
                new Uri(_baseUri, taskPath + "/linkedResources"),
                GraphJsonContext.Default.GraphCollectionGraphLinkedResource,
                cancellationToken).ConfigureAwait(false);
        }

        TraceTaskRead(tasks, "fallback");
        return tasks;
    }

    private static void TraceTaskRead(List<GraphTodoTask> tasks, string expand)
    {
        // Counts and endpoint outcomes only; never log names, ids or URLs.
        Trace.WriteLine(
            $"WINTODO_IMPORT_READ expand={expand} tasks={tasks.Count} " +
            $"completed={tasks.Count(task => task.Status == "completed")} " +
            $"steps={tasks.Sum(task => task.ChecklistItems?.Count ?? 0)} " +
            $"links={tasks.Sum(task => task.LinkedResources?.Count ?? 0)}");
    }

    public Task<List<GraphAttachment>> GetAttachmentsAsync(
        string listId,
        string taskId,
        CancellationToken cancellationToken = default)
    {
        return GetAllPagesAsync(
            new Uri(_baseUri, AttachmentsPath(listId, taskId) + "?$select=id,name,contentType,size,lastModifiedDateTime"),
            GraphJsonContext.Default.GraphCollectionGraphAttachment,
            cancellationToken);
    }

    public async Task<byte[]> GetAttachmentContentAsync(
        string listId,
        string taskId,
        string attachmentId,
        CancellationToken cancellationToken = default)
    {
        using var destination = new MemoryStream();
        await CopyAttachmentContentAsync(listId, taskId, attachmentId, destination,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return destination.ToArray();
    }

    /// <summary>Copies attachment content without buffering the file in memory.</summary>
    public async Task<long> CopyAttachmentContentAsync(
        string listId,
        string taskId,
        string attachmentId,
        Stream destination,
        Action<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string path = AttachmentsPath(listId, taskId) + "/" + Escape(attachmentId);
        HttpResponseMessage raw;
        try
        {
            raw = await SendAsync(
                new Uri(_baseUri, path + "/$value"),
                cancellationToken).ConfigureAwait(false);
        }
        catch (GraphRequestException exception) when (
            exception.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound)
        {
            Trace.WriteLine($"WINTODO_IMPORT_READ attachment=fallback status={(int)exception.StatusCode}");
            using HttpResponseMessage response = await SendAsync(new Uri(_baseUri, path), cancellationToken).ConfigureAwait(false);
            await using Stream json = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            long count = await AttachmentContentDecoder.CopyAsync(json, destination, progress, cancellationToken).ConfigureAwait(false);
            Trace.WriteLine($"WINTODO_IMPORT_READ attachment=contentBytes bytes={count}");
            return count;
        }

        using (raw)
        {
            await using Stream source = await raw.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            byte[] buffer = new byte[64 * 1024];
            long count = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                count += read;
                progress?.Invoke(count);
            }
            Trace.WriteLine($"WINTODO_IMPORT_READ attachment=raw bytes={count}");
            return count;
        }
    }

    private static string AttachmentsPath(string listId, string taskId)
    {
        return "me/todo/lists/" + Escape(listId) + "/tasks/" + Escape(taskId) + "/attachments";
    }

    private static string Escape(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return Uri.EscapeDataString(id);
    }

    private async Task<List<T>> GetAllPagesAsync<T>(
        Uri firstPage,
        JsonTypeInfo<GraphCollection<T>> typeInfo,
        CancellationToken cancellationToken)
    {
        var items = new List<T>();
        Uri? next = firstPage;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (next is not null)
        {
            if (!visited.Add(next.AbsoluteUri))
            {
                throw new GraphRequestException(HttpStatusCode.OK, "pagingLoop", "Graph returned a repeating page link.");
            }

            GraphCollection<T> page = await GetJsonAsync(next, typeInfo, cancellationToken).ConfigureAwait(false);
            items.AddRange(page.Value);
            next = string.IsNullOrWhiteSpace(page.NextLink) ? null : ResolveNextLink(page.NextLink);
        }

        return items;
    }

    private Uri ResolveNextLink(string nextLink)
    {
        return new Uri(_baseUri, nextLink);
    }

    private void ValidateRequestUri(Uri uri)
    {
        // Validate the final destination, including network-path links, before
        // constructing any authenticated request (paging and downloads alike).
        if (!uri.IsAbsoluteUri
            || !string.Equals(uri.IdnHost, _baseUri.IdnHost, StringComparison.OrdinalIgnoreCase)
            || uri.Scheme != _baseUri.Scheme
            || uri.Port != _baseUri.Port
            || uri.UserInfo.Length != 0
            || uri.Fragment.Length != 0)
        {
            throw new GraphRequestException(HttpStatusCode.OK, "unexpectedHost", "Graph returned a request destination outside the approved origin.");
        }
    }

    private async Task<T> GetJsonAsync<T>(
        Uri uri,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendAsync(uri, cancellationToken).ConfigureAwait(false);
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await JsonSerializer.DeserializeAsync(stream, typeInfo, cancellationToken).ConfigureAwait(false)
                ?? throw new GraphRequestException(response.StatusCode, "emptyResponse", "Graph returned an empty response.");
        }
        catch (JsonException exception)
        {
            throw new GraphRequestException(response.StatusCode, "invalidJson", exception.Message);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(Uri uri, CancellationToken cancellationToken)
    {
        ValidateRequestUri(uri);
        for (int attempt = 1; ; attempt++)
        {
            Task throttle;
            lock (_throttleLock) throttle = _throttleWait;
            await throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            HttpResponseMessage response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            bool transient = response.StatusCode is HttpStatusCode.TooManyRequests
                or HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.GatewayTimeout
                or HttpStatusCode.BadGateway;
            if (transient && attempt < MaxAttempts)
            {
                TimeSpan delay = GetRetryDelay(response, attempt);
                Retrying?.Invoke(delay);
                response.Dispose();
                lock (_throttleLock)
                {
                    Task wait = _delay(delay, cancellationToken);
                    _throttleWait = _throttleWait.IsCompleted ? wait : Task.WhenAll(_throttleWait, wait);
                    throttle = _throttleWait;
                }
                await throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            using (response)
            {
                throw await CreateExceptionAsync(response, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        RetryConditionHeaderValue? retryAfter = response.Headers.RetryAfter;
        TimeSpan delay = retryAfter?.Delta
            ?? (retryAfter?.Date is DateTimeOffset date ? date - _clock.UtcNow : (TimeSpan?)null)
            ?? TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
        if (delay < TimeSpan.Zero)
        {
            delay = TimeSpan.Zero;
        }

        return delay > MaxRetryDelay ? MaxRetryDelay : delay;
    }

    private static async Task<Exception> CreateExceptionAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        string? code = null;
        string? message = null;
        try
        {
            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            GraphErrorResponse? error = await JsonSerializer.DeserializeAsync(
                stream,
                GraphJsonContext.Default.GraphErrorResponse,
                cancellationToken).ConfigureAwait(false);
            code = error?.Error?.Code;
            message = error?.Error?.Message;
        }
        catch (JsonException)
        {
        }

        return response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            ? new GraphAuthorizationException(response.StatusCode, code, message)
            : new GraphRequestException(response.StatusCode, code, message);
    }
}

public class GraphRequestException(HttpStatusCode statusCode, string? code, string? message)
    : Exception(message ?? $"Microsoft Graph returned {(int)statusCode}.")
{
    public HttpStatusCode StatusCode { get; } = statusCode;

    public string? Code { get; } = code;
}

public sealed class GraphAuthorizationException(HttpStatusCode statusCode, string? code, string? message)
    : GraphRequestException(statusCode, code, message)
{
    /// <summary>
    /// HTTP 401: Graph rejected the access token, usually because it expired
    /// while the wizard waited. Signing in again fixes it; a 403 does not.
    /// </summary>
    public bool IsSignInExpired => StatusCode == HttpStatusCode.Unauthorized;
}
