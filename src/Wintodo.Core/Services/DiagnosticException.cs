using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Xml;

namespace Wintodo.Core.Services;

/// <summary>
/// Describes failures without evaluating messages, custom type names, stack
/// traces, Data or inner exceptions. These values may contain private content.
/// Callers supply a constant operation label separately.
/// </summary>
public static class DiagnosticException
{
    public static string Describe(Exception exception, HttpStatusCode? statusCode = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        string type = exception switch
        {
            OperationCanceledException => "Cancelled",
            HttpRequestException => "Http",
            SocketException => "Socket",
            UnauthorizedAccessException => "AccessDenied",
            InvalidDataException => "InvalidData",
            IOException => "Io",
            JsonException => "Json",
            XmlException => "Xml",
            FormatException => "Format",
            ArgumentException => "Argument",
            ObjectDisposedException => "Disposed",
            InvalidOperationException => "InvalidOperation",
            NotSupportedException => "Unsupported",
            _ => "Other",
        };
        statusCode ??= (exception as HttpRequestException)?.StatusCode;
        int status = (int)(statusCode ?? 0);
        return status is >= 100 and <= 599
            ? "TYPE=" + type + ";STATUS=" + status.ToString(CultureInfo.InvariantCulture)
            : "TYPE=" + type;
    }
}
