using System.Net;

namespace Wintodo.Sync;

public sealed class CalDavProtocolException : Exception
{
    public CalDavProtocolException(
        string message,
        HttpStatusCode statusCode,
        string? responseBody = null)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public HttpStatusCode StatusCode { get; }

    public string? ResponseBody { get; }
}
