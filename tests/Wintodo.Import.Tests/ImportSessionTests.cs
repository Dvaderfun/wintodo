using System.Net;
using Wintodo.Core.Import;
using Wintodo.Core.Models;
using Wintodo.Import.MicrosoftTodo;

namespace Wintodo.Import.Tests;

public sealed class ImportSessionTests
{
    [Fact]
    public async Task FinalDismissalRevokesAnIdleClientAndReader()
    {
        using var transport = new HttpClient(new ScriptedHandler(_ => ScriptedHandler.Json(HttpStatusCode.OK, "{\"id\":\"fixture\"}")));
        using var client = new GraphTodoClient(transport, "synthetic-old-token");
        var session = new MicrosoftTodoImportSession();
        session.Connect(client);
        Assert.NotNull(session.Reader);
        await session.EndAsync();
        Assert.Null(session.Reader);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.GetMeAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DismissalCancelsAndWaitsForInFlightReads()
    {
        var handler = new PausedHandler();
        using var transport = new HttpClient(handler);
        using var client = new GraphTodoClient(transport, "synthetic-token");
        var session = new MicrosoftTodoImportSession();
        session.Connect(client);
        CancellationToken work = session.BeginWork();
        Task read = client.GetMeAsync(work);
        await handler.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        Task ended = session.EndAsync();
        Assert.True(work.IsCancellationRequested);
        Assert.False(ended.IsCompleted);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        session.EndWork();
        await ended;
        Assert.Null(session.Reader);
    }

    [Fact]
    public async Task ClosingDuringSavingWaitsForCommittedData()
    {
        await using TempStore store = await TempStore.CreateAsync();
        var session = new MicrosoftTodoImportSession();
        CancellationToken work = session.BeginWork();
        Task ended = session.EndAsync();
        Assert.True(work.IsCancellationRequested);
        Assert.False(ended.IsCompleted);
        var snapshot = new ImportSnapshot { Provider = "mstodo", AccountKey = "synthetic-account" };
        var list = new ImportList { ExternalId = "list", DisplayName = "Tasks", Kind = ImportListKind.Default };
        list.Tasks.Add(new ImportTask { ExternalId = "task", Task = new TodoTask { Summary = "Committed synthetic task" } });
        snapshot.Lists.Add(list);
        ImportOutcome outcome = await store.Store.ApplyImportAsync(snapshot, new ImportOptions(), CancellationToken.None);
        Assert.Equal(1, outcome.NewTasks);
        Assert.False(ended.IsCompleted);
        session.EndWork();
        await ended;
        Assert.Equal(1, (await store.Store.GetStatsAsync(TestContext.Current.CancellationToken)).TaskCount);
    }

    [Fact]
    public async Task LateSignInCannotReattachAfterDismissal()
    {
        using var transport = new HttpClient(new ScriptedHandler(_ => ScriptedHandler.Json(HttpStatusCode.OK, "{}")));
        using var lateClient = new GraphTodoClient(transport, "late-synthetic-token");
        var session = new MicrosoftTodoImportSession();
        session.BeginWork();
        Task ended = session.EndAsync();
        Assert.ThrowsAny<OperationCanceledException>(() => session.Connect(lateClient));
        session.EndWork();
        await ended;
        await Assert.ThrowsAsync<ObjectDisposedException>(() => lateClient.GetMeAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void CancelledSignInCannotReattachAClient()
    {
        using var transport = new HttpClient(new ScriptedHandler(_ => ScriptedHandler.Json(HttpStatusCode.OK, "{}")));
        using var client = new GraphTodoClient(transport, "synthetic-token");
        var session = new MicrosoftTodoImportSession();
        session.BeginWork();
        session.CancelWork();
        Assert.ThrowsAny<OperationCanceledException>(() => session.Connect(client));
        Assert.Null(session.Reader);
        session.EndWork();
    }

    [Fact]
    public async Task ExpirationDropsOldTokenAndFreshSignInCanResumeOnSharedTransport()
    {
        var handler = new ScriptedHandler(request =>
        {
            Assert.Equal("fresh-synthetic-token", request.Headers.Authorization?.Parameter);
            return ScriptedHandler.Json(HttpStatusCode.OK, "{\"id\":\"same-account\"}");
        });
        using var transport = new HttpClient(handler);
        using var oldClient = new GraphTodoClient(transport, "old-synthetic-token");
        using var freshClient = new GraphTodoClient(transport, "fresh-synthetic-token");
        var session = new MicrosoftTodoImportSession();
        session.Connect(oldClient);
        session.ReleaseConnection();
        Assert.Null(session.Reader);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => oldClient.GetMeAsync(TestContext.Current.CancellationToken));
        session.Connect(freshClient);
        var export = new MicrosoftTodoExport { User = new GraphUser { Id = "same-account" } };
        Assert.True(await MicrosoftTodoReader.CanResumeAsync(freshClient, export, TestContext.Current.CancellationToken));
        Assert.Single(handler.Requests);
        await session.EndAsync();
    }

    [Fact]
    public async Task QueuedProgressIsInvalidAfterWorkDismissalAndReset()
    {
        var session = new MicrosoftTodoImportSession();
        session.BeginWork();
        int reading = session.Generation;
        Assert.True(session.IsCurrent(reading));
        session.EndWork();
        Assert.False(session.IsCurrent(reading));
        int idle = session.Generation;
        await session.EndAsync();
        Assert.False(session.IsCurrent(idle));
        session.Reset();
        Assert.False(session.IsCurrent(idle));
        session.BeginWork();
        Assert.False(session.IsCurrent(reading));
        session.EndWork();
    }

    [Fact]
    public async Task ResetWaitsUntilTheDismissedOperationFinishes()
    {
        var session = new MicrosoftTodoImportSession();
        session.BeginWork();
        Task ended = session.EndAsync();
        Assert.Throws<InvalidOperationException>(session.Reset);
        Assert.Throws<InvalidOperationException>(() => session.BeginWork());
        session.EndWork();
        await ended;
        session.Reset();
        Assert.False(session.IsDismissed);
        session.BeginWork();
        session.EndWork();
    }

    [Fact]
    public async Task RepeatedDismissalSharesOneCompletion()
    {
        var session = new MicrosoftTodoImportSession();
        session.BeginWork();
        Task first = session.EndAsync();
        Assert.Same(first, session.EndAsync());
        session.EndWork();
        await first;
        Assert.Same(first, session.EndAsync());
    }

    [Fact]
    public async Task FixtureOwnedTransportIsDisposedWhenSessionEnds()
    {
        var handler = new PausedHandler();
        using var transport = new HttpClient(handler);
        using var client = new GraphTodoClient(transport, "synthetic-token", ownsHttpClient: true);
        var session = new MicrosoftTodoImportSession();
        session.Connect(client);
        await session.EndAsync();
        Assert.True(handler.IsDisposed);
    }

    private sealed class PausedHandler : HttpMessageHandler
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool IsDisposed { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return ScriptedHandler.Json(HttpStatusCode.OK, "{}");
        }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
