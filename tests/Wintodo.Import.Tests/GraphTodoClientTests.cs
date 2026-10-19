using System.Net;
using Wintodo.Import.MicrosoftTodo;

namespace Wintodo.Import.Tests;

public sealed class GraphTodoClientTests
{
    [Theory]
    [InlineData("11111111-1111-1111-1111-111111111111", true)]
    [InlineData("9188040d-6c67-4c5b-b112-36a304b66dad", false)]
    [InlineData("not-a-tenant", false)]
    public void AdminConsentIsEnabledOnlyByWorkTenantClaim(string tenant, bool enabled)
    {
        string payload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("{\"tid\":\"" + tenant + "\"}"));
        var client = new GraphTodoClient(new HttpClient(), "header." + payload + ".signature");
        Assert.Equal(enabled, client.WorkTenantId is not null);
    }

    [Theory]
    [InlineData("same", true)]
    [InlineData("different", false)]
    [InlineData("", false)]
    public async Task ResumeChecksOnlyAccountIdentity(string id, bool expected)
    {
        var handler = new ScriptedHandler(_ => ScriptedHandler.Json(HttpStatusCode.OK, "{\"id\":\"" + id + "\"}"));
        var client = new GraphTodoClient(new HttpClient(handler), "new-token");
        var export = new MicrosoftTodoExport { User = new GraphUser { Id = "same" } };
        Assert.Equal(expected, await MicrosoftTodoReader.CanResumeAsync(client, export, TestContext.Current.CancellationToken));
        Assert.Equal("/v1.0/me", Assert.Single(handler.Requests).AbsolutePath);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LargeAttachmentStreamsWithIncrementalProgress(bool fallback)
    {
        byte[] expected = Enumerable.Range(0, 200_000).Select(i => (byte)i).ToArray();
        Func<HttpRequestMessage, HttpResponseMessage> body = _ => fallback
            ? ScriptedHandler.Json(HttpStatusCode.OK, "{\"contentBytes\":\"" + Convert.ToBase64String(expected) + "\"}")
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(expected) };
        var handler = fallback
            ? new ScriptedHandler(_ => ScriptedHandler.Json(HttpStatusCode.NotFound, "{}"), body)
            : new ScriptedHandler(body);
        using var target = new MemoryStream();
        var progress = new List<long>();
        long bytes = await new GraphTodoClient(new HttpClient(handler), "fixture").CopyAttachmentContentAsync(
            "L1", "T1", "A1", target, progress.Add, TestContext.Current.CancellationToken);
        Assert.Equal(expected.Length, bytes);
        Assert.Equal(expected, target.ToArray());
        Assert.True(progress.Count > 1);
        Assert.Equal(bytes, progress[^1]);
    }
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
        var retries = new List<TimeSpan>();
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

        client.Retrying = retries.Add;
        GraphUser user = await client.GetMeAsync(TestContext.Current.CancellationToken);

        Assert.Equal("u1", user.Id);
        Assert.Equal([TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(2)], delays);
        Assert.Equal(delays, retries);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task AttachmentContent_UnsupportedRawFallsBackToContentBytes(HttpStatusCode rawStatus)
    {
        var handler = new ScriptedHandler(
            _ => ScriptedHandler.Json(rawStatus, "{}"),
            _ => ScriptedHandler.Json(HttpStatusCode.OK, """
                {"id":"A1","contentBytes":"AQID/w=="}
                """));
        var client = new GraphTodoClient(new HttpClient(handler), "token");

        byte[] content = await client.GetAttachmentContentAsync("L1", "T1", "A1", TestContext.Current.CancellationToken);

        Assert.Equal(new byte[] { 1, 2, 3, 255 }, content);
        Assert.Equal(2, handler.Requests.Count);
        Assert.EndsWith("/attachments/A1/$value", handler.Requests[0].AbsolutePath);
        Assert.EndsWith("/attachments/A1", handler.Requests[1].AbsolutePath);
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
        Assert.False(exception.IsSignInExpired);
    }

    [Fact]
    public async Task ExpiredToken_IsReportedAsAnExpiredSignInWithoutFallback()
    {
        // Seen live: an import started long after sign-in got 401 on the raw
        // attachment download. It must not fall back to contentBytes with the
        // same token, and it must not read as "access denied".
        var handler = new ScriptedHandler(_ => ScriptedHandler.Json(
            HttpStatusCode.Unauthorized,
            """{"error":{"code":"InvalidAuthenticationToken","message":"Access token has expired or is not yet valid."}}"""));
        var client = new GraphTodoClient(new HttpClient(handler), "token");

        GraphAuthorizationException exception = await Assert.ThrowsAsync<GraphAuthorizationException>(
            () => client.GetAttachmentContentAsync("L1", "T1", "A1", TestContext.Current.CancellationToken));

        Assert.True(exception.IsSignInExpired);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task FixtureExpiredSignIn_ReadsEverythingButRejectsAttachmentContent()
    {
        var client = new GraphTodoClient(
            new HttpClient(new FixtureGraphHandler(TestSupport.FixturePath("basic"), rejectAttachmentContent: true)),
            "fixture-token");

        MicrosoftTodoExport export = await new MicrosoftTodoReader(client)
            .ReadAsync(cancellationToken: TestContext.Current.CancellationToken);
        GraphAuthorizationException exception = await Assert.ThrowsAsync<GraphAuthorizationException>(
            () => client.GetAttachmentContentAsync("L-default", "T3", "A1", TestContext.Current.CancellationToken));

        Assert.Equal(4, export.Lists.Count);
        Assert.True(exception.IsSignInExpired);
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
