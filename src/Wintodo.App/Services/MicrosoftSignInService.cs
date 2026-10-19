using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Security.Authentication.OAuth;

namespace Wintodo.Services;

/// <summary>
/// Microsoft identity platform sign-in via the Windows App SDK OAuth2Manager:
/// authorization code + PKCE in the system browser, redirect to a loopback
/// port this process listens on (RFC 8252). Only <c>Tasks.ReadWrite</c> and
/// <c>User.Read</c> are requested, and the access token is never stored.
/// </summary>
internal static class MicrosoftSignInService
{
    private const string Authority = "https://login.microsoftonline.com/common/oauth2/v2.0/";
    // Tasks.ReadWrite only because Microsoft's list delta function requires it
    // (ADR-016 amendment 1); GraphTodoClient sends GET requests only.
    private const string Scopes = "Tasks.ReadWrite User.Read";
    private const int MaxRequestBytes = 16 * 1024;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    internal static async Task<string> SignInAsync(
        string clientId,
        string completePageText,
        CancellationToken cancellationToken)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var redirectUri = new Uri($"http://localhost:{port}/");

        AuthRequestParams authParams = AuthRequestParams.CreateForAuthorizationCodeRequest(clientId, redirectUri);
        authParams.Scope = Scopes;
        authParams.AdditionalParams["prompt"] = "select_account";

        using var listenerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task redirectTask = AcceptRedirectAsync(listener, port, completePageText, listenerCancellation.Token);
        AuthRequestResult authResult;
        try
        {
            authResult = await OAuth2Manager.RequestAuthWithParamsAsync(
                App.WindowId,
                new Uri(Authority + "authorize"),
                authParams).AsTask(cancellationToken);
        }
        finally
        {
            listenerCancellation.Cancel();
            listener.Stop();
            try
            {
                await redirectTask;
            }
            catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException)
            {
            }
        }

        if (authResult.Response is null)
        {
            string? error = authResult.Failure?.Error;
            throw new MicrosoftSignInException(
                string.Equals(error, "access_denied", StringComparison.Ordinal)
                    ? MicrosoftSignInFailure.Cancelled
                    : MicrosoftSignInFailure.Failed,
                authResult.Failure?.ErrorDescription ?? error);
        }

        TokenRequestParams tokenParams = TokenRequestParams.CreateForAuthorizationCodeRequest(authResult.Response);
        tokenParams.ClientId = clientId;
        tokenParams.Scope = Scopes;
        TokenRequestResult tokenResult = await OAuth2Manager.RequestTokenAsync(
            new Uri(Authority + "token"),
            tokenParams).AsTask(cancellationToken);
        string? accessToken = tokenResult.Response?.AccessToken;
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new MicrosoftSignInException(
                MicrosoftSignInFailure.Failed,
                tokenResult.Failure?.ErrorDescription ?? tokenResult.Failure?.Error);
        }

        return accessToken;
    }

    private static async Task AcceptRedirectAsync(
        TcpListener listener,
        int port,
        string completePageText,
        CancellationToken cancellationToken)
    {
        // Any local process can reach the port, so a stalled, broken or
        // unmatched request (wrong state) must not end the wait for the
        // browser's real redirect.
        while (!cancellationToken.IsCancellationRequested)
        {
            using TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken);
            using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            requestTimeout.CancelAfter(RequestTimeout);
            try
            {
                await using NetworkStream stream = client.GetStream();
                string? target = await ReadRequestTargetAsync(stream, requestTimeout.Token);
                bool isRedirect = target is not null
                    && target.StartsWith("/?", StringComparison.Ordinal)
                    && (target.Contains("code=", StringComparison.Ordinal)
                        || target.Contains("error=", StringComparison.Ordinal));
                await WriteResponseAsync(stream, isRedirect, completePageText, requestTimeout.Token);
                if (isRedirect)
                {
                    bool matched = OAuth2Manager.CompleteAuthRequest(new Uri($"http://localhost:{port}{target}"));
                    Trace.WriteLine($"WINTODO_SIGNIN_REDIRECT_MATCHED={matched}");
                    if (matched)
                    {
                        return;
                    }
                }
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested
                && exception is IOException or SocketException or OperationCanceledException or UriFormatException)
            {
                Trace.WriteLine($"WINTODO_SIGNIN_REDIRECT_IGNORED={exception.GetType().Name}");
            }
        }
    }

    private static async Task<string?> ReadRequestTargetAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[MaxRequestBytes];
        int total = 0;
        while (total < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (buffer.AsSpan(0, total).IndexOf("\r\n\r\n"u8) >= 0)
            {
                break;
            }
        }

        string head = Encoding.ASCII.GetString(buffer, 0, total);
        int lineEnd = head.IndexOf("\r\n", StringComparison.Ordinal);
        string[] parts = (lineEnd < 0 ? head : head[..lineEnd]).Split(' ');
        return parts.Length >= 2 && parts[0] == "GET" ? parts[1] : null;
    }

    private static async Task WriteResponseAsync(
        NetworkStream stream,
        bool isRedirect,
        string completePageText,
        CancellationToken cancellationToken)
    {
        string body = isRedirect
            ? "<!doctype html><html><head><meta charset=\"utf-8\"><title>Wintodo</title></head>"
                + "<body style=\"font-family:'Segoe UI',sans-serif;margin:48px\"><p>"
                + WebUtility.HtmlEncode(completePageText)
                + "</p></body></html>"
            : string.Empty;
        byte[] content = Encoding.UTF8.GetBytes(body);
        string status = isRedirect ? "200 OK" : "404 Not Found";
        byte[] header = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {content.Length}\r\n"
            + "Cache-Control: no-store\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(content, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}
