using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Xml;
using Wintodo.Core.Services;

namespace Wintodo.Core.Tests;

public sealed class DiagnosticExceptionTests
{
    private const string PrivateMarkers = "token=SYNTHETIC-TOKEN password=SYNTHETIC-PASSWORD "
        + "person@example.invalid task=SYNTHETIC-TASK account=SYNTHETIC-ACCOUNT "
        + "https://example.invalid/SYNTHETIC-ENDPOINT C:\\Users\\SYNTHETIC-OWNER\\private.db";

    [Fact]
    public void Describe_UsesOnlyReviewedCodesForEverySupportedFailure()
    {
        (Exception Exception, string Type)[] cases =
        [
            (new OperationCanceledException(PrivateMarkers), "Cancelled"),
            (new HttpRequestException(PrivateMarkers), "Http"),
            (new SocketException(), "Socket"),
            (new UnauthorizedAccessException(PrivateMarkers), "AccessDenied"),
            (new InvalidDataException(PrivateMarkers), "InvalidData"),
            (new IOException(PrivateMarkers), "Io"),
            (new JsonException(PrivateMarkers), "Json"),
            (new XmlException(PrivateMarkers), "Xml"),
            (new FormatException(PrivateMarkers), "Format"),
            (new ArgumentException(PrivateMarkers, PrivateMarkers), "Argument"),
            (new ObjectDisposedException(PrivateMarkers, PrivateMarkers), "Disposed"),
            (new InvalidOperationException(PrivateMarkers), "InvalidOperation"),
            (new NotSupportedException(PrivateMarkers), "Unsupported"),
            (new Exception(PrivateMarkers, new IOException(PrivateMarkers)), "Other"),
        ];
        foreach ((Exception exception, string type) in cases)
        {
            exception.Data[PrivateMarkers] = PrivateMarkers;
            exception.Source = PrivateMarkers;
            Assert.Equal("TYPE=" + type, DiagnosticException.Describe(exception));
        }
    }

    [Fact]
    public void Describe_NeverEvaluatesCustomMessagesOrStringRepresentations()
    {
        Assert.Equal("TYPE=Other", DiagnosticException.Describe(new UntrustedException()));
        Assert.Equal("TYPE=Other;STATUS=403",
            DiagnosticException.Describe(new UntrustedException(), HttpStatusCode.Forbidden));
    }

    [Theory]
    [InlineData(100, "TYPE=Http;STATUS=100")]
    [InlineData(401, "TYPE=Http;STATUS=401")]
    [InlineData(599, "TYPE=Http;STATUS=599")]
    [InlineData(0, "TYPE=Http")]
    [InlineData(99, "TYPE=Http")]
    [InlineData(600, "TYPE=Http")]
    [InlineData(int.MaxValue, "TYPE=Http")]
    public void Describe_StatusIsBoundedAndContainsNoServerText(int status, string expected)
    {
        var exception = new HttpRequestException(PrivateMarkers,
            new Exception(PrivateMarkers), (HttpStatusCode)status);
        Assert.Equal(expected, DiagnosticException.Describe(exception));
    }

    private sealed class UntrustedException : Exception
    {
        public override string Message => throw new InvalidOperationException("Message was evaluated.");
        public override string ToString() => throw new InvalidOperationException("ToString was evaluated.");
    }
}
