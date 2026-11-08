using System.Net;
using System.Net.Sockets;
using System.Text;
using Wintodo.Core.Models;
using Wintodo.Sync;

namespace Wintodo.Sync.Tests;

public sealed class CalDavNetworkStreamTests
{
    [Theory]
    [InlineData("slow")]
    [InlineData("truncated")]
    [InlineData("chunked")]
    [InlineData("unknown")]
    public async Task ProductionHandlerHasBoundedBodyReads(string mode)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var origin = new Uri("http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/");
        Task server = ServeAsync(listener, mode, stop.Token);
        try
        {
            using HttpClient http = CalDavClient.CreateHttpClient();
            if (mode == "slow") http.Timeout = TimeSpan.FromMilliseconds(250);
            var account = new Account { ServerUrl = origin.AbsoluteUri, Username = "synthetic" };
            Task operation = new CalDavClient(http).GetTaskAsync(account, "synthetic", new Uri(origin, "task.ics"), TestContext.Current.CancellationToken);
            if (mode == "slow")
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            else if (mode == "truncated")
                await Assert.ThrowsAnyAsync<IOException>(() => operation);
            else
                await Assert.ThrowsAnyAsync<InvalidDataException>(() => operation);
        }
        finally
        {
            await stop.CancelAsync();
            listener.Stop();
            await server;
        }
    }

    private static async Task ServeAsync(TcpListener listener, string mode, CancellationToken token)
    {
        try
        {
            using TcpClient connection = await listener.AcceptTcpClientAsync(token);
            using NetworkStream stream = connection.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
            while (await reader.ReadLineAsync(token) is { Length: > 0 }) { }
            string header = mode is "slow" or "truncated" ? "Content-Length: 100\r\n"
                : mode == "chunked" ? "Transfer-Encoding: chunked\r\n" : "";
            await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nConnection: close\r\n" + header + "\r\n"), token);
            if (mode is "slow" or "truncated")
            {
                await stream.WriteAsync(Encoding.ASCII.GetBytes("partial"), token);
                if (mode == "slow") await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return;
            }
            byte[] body = new byte[1024 * 1024];
            for (int i = 0; i < 33; i++)
            {
                if (mode == "chunked") await stream.WriteAsync(Encoding.ASCII.GetBytes("100000\r\n"), token);
                await stream.WriteAsync(body, token);
                if (mode == "chunked") await stream.WriteAsync(Encoding.ASCII.GetBytes("\r\n"), token);
            }
            if (mode == "chunked") await stream.WriteAsync(Encoding.ASCII.GetBytes("0\r\n\r\n"), token);
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException) { }
    }
}
