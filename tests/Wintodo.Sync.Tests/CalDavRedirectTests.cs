using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Wintodo.Core.Models;
using Wintodo.Core.Services;
using Wintodo.Sync;

namespace Wintodo.Sync.Tests;

public sealed class CalDavRedirectTests
{
    private const string SyntheticSecret = "synthetic-caldav-secret";
    private const string Calendar = "BEGIN:VCALENDAR\r\nBEGIN:VTODO\r\nUID:synthetic-task\r\nSUMMARY:synthetic-private-task\r\nEND:VTODO\r\nEND:VCALENDAR\r\n";

    [Theory]
    [InlineData(HttpStatusCode.MovedPermanently, false)]
    [InlineData(HttpStatusCode.MovedPermanently, true)]
    [InlineData(HttpStatusCode.Found, false)]
    [InlineData(HttpStatusCode.Found, true)]
    [InlineData(HttpStatusCode.SeeOther, false)]
    [InlineData(HttpStatusCode.SeeOther, true)]
    [InlineData(HttpStatusCode.TemporaryRedirect, false)]
    [InlineData(HttpStatusCode.TemporaryRedirect, true)]
    [InlineData(HttpStatusCode.PermanentRedirect, false)]
    [InlineData(HttpStatusCode.PermanentRedirect, true)]
    public async Task ProductionTransportRejectsRedirectedReads(HttpStatusCode status, bool sameOrigin)
    {
        await using var foreign = new LoopbackServer(_ => Response(HttpStatusCode.OK));
        string location = sameOrigin ? "/redirected.ics" : new Uri(foreign.BaseUri, "redirected.ics").AbsoluteUri;
        await using var approved = new LoopbackServer(_ => Response(status, location));
        using HttpClient http = CalDavClient.CreateHttpClient();
        var client = new CalDavClient(http);

        Exception? failure = await Record.ExceptionAsync(() => client.GetTaskAsync(
            CreateAccount(approved.BaseUri), SyntheticSecret, new Uri(approved.BaseUri, "task.ics"),
            TestContext.Current.CancellationToken));

        Assert.Empty(foreign.Requests);
        CapturedRequest request = Assert.Single(approved.Requests);
        Assert.Equal("GET /dav/task.ics HTTP/1.1", request.Target);
        Assert.Equal(ExpectedAuthorization, request.Authorization);
        Assert.Empty(request.Body);
        AssertRedirectFailure(status, failure);
    }

    [Theory]
    [InlineData(HttpStatusCode.TemporaryRedirect, false)]
    [InlineData(HttpStatusCode.TemporaryRedirect, true)]
    [InlineData(HttpStatusCode.PermanentRedirect, false)]
    [InlineData(HttpStatusCode.PermanentRedirect, true)]
    public async Task ProductionTransportNeverReplaysPutBodies(HttpStatusCode status, bool sameOrigin)
    {
        await using var foreign = new LoopbackServer(_ => Response(HttpStatusCode.NoContent));
        string location = sameOrigin ? "/redirected.ics" : new Uri(foreign.BaseUri, "redirected.ics").AbsoluteUri;
        await using var approved = new LoopbackServer(_ => Response(status, location));
        using HttpClient http = CalDavClient.CreateHttpClient();
        var client = new CalDavClient(http);
        byte[] bytes = Encoding.UTF8.GetBytes(Calendar);

        Exception? failure = await Record.ExceptionAsync(() => client.PutTaskAsync(
            CreateAccount(approved.BaseUri), SyntheticSecret, new Uri(approved.BaseUri, "task.ics"),
            bytes, "\"v1\"", TestContext.Current.CancellationToken));

        Assert.Empty(foreign.Requests);
        CapturedRequest request = Assert.Single(approved.Requests);
        Assert.Equal("PUT /dav/task.ics HTTP/1.1", request.Target);
        Assert.Equal(ExpectedAuthorization, request.Authorization);
        Assert.Equal("\"v1\"", request.IfMatch);
        Assert.Equal(bytes, request.Body);
        AssertRedirectFailure(status, failure);
    }

    [Theory]
    [InlineData("discover", "PROPFIND")]
    [InlineData("collections", "PROPFIND")]
    [InlineData("delta", "REPORT")]
    [InlineData("create-collection", "MKCALENDAR")]
    [InlineData("rename-collection", "PROPPATCH")]
    [InlineData("delete-collection", "DELETE")]
    [InlineData("delete-task", "DELETE")]
    public async Task EveryCalDavOperationRejectsRedirects(string operation, string method)
    {
        await using var foreign = new LoopbackServer(_ => Response(HttpStatusCode.NoContent));
        await using var approved = new LoopbackServer(_ => Response(HttpStatusCode.TemporaryRedirect,
            new Uri(foreign.BaseUri, "redirected").AbsoluteUri));
        using HttpClient http = CalDavClient.CreateHttpClient();
        var client = new CalDavClient(http);
        Account account = CreateAccount(approved.BaseUri);
        Uri resource = new(approved.BaseUri, "task.ics");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        Exception? failure = await Record.ExceptionAsync(async () =>
        {
            switch (operation)
            {
                case "discover":
                    await client.DiscoverAsync(account, SyntheticSecret, cancellationToken);
                    break;
                case "collections":
                    await client.GetCollectionsAsync(account, SyntheticSecret, cancellationToken);
                    break;
                case "delta":
                    await client.GetCollectionDeltaAsync(account, SyntheticSecret,
                        new TodoList { RemoteUrl = approved.BaseUri.AbsoluteUri, SyncToken = "synthetic-token" },
                        cancellationToken);
                    break;
                case "create-collection":
                    await client.CreateCollectionAsync(account, SyntheticSecret, "synthetic-list", "Synthetic list", cancellationToken);
                    break;
                case "rename-collection":
                    await client.RenameCollectionAsync(account, SyntheticSecret, resource, "Synthetic list", cancellationToken);
                    break;
                case "delete-collection":
                    await client.DeleteCollectionAsync(account, SyntheticSecret, resource, cancellationToken);
                    break;
                case "delete-task":
                    await client.DeleteTaskAsync(account, SyntheticSecret, resource, "\"v1\"", cancellationToken);
                    break;
                default:
                    throw new InvalidOperationException("Unknown test operation.");
            }
        });

        Assert.Empty(foreign.Requests);
        CapturedRequest request = Assert.Single(approved.Requests);
        Assert.StartsWith(method + " ", request.Target);
        Assert.Equal(ExpectedAuthorization, request.Authorization);
        AssertRedirectFailure(HttpStatusCode.TemporaryRedirect, failure);
    }

    [Theory]
    [InlineData(HttpStatusCode.TemporaryRedirect)]
    [InlineData(HttpStatusCode.PermanentRedirect)]
    public async Task RedirectLoopsStopAfterTheOriginalRequest(HttpStatusCode status)
    {
        await using var approved = new LoopbackServer(_ => Response(status, "/dav/task.ics"));
        using HttpClient http = CalDavClient.CreateHttpClient();
        var client = new CalDavClient(http);

        Exception? failure = await Record.ExceptionAsync(() => client.GetTaskAsync(
            CreateAccount(approved.BaseUri), SyntheticSecret, new Uri(approved.BaseUri, "task.ics"),
            TestContext.Current.CancellationToken));

        Assert.Single(approved.Requests);
        AssertRedirectFailure(status, failure);
    }

    [Theory]
    [InlineData(HttpStatusCode.MultipleChoices)]
    [InlineData(HttpStatusCode.NotModified)]
    [InlineData(HttpStatusCode.UseProxy)]
    [InlineData((HttpStatusCode)399)]
    public async Task RedirectResponsesAreDisposedWithoutReadingTheirBody(HttpStatusCode status)
    {
        using var handler = new RedirectHandler(status);
        using var http = new HttpClient(handler);
        var client = new CalDavClient(http);

        Exception? failure = await Record.ExceptionAsync(() => client.GetTaskAsync(
            CreateAccount(new Uri("https://dav.example/dav/")), SyntheticSecret,
            new Uri("https://dav.example/dav/task.ics"), TestContext.Current.CancellationToken));

        AssertRedirectFailure(status, failure);
        Assert.Equal(1, handler.RequestCount);
        Assert.False(handler.Content.WasRead);
        Assert.True(handler.Content.WasDisposed);
    }

    [Theory]
    [InlineData("discover")]
    [InlineData("collections")]
    [InlineData("delta")]
    [InlineData("create-collection")]
    [InlineData("rename-collection")]
    [InlineData("delete-collection")]
    [InlineData("delete-task")]
    [InlineData("put-task")]
    public async Task RedirectBodiesAreNotBufferedByWriteOrXmlOperations(string operation)
    {
        using var handler = new RedirectHandler(HttpStatusCode.TemporaryRedirect);
        using var http = new HttpClient(handler);
        var client = new CalDavClient(http);
        Account account = CreateAccount(new Uri("https://dav.example/dav/"));
        Uri resource = new("https://dav.example/dav/task.ics");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        Exception? failure = await Record.ExceptionAsync(async () =>
        {
            switch (operation)
            {
                case "discover":
                    await client.DiscoverAsync(account, SyntheticSecret, cancellationToken);
                    break;
                case "collections":
                    await client.GetCollectionsAsync(account, SyntheticSecret, cancellationToken);
                    break;
                case "delta":
                    await client.GetCollectionDeltaAsync(account, SyntheticSecret,
                        new TodoList { RemoteUrl = account.ServerUrl, SyncToken = "synthetic-token" }, cancellationToken);
                    break;
                case "create-collection":
                    await client.CreateCollectionAsync(account, SyntheticSecret, "synthetic-list", "Synthetic list", cancellationToken);
                    break;
                case "rename-collection":
                    await client.RenameCollectionAsync(account, SyntheticSecret, resource, "Synthetic list", cancellationToken);
                    break;
                case "delete-collection":
                    await client.DeleteCollectionAsync(account, SyntheticSecret, resource, cancellationToken);
                    break;
                case "delete-task":
                    await client.DeleteTaskAsync(account, SyntheticSecret, resource, "\"v1\"", cancellationToken);
                    break;
                case "put-task":
                    await client.PutTaskAsync(account, SyntheticSecret, resource,
                        Encoding.UTF8.GetBytes(Calendar), "\"v1\"", cancellationToken);
                    break;
                default:
                    throw new InvalidOperationException("Unknown test operation.");
            }
        });

        AssertRedirectFailure(HttpStatusCode.TemporaryRedirect, failure);
        Assert.Equal(1, handler.RequestCount);
        Assert.False(handler.Content.WasRead);
        Assert.True(handler.Content.WasDisposed);
    }

    [Fact]
    public async Task ProductionTransportPreservesCallerCancellation()
    {
        await using var approved = new LoopbackServer(_ => Response(HttpStatusCode.OK));
        using HttpClient http = CalDavClient.CreateHttpClient();
        var client = new CalDavClient(http);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetTaskAsync(
            CreateAccount(approved.BaseUri), SyntheticSecret, new Uri(approved.BaseUri, "task.ics"), cancellation.Token));

        Assert.Empty(approved.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.TemporaryRedirect)]
    [InlineData(HttpStatusCode.PermanentRedirect)]
    public async Task HttpsRedirectCannotDowngradeOrReplayTaskContent(HttpStatusCode status)
    {
        await using var destination = new LoopbackServer(_ => Response(HttpStatusCode.NoContent));
        await using var approved = new LoopbackServer(_ => Response(status,
            new Uri(destination.BaseUri, "redirected.ics").AbsoluteUri), useTls: true);
        using HttpClient http = CreateTlsTestClient(approved);
        var client = new CalDavClient(http);

        Exception? failure = await Record.ExceptionAsync(() => client.PutTaskAsync(
            CreateAccount(approved.BaseUri), SyntheticSecret, new Uri(approved.BaseUri, "task.ics"),
            Encoding.UTF8.GetBytes(Calendar), "\"v1\"", TestContext.Current.CancellationToken));

        Assert.Empty(destination.Requests);
        Assert.Equal(Calendar, Encoding.UTF8.GetString(Assert.Single(approved.Requests).Body));
        AssertRedirectFailure(status, failure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DirectLoopbackAndHttpsDiscoveryRemainSupported(bool useTls)
    {
        const string discovery = """
            <d:multistatus xmlns:d="DAV:" xmlns:c="urn:ietf:params:xml:ns:caldav">
              <d:response><d:propstat><d:prop>
                <d:current-user-principal><d:href>/principals/synthetic/</d:href></d:current-user-principal>
                <c:calendar-home-set><d:href>/calendars/synthetic/</d:href></c:calendar-home-set>
              </d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>
            </d:multistatus>
            """;
        await using var approved = new LoopbackServer(_ => Response(HttpStatusCode.MultiStatus, body: discovery), useTls);
        using HttpClient http = useTls ? CreateTlsTestClient(approved) : CalDavClient.CreateHttpClient();
        Assert.Equal(TimeSpan.FromSeconds(30), http.Timeout);
        var client = new CalDavClient(http);

        CalDavDiscovery result = await client.DiscoverAsync(
            CreateAccount(approved.BaseUri), SyntheticSecret, TestContext.Current.CancellationToken);

        Assert.Equal(new Uri(approved.BaseUri, "/principals/synthetic/"), result.PrincipalUrl);
        Assert.Equal(new Uri(approved.BaseUri, "/calendars/synthetic/"), result.HomeSetUrl);
        CapturedRequest request = Assert.Single(approved.Requests);
        Assert.Equal("PROPFIND /dav/ HTTP/1.1", request.Target);
        Assert.Equal(ExpectedAuthorization, request.Authorization);
        Assert.Equal("0", request.Depth);
    }

    [Fact]
    public async Task ProductionTransportStillRequiresTrustedHttpsCertificates()
    {
        await using var approved = new LoopbackServer(_ => Response(HttpStatusCode.OK), useTls: true);
        using HttpClient http = CalDavClient.CreateHttpClient();
        var client = new CalDavClient(http);

        HttpRequestException failure = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetTaskAsync(
            CreateAccount(approved.BaseUri), SyntheticSecret, new Uri(approved.BaseUri, "task.ics"),
            TestContext.Current.CancellationToken));

        Assert.IsType<AuthenticationException>(failure.InnerException);
        Assert.Empty(approved.Requests);
    }

    private static Account CreateAccount(Uri baseUri) => new()
    {
        ServerUrl = baseUri.AbsoluteUri,
        HomeSetUrl = baseUri.AbsoluteUri,
        Username = "synthetic-user",
    };

    private static string ExpectedAuthorization => "Basic " + Convert.ToBase64String(
        Encoding.UTF8.GetBytes("synthetic-user:" + SyntheticSecret));

    private static void AssertRedirectFailure(HttpStatusCode status, Exception? failure)
    {
        CalDavProtocolException exception = Assert.IsType<CalDavProtocolException>(failure);
        Assert.Equal(status, exception.StatusCode);
        Assert.Null(exception.ResponseBody);
        Assert.DoesNotContain("synthetic-private", exception.Message);
        Assert.DoesNotContain("redirected", exception.Message);
    }

    private static HttpClient CreateTlsTestClient(LoopbackServer server) => new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        // Trust only this test's in-memory certificate; never install it or relax production validation.
        ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
            certificate?.GetCertHashString() == server.Certificate!.GetCertHashString(),
    }) { Timeout = TimeSpan.FromSeconds(30) };

    private static string Response(HttpStatusCode status, string? location = null, string body = Calendar)
    {
        string locationHeader = location is null ? string.Empty : "Location: " + location + "\r\n";
        return $"HTTP/1.1 {(int)status} Response\r\n{locationHeader}" +
            $"Content-Type: text/calendar\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\n" +
            $"ETag: \"v2\"\r\nConnection: close\r\n\r\n{body}";
    }

    private sealed class RedirectHandler(HttpStatusCode status) : HttpMessageHandler
    {
        internal UnreadableContent Content { get; } = new();
        internal int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestCount++;
            var response = new HttpResponseMessage(status) { Content = Content };
            response.Headers.Location = new Uri("https://foreign.example/redirected?synthetic-private-token");
            return Task.FromResult(response);
        }
    }

    private sealed class UnreadableContent : HttpContent
    {
        internal bool WasRead { get; private set; }
        internal bool WasDisposed { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            WasRead = true;
            throw new InvalidOperationException("synthetic-private-response-body-must-not-be-read");
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed record CapturedRequest(string Target, string? Authorization, string? IfMatch, string? Depth, byte[] Body);

    private sealed class LoopbackServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _lifetime =
            CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        private readonly Func<CapturedRequest, string> _response;
        private readonly Task _serve;

        internal Uri BaseUri { get; }
        internal X509Certificate2? Certificate { get; }
        internal ConcurrentQueue<CapturedRequest> Requests { get; } = new();

        internal LoopbackServer(Func<CapturedRequest, string> response, bool useTls = false)
        {
            _response = response;
            try
            {
                if (useTls)
                {
                    using RSA key = RSA.Create(2048);
                    var request = new CertificateRequest("CN=synthetic-loopback", key,
                        HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                    DateTimeOffset now = new SystemClock().UtcNow;
                    using X509Certificate2 certificate = request.CreateSelfSigned(now.AddMinutes(-1), now.AddHours(1));
                    byte[] encoded = certificate.Export(X509ContentType.Pkcs12);
                    try
                    {
                        // Schannel requires a key container. DefaultKeySet removes this
                        // transient fixture key on disposal; do not persist it or install trust.
                        Certificate = X509CertificateLoader.LoadPkcs12(encoded, password: null,
                            X509KeyStorageFlags.DefaultKeySet);
                    }
                    finally { CryptographicOperations.ZeroMemory(encoded); }
                }
                _lifetime.CancelAfter(TimeSpan.FromSeconds(20));
                _listener.Start();
                string scheme = useTls ? "https" : "http";
                BaseUri = new Uri($"{scheme}://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/dav/");
                _serve = ServeAsync();
            }
            catch
            {
                Certificate?.Dispose();
                _listener.Stop();
                _lifetime.Dispose();
                throw;
            }
        }

        private async Task ServeAsync()
        {
            try
            {
                while (!_lifetime.IsCancellationRequested)
                {
                    using TcpClient client = await _listener.AcceptTcpClientAsync(_lifetime.Token);
                    await using NetworkStream network = client.GetStream();
                    if (Certificate is null)
                    {
                        await ServeRequestAsync(network);
                    }
                    else
                    {
                        await using var tls = new SslStream(network, leaveInnerStreamOpen: true);
                        try
                        {
                            await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                            {
                                ServerCertificate = Certificate,
                                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                            }, _lifetime.Token);
                            await ServeRequestAsync(tls);
                        }
                        catch (Exception exception) when (exception is AuthenticationException or IOException)
                        {
                            // An untrusted certificate is rejected before any authenticated HTTP request.
                        }
                    }
                }
            }
            catch (Exception exception) when (_lifetime.IsCancellationRequested
                && exception is OperationCanceledException or SocketException or IOException or ObjectDisposedException)
            {
            }
        }

        private async Task ServeRequestAsync(Stream stream)
        {
            var headerBytes = new List<byte>();
            byte[] next = new byte[1];
            while (headerBytes.Count < 32 * 1024)
            {
                if (await stream.ReadAsync(next, _lifetime.Token) == 0) return;
                headerBytes.Add(next[0]);
                int count = headerBytes.Count;
                if (count >= 4 && headerBytes[count - 4] == '\r' && headerBytes[count - 3] == '\n'
                    && headerBytes[count - 2] == '\r' && headerBytes[count - 1] == '\n') break;
            }
            string[] lines = Encoding.ASCII.GetString(headerBytes.ToArray()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            string? authorization = null;
            string? ifMatch = null;
            string? depth = null;
            int contentLength = 0;
            foreach (string line in lines.Skip(1))
            {
                if (line.StartsWith("Authorization: ", StringComparison.OrdinalIgnoreCase)) authorization = line[15..];
                if (line.StartsWith("If-Match: ", StringComparison.OrdinalIgnoreCase)) ifMatch = line[10..];
                if (line.StartsWith("Depth: ", StringComparison.OrdinalIgnoreCase)) depth = line[7..];
                if (line.StartsWith("Content-Length: ", StringComparison.OrdinalIgnoreCase))
                    contentLength = int.Parse(line[16..], System.Globalization.CultureInfo.InvariantCulture);
            }
            if (contentLength > 128 * 1024) throw new InvalidOperationException("Test request exceeded its synthetic body budget.");
            byte[] body = new byte[contentLength];
            await stream.ReadExactlyAsync(body, _lifetime.Token);
            var request = new CapturedRequest(lines[0], authorization, ifMatch, depth, body);
            Requests.Enqueue(request);
            await stream.WriteAsync(Encoding.UTF8.GetBytes(_response(request)), _lifetime.Token);
        }

        public async ValueTask DisposeAsync()
        {
            await _lifetime.CancelAsync();
            _listener.Stop();
            try { await _serve; }
            finally
            {
                Certificate?.Dispose();
                _lifetime.Dispose();
            }
        }
    }
}
