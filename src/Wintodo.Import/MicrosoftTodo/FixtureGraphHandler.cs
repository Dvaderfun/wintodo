using System.Net;
using System.Text;

namespace Wintodo.Import.MicrosoftTodo;

/// <summary>
/// Serves recorded Graph responses from a folder, so tests and UI gates run
/// the real client and mapper without a Microsoft account. A request for
/// <c>/v1.0/me/todo/lists/delta</c> reads <c>me/todo/lists/delta.json</c>. A raw
/// attachment request (<c>…/$value</c>) reads <c>…/content.bin</c>. The query
/// string is ignored, and a missing file returns a Graph-shaped 404.
/// With <c>rejectAttachmentContent</c>, raw attachment requests return the 401
/// Graph sends for an expired token, as seen live when an import started long
/// after sign-in.
/// </summary>
public sealed class FixtureGraphHandler : HttpMessageHandler
{
    private readonly string _root;
    private readonly bool _rejectAttachmentContent;
    private readonly TimeSpan _latency;
    private int _throttleOnce;

    public FixtureGraphHandler(string root, bool rejectAttachmentContent = false,
        TimeSpan latency = default, bool throttleOnce = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.GetFullPath(root);
        _rejectAttachmentContent = rejectAttachmentContent;
        _latency = latency;
        _throttleOnce = throttleOnce ? 1 : 0;
    }

    private int _requestCount;
    public int RequestCount => _requestCount;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _requestCount);
        if (_latency > TimeSpan.Zero) await Task.Delay(_latency, cancellationToken).ConfigureAwait(false);
        if (Interlocked.Exchange(ref _throttleOnce, 0) == 1)
        {
            var throttled = new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("{}") };
            throttled.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(3));
            return throttled;
        }
        string path = Uri.UnescapeDataString(request.RequestUri?.AbsolutePath ?? string.Empty).Trim('/');
        if (path.StartsWith("v1.0/", StringComparison.OrdinalIgnoreCase))
        {
            path = path[5..];
        }

        bool raw = path.EndsWith("/$value", StringComparison.Ordinal);
        if (raw && _rejectAttachmentContent)
        {
            return new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent(
                    "{\"error\":{\"code\":\"InvalidAuthenticationToken\",\"message\":\"Access token has expired or is not yet valid.\"}}",
                    Encoding.UTF8,
                    "application/json"),
            };
        }

        string relative = raw ? path[..^"/$value".Length] + "/content.bin" : path + ".json";
        string file = Path.GetFullPath(Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!file.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(file))
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent(
                    "{\"error\":{\"code\":\"itemNotFound\",\"message\":\"Fixture not found.\"}}",
                    Encoding.UTF8,
                    "application/json"),
            };
        }

        byte[] content = await File.ReadAllBytesAsync(file, cancellationToken).ConfigureAwait(false);
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(content),
        };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            raw ? "application/octet-stream" : "application/json");
        return response;
    }
}
