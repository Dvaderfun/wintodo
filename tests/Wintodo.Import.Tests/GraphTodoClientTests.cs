using System.Net;
using Wintodo.Import.MicrosoftTodo;

namespace Wintodo.Import.Tests;

public sealed class GraphTodoClientTests
{
    [Fact]
    public async Task Reader_FollowsPagingAndLoadsStepsLinksAndAttachments()
    {
        MicrosoftTodoExport export = await TestSupport.ReadFixtureAsync();

        Assert.Equal("user-0001", export.User.Id);
        Assert.Equal(["L-default", "L-groc", "L-flag", "L-team"], export.Lists.Select(list => list.List.Id));
        MicrosoftTodoListData defaults = export.Lists[0];
        Assert.Equal(["T1", "T2", "T3"], defaults.Tasks.Select(task => task.Id));
        Assert.Equal(2, defaults.Tasks[0].ChecklistItems!.Count);
        Assert.Equal("figures.csv", Assert.Single(defaults.Tasks[2].Attachments).Name);
        Assert.Empty(export.Lists[3].Tasks);
    }

    [Fact]
    public async Task Client_OnlySendsGetRequests()
    {
        // Sign-in asks for Tasks.ReadWrite (ADR-016 amendment 1); the import
        // must still never change anything in Microsoft To Do.
        var recorder = new MethodRecorder(new FixtureGraphHandler(TestSupport.FixturePath("basic")));
        var client = new GraphTodoClient(new HttpClient(recorder), "token");

        await new MicrosoftTodoReader(client).ReadAsync(cancellationToken: TestContext.Current.CancellationToken);
        await client.GetAttachmentContentAsync("L-default", "T3", "A1", TestContext.Current.CancellationToken);

        Assert.NotEmpty(recorder.Methods);
        Assert.All(recorder.Methods, method => Assert.Equal(HttpMethod.Get, method));
    }

    private sealed class MethodRecorder(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        internal List<HttpMethod> Methods { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Methods.Add(request.Method);
            return base.SendAsync(request, cancellationToken);
        }
    }

    [Fact]
    public async Task Lists_AreReadThroughDeltaUntilTheDeltaLink()
    {
        var handler = new ScriptedHandler(
            _ => ScriptedHandler.Json(HttpStatusCode.OK, """
                {"@odata.nextLink":"https://graph.microsoft.com/v1.0/me/todo/lists/delta?$skiptoken=a",
                 "value":[{"id":"L1","displayName":"Tasks","wellknownListName":"defaultList"}]}
                """),
            _ => ScriptedHandler.Json(HttpStatusCode.OK, """
                {"@odata.deltaLink":"https://graph.microsoft.com/v1.0/me/todo/lists/delta?$deltatoken=b",
                 "value":[{"id":"L2","displayName":"♥ Sweetheart","wellknownListName":"none"},
                          {"id":"L3","@removed":{"reason":"deleted"}}]}
                """));
        var client = new GraphTodoClient(new HttpClient(handler), "token");

        List<GraphTodoList> lists = await client.GetListsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["L1", "L2"], lists.Select(list => list.Id));
        Assert.Equal("/v1.0/me/todo/lists/delta", handler.Requests[0].AbsolutePath);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task AttachmentContent_IsReadRaw()
    {
        byte[] content = await TestSupport.FixtureClient().GetAttachmentContentAsync(
            "L-default", "T3", "A1", TestContext.Current.CancellationToken);

        Assert.Equal("quarter,revenue\nq3,1200\n", System.Text.Encoding.UTF8.GetString(content));
    }

    [Fact]
    public async Task Throttling_HonoursRetryAfterThenSucceeds()
    {
        var delays = new List<TimeSpan>();
        var handler = new ScriptedHandler(
            _ =>
            {
                HttpResponseMessage throttled = ScriptedHandler.Json(HttpStatusCode.TooManyRequests, "{}");
                throttled.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
                return throttled;
            },
            _ => ScriptedHandler.Json(HttpStatusCode.ServiceUnavailable, "{}"),
            _ => ScriptedHandler.Json(HttpStatusCode.OK, """{"id":"u1","mail":"a@example.com"}"""));
        var client = new GraphTodoClient(
            new HttpClient(handler),
            "token",
            delay: (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

        GraphUser user = await client.GetMeAsync(TestContext.Current.CancellationToken);

        Assert.Equal("u1", user.Id);
        Assert.Equal([TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(2)], delays);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task Throttling_RetryAfterDateIsMeasuredFromTheClock()
    {
        var clock = new FakeClock();
        var delays = new List<TimeSpan>();
        var handler = new ScriptedHandler(
            _ =>
            {
                HttpResponseMessage throttled = ScriptedHandler.Json(HttpStatusCode.TooManyRequests, "{}");
                throttled.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
                    clock.UtcNow.AddSeconds(12));
                return throttled;
            },
            _ => ScriptedHandler.Json(HttpStatusCode.OK, """{"id":"u1"}"""));
        var client = new GraphTodoClient(
            new HttpClient(handler),
            "token",
            delay: (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            },
            clock: clock);

        await client.GetMeAsync(TestContext.Current.CancellationToken);

        Assert.Equal([TimeSpan.FromSeconds(12)], delays);
    }

    [Fact]
    public async Task Unauthorized_RaisesAuthorizationErrorWithGraphCode()
    {
        var handler = new ScriptedHandler(_ => ScriptedHandler.Json(
            HttpStatusCode.Forbidden,
            """{"error":{"code":"Authorization_RequestDenied","message":"Admin consent required."}}"""));
        var client = new GraphTodoClient(new HttpClient(handler), "token");

        GraphAuthorizationException exception = await Assert.ThrowsAsync<GraphAuthorizationException>(
            () => client.GetListsAsync(TestContext.Current.CancellationToken));

        Assert.Equal("Authorization_RequestDenied", exception.Code);
        Assert.Equal("Admin consent required.", exception.Message);
    }

    [Fact]
    public async Task UnsupportedExpand_FallsBackToPerTaskRequests()
    {
        var handler = new ScriptedHandler(request =>
        {
            string uri = request.RequestUri!.ToString();
            if (uri.Contains("$expand", StringComparison.Ordinal))
            {
                return ScriptedHandler.Json(HttpStatusCode.BadRequest, """{"error":{"code":"BadRequest"}}""");
            }

            if (uri.Contains("/checklistItems", StringComparison.Ordinal))
            {
                return ScriptedHandler.Json(HttpStatusCode.OK, """{"value":[{"id":"C1","displayName":"Step","isChecked":false}]}""");
            }

            return uri.Contains("/linkedResources", StringComparison.Ordinal)
                ? ScriptedHandler.Json(HttpStatusCode.OK, """{"value":[]}""")
                : ScriptedHandler.Json(HttpStatusCode.OK, """{"value":[{"id":"T1","title":"Task"}]}""");
        });
        var client = new GraphTodoClient(new HttpClient(handler), "token");

        List<GraphTodoTask> tasks = await client.GetTasksAsync("L1", TestContext.Current.CancellationToken);

        Assert.Equal("Step", Assert.Single(Assert.Single(tasks).ChecklistItems!).DisplayName);
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task NextLinkToAnotherHost_IsRefusedSoTheTokenStaysWithGraph()
    {
        var handler = new ScriptedHandler(_ => ScriptedHandler.Json(
            HttpStatusCode.OK,
            """{"value":[],"@odata.nextLink":"https://evil.example.com/steal"}"""));
        var client = new GraphTodoClient(new HttpClient(handler), "token");

        await Assert.ThrowsAsync<GraphRequestException>(() => client.GetListsAsync(TestContext.Current.CancellationToken));
        Assert.Single(handler.Requests);
    }
}
