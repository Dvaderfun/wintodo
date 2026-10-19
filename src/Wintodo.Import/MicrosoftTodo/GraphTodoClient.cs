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
        _delay = delay ?? Task.Delay;
        _clock = clock ?? new SystemClock();
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
        string path = AttachmentsPath(listId, taskId) + "/" + Escape(attachmentId);
        try
        {
            using HttpResponseMessage raw = await SendAsync(
                new Uri(_baseUri, path + "/$value"),
                cancellationToken).ConfigureAwait(false);
            byte[] content = await raw.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            Trace.WriteLine($"WINTODO_IMPORT_READ attachment=raw bytes={content.LongLength}");
            return content;
        }
        catch (GraphRequestException exception) when (
            exception.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound)
        {
            Trace.WriteLine($"WINTODO_IMPORT_READ attachment=fallback status={(int)exception.StatusCode}");
            GraphAttachment attachment = await GetJsonAsync(
                new Uri(_baseUri, path),
                GraphJsonContext.Default.GraphAttachment,
                cancellationToken).ConfigureAwait(false);
            byte[] content = string.IsNullOrEmpty(attachment.ContentBytes)
                ? []
                : Convert.FromBase64String(attachment.ContentBytes);
            Trace.WriteLine($"WINTODO_IMPORT_READ attachment=contentBytes bytes={content.LongLength}");
            return content;
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
        var uri = new Uri(nextLink, UriKind.RelativeOrAbsolute);
        if (!uri.IsAbsoluteUri)
        {
            return new Uri(_baseUri, nextLink);
        }

        // Never send the token anywhere but the Graph host we started with.
        if (!string.Equals(uri.Host, _baseUri.Host, StringComparison.OrdinalIgnoreCase)
            || uri.Scheme != _baseUri.Scheme)
        {
            throw new GraphRequestException(HttpStatusCode.OK, "unexpectedHost", "Graph returned a page link for another host.");
        }

        return uri;
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
        for (int attempt = 1; ; attempt++)
        {
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
                response.Dispose();
                await _delay(delay, cancellationToken).ConfigureAwait(false);
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
    : GraphRequestException(statusCode, code, message);
