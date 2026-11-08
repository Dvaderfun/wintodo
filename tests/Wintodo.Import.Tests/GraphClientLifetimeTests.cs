using System.Net;
using Wintodo.Import.MicrosoftTodo;

namespace Wintodo.Import.Tests;

public sealed class GraphClientLifetimeTests
{
    [Fact]
    public async Task EndingTheStandardClientLifetimeRevokesAuthenticatedRequests()
    {
        var handler = new ScriptedHandler(_ => ScriptedHandler.Json(HttpStatusCode.OK, "{\"id\":\"synthetic-account\"}"));
        using var transport = new HttpClient(handler);
        var client = new GraphTodoClient(transport, "synthetic-old-token");
        IDisposable lifetime = Assert.IsAssignableFrom<IDisposable>(client);
        lifetime.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.GetMeAsync(TestContext.Current.CancellationToken));
        Assert.Empty(handler.Requests);
    }
}
