using System.Net;
using System.Text;
using Microsoft.Data.Sqlite;
using Wintodo.Core.Services;
using Wintodo.Data;
using Wintodo.Import.MicrosoftTodo;

namespace Wintodo.Import.Tests;

internal static class TestSupport
{
    internal static string FixturePath(string name)
    {
        return Path.Combine(AppContext.BaseDirectory, "fixtures", "mstodo", name);
    }

    internal static GraphTodoClient FixtureClient(string name = "basic")
    {
        return new GraphTodoClient(new HttpClient(new FixtureGraphHandler(FixturePath(name))), "fixture-token");
    }

    internal static async Task<MicrosoftTodoExport> ReadFixtureAsync(string name = "basic")
    {
        return await new MicrosoftTodoReader(FixtureClient(name)).ReadAsync(cancellationToken: TestContext.Current.CancellationToken);
    }

    internal static MicrosoftTodoMapOptions Options(DateTimeOffset now, bool includeCompleted = true)
    {
        return new MicrosoftTodoMapOptions
        {
            IncludeCompleted = includeCompleted,
            LocalTimeZone = TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time"),
            Now = now,
            UntitledTaskTitle = "Untitled task",
        };
    }
}

internal sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    public TimeZoneInfo LocalTimeZone { get; set; } = TimeZoneInfo.Utc;
}

internal sealed class TempStore : IAsyncDisposable
{
    private TempStore(string directory, FakeClock clock, SqliteTaskStore store)
    {
        Directory = directory;
        Clock = clock;
        Store = store;
    }

    internal string Directory { get; }

    internal FakeClock Clock { get; }

    internal SqliteTaskStore Store { get; }

    internal static async Task<TempStore> CreateAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), "Wintodo.Import.Tests", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(directory);
        var clock = new FakeClock();
        var store = new SqliteTaskStore(Path.Combine(directory, "wintodo.db"), clock);
        await store.InitializeAsync();
        await store.EnsureDefaultListAsync("Tasks");
        return new TempStore(directory, clock, store);
    }

    public async ValueTask DisposeAsync()
    {
        await Store.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

/// <summary>Returns queued responses and records every request.</summary>
internal sealed class ScriptedHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses) : HttpMessageHandler
{
    private int _index;

    internal List<Uri> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        Func<HttpRequestMessage, HttpResponseMessage> next = responses[Math.Min(_index++, responses.Length - 1)];
        return Task.FromResult(next(request));
    }

    internal static HttpResponseMessage Json(HttpStatusCode status, string json)
    {
        return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
