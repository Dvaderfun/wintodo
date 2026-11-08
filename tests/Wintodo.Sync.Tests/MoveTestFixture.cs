using Microsoft.Data.Sqlite;
using Wintodo.Core.Ical;
using Wintodo.Core.Models;
using Wintodo.Core.Services;
using Wintodo.Data;

namespace Wintodo.Sync.Tests;

internal sealed class MoveTestFixture : IAsyncDisposable
{
    private readonly string _directory;
    private readonly bool _ownsServer;

    private MoveTestFixture(string directory, SyncedMoveTestServer server, bool ownsServer)
    {
        _directory = directory;
        _ownsServer = ownsServer;
        Server = server;
        Store = new SqliteTaskStore(DatabasePath, Clock);
    }

    internal SqliteTaskStore Store { get; private set; }
    internal SyncedMoveTestServer Server { get; }
    internal MoveClock Clock { get; } = new();
    internal MoveCredentials Credentials { get; } = new();
    internal string DatabasePath => Path.Combine(_directory, "wintodo.db");
    internal Account SourceAccount { get; private set; } = null!;
    internal Account DestinationAccount { get; private set; } = null!;
    internal TodoList Source { get; private set; } = null!;
    internal TodoList Destination { get; private set; } = null!;
    internal string SourceHref => Source.RemoteUrl + "original.ics";

    internal static async Task<MoveTestFixture> CreateAsync(bool crossAccount = false,
        SyncedMoveTestServer? server = null, bool reverseCreation = false)
    {
        string directory = Path.Combine(Path.GetTempPath(), "Wintodo.Sync.Move.Tests", Guid.NewGuid().ToString("N"));
        var fixture = new MoveTestFixture(directory, server ?? new SyncedMoveTestServer(), server is null);
        var ct = TestContext.Current.CancellationToken;
        await fixture.Store.InitializeAsync(ct);
        if (crossAccount && reverseCreation)
            fixture.DestinationAccount = await fixture.CreateAccountAsync("other.example", "destination-secret");
        fixture.SourceAccount = await fixture.CreateAccountAsync("dav.example", "source-secret");
        fixture.DestinationAccount = !crossAccount ? fixture.SourceAccount
            : fixture.DestinationAccount ?? await fixture.CreateAccountAsync("other.example", "destination-secret");
        if (reverseCreation) fixture.Destination = await fixture.CreateMappedListAsync(fixture.DestinationAccount, "b", "Destination");
        fixture.Source = await fixture.CreateMappedListAsync(fixture.SourceAccount, "a", "Source");
        fixture.Destination ??= await fixture.CreateMappedListAsync(fixture.DestinationAccount, "b", "Destination");
        return fixture;
    }

    internal async Task<Account> CreateAccountAsync(string host, string secret)
    {
        Server.AddAccount(host, secret);
        Credentials.Secrets[host] = secret;
        return await Store.SaveAccountAsync(new Account
        {
            DisplayName = host, ServerUrl = "https://" + host + "/", Username = "synthetic", CredentialKey = host,
            PrincipalUrl = "https://" + host + "/principals/synthetic/", HomeSetUrl = "https://" + host + "/cal/",
        }, TestContext.Current.CancellationToken);
    }

    internal async Task<TodoList> CreateMappedListAsync(Account account, string segment, string name)
    {
        var ct = TestContext.Current.CancellationToken;
        TodoList list = await Store.CreateListAsync(name, cancellationToken: ct);
        string url = account.ServerUrl + "cal/" + segment + "/";
        Server.AddCollection(url, name);
        await Store.SetListSyncMetadataAsync(list.Id, account.Id, url, null, null, true, ct);
        list.AccountId = account.Id;
        list.RemoteUrl = url;
        list.IsSyncEnabled = true;
        return list;
    }

    internal async Task<TodoTask> SeedAsync(string uid = "original-task", string? parentUid = null, string? fileName = null)
    {
        var task = new TodoTask
        {
            Uid = uid, Summary = "Original task", ParentUid = parentUid, Created = Clock.UtcNow,
            LastModified = Clock.UtcNow, Description = "Retained description", Sequence = 4, Priority = 1,
            IcalUnknown = IcalExtensionProperties.Merge(null,
                [new IcalExtensionProperty("X-SYNTHETIC-PRESERVE", "retained")], ["X-SYNTHETIC-PRESERVE"]),
        };
        task.Categories.Add("retained-category");
        string href = Source.RemoteUrl + (fileName ?? "original.ics");
        string etag = Server.Seed(href, Serialize(Source, task));
        await Store.ApplyRemoteChangesAsync(Source.Id,
            [new RemoteTaskUpdate { Task = task, Href = href, ETag = etag }], [], TestContext.Current.CancellationToken);
        return (await Store.GetTaskForSyncAsync(uid, TestContext.Current.CancellationToken))!;
    }

    internal byte[] Serialize(TodoList list, TodoTask task) =>
        new IcalWriter(TimeZoneInfo.Utc).WriteCalendar(list, [task], Clock.UtcNow);

    internal async Task ReopenAsync()
    {
        await Store.DisposeAsync();
        Store = new SqliteTaskStore(DatabasePath, Clock);
        await Store.InitializeAsync(TestContext.Current.CancellationToken);
    }

    internal async Task<SyncRunResult> SyncAsync()
    {
        using var http = new HttpClient(Server, disposeHandler: false);
        using var sync = new CalDavSyncService(Store, Credentials, Clock, http);
        return await sync.SyncAllAsync(TestContext.Current.CancellationToken);
    }

    internal async Task ResolveAsync(SyncConflict conflict, SyncConflictChoice choice)
    {
        using var http = new HttpClient(Server, disposeHandler: false);
        using var sync = new CalDavSyncService(Store, Credentials, Clock, http);
        await sync.ResolveConflictAsync(conflict, choice, TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await Store.DisposeAsync();
        SqliteConnection.ClearAllPools();
        if (_ownsServer) Server.Dispose();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}

internal sealed class MoveClock : IClock
{
    public DateTimeOffset UtcNow { get; } = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);
    public TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

internal sealed class MoveCredentials : ICredentialStore
{
    internal Dictionary<string, string> Secrets { get; } = new(StringComparer.Ordinal);
    public Task SaveAsync(string resource, string username, string secret, CancellationToken cancellationToken = default)
    {
        Secrets[resource] = secret;
        return Task.CompletedTask;
    }
    public Task<string?> GetSecretAsync(string resource, string username, CancellationToken cancellationToken = default) =>
        Task.FromResult(Secrets.GetValueOrDefault(resource));
    public Task DeleteAsync(string resource, string username, CancellationToken cancellationToken = default)
    {
        Secrets.Remove(resource);
        return Task.CompletedTask;
    }
}
