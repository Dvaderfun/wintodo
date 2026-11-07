using System.Net;
using Microsoft.Data.Sqlite;
using Wintodo.Core.Models;
using Wintodo.Core.Ical;
using Wintodo.Core.Queries;
using Wintodo.Core.Services;
using Wintodo.Data;
using Wintodo.Sync;

namespace Wintodo.Sync.Tests;

public sealed partial class SyncPersistenceTests
{
    [Fact]
    public async Task PulledUnknownProperty_SurvivesLocalEditAndNextPutPayload()
    {
        await using SyncFixture fixture = await SyncFixture.CreateAsync();
        (_, TodoList list) = await fixture.CreateMappedListAsync();
        const string ics = """
            BEGIN:VCALENDAR
            VERSION:2.0
            BEGIN:VTODO
            UID:unknown-property-task
            SUMMARY:Before edit
            X-NEXTCLOUD-CUSTOM;VALUE=TEXT:keep-me
            END:VTODO
            END:VCALENDAR

            """;
        TodoTask remote = new IcalReader(TimeZoneInfo.Utc).Read(ics).Tasks.Single();
        await fixture.Store.ApplyRemoteChangesAsync(
            list.Id,
            [new RemoteTaskUpdate
            {
                Task = remote,
                Href = "https://dav.example/cal/tasks/unknown.ics",
                ETag = "\"v1\"",
            }],
            [],
            TestContext.Current.CancellationToken);
        TodoTask local = (await fixture.Store.GetTaskForSyncAsync(remote.Uid, TestContext.Current.CancellationToken))!;
        local.Summary = "After edit";
        await fixture.Store.UpdateTaskAsync(local, TestContext.Current.CancellationToken);
        byte[] payload = new IcalWriter(TimeZoneInfo.Utc).WriteCalendar(
            list,
            [(await fixture.Store.GetTaskForSyncAsync(remote.Uid, TestContext.Current.CancellationToken))!],
            fixture.Clock.UtcNow);

        string written = System.Text.Encoding.UTF8.GetString(payload);
        Assert.Contains("X-NEXTCLOUD-CUSTOM;VALUE=TEXT:keep-me", written, StringComparison.Ordinal);
        Assert.Contains("SUMMARY:After edit", written, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OfflineSync_LeavesLocalChangesQueued()
    {
        await using SyncFixture fixture = await SyncFixture.CreateAsync();
        (Account account, TodoList list) = await fixture.CreateMappedListAsync();
        TodoTask task = await fixture.Store.CreateTaskAsync(new TodoTask
        {
            ListId = list.Id,
            Summary = "Queue while offline",
        }, TestContext.Current.CancellationToken);
        using var httpClient = new HttpClient(new ThrowingHandler());
        using var service = new CalDavSyncService(
            fixture.Store,
            new FakeCredentialStore("secret"),
            fixture.Clock,
            httpClient);

        SyncRunResult result = await service.SyncAccountAsync(account, TestContext.Current.CancellationToken);
        IReadOnlyList<SyncChange> pending =
            await fixture.Store.GetPendingSyncChangesAsync(account.Id, TestContext.Current.CancellationToken);

        Assert.Equal(SyncRunStatus.Offline, result.Status);
        Assert.Contains(pending, change => change.EntityUid == task.Uid);
        Assert.True((await fixture.Store.GetTaskForSyncAsync(task.Uid, TestContext.Current.CancellationToken))!.IsDirty);
    }

    [Fact]
    public async Task AcknowledgingSnapshot_DoesNotClearANewerLocalEdit()
    {
        await using SyncFixture fixture = await SyncFixture.CreateAsync();
        (Account account, TodoList list) = await fixture.CreateMappedListAsync();
        TodoTask task = await fixture.Store.CreateTaskAsync(new TodoTask
        {
            ListId = list.Id,
            Summary = "First value",
        }, TestContext.Current.CancellationToken);
        SyncChange snapshot = Assert.Single(
            await fixture.Store.GetPendingSyncChangesAsync(account.Id, TestContext.Current.CancellationToken),
            change => change.EntityUid == task.Uid);
        task.Summary = "Newer value";
        await fixture.Store.UpdateTaskAsync(task, TestContext.Current.CancellationToken);

        await fixture.Store.MarkTaskChangeSyncedAsync(
            snapshot.Id,
            task.Uid,
            "https://dav.example/cal/tasks/task.ics",
            "\"v1\"",
            TestContext.Current.CancellationToken);

        TodoTask stored = (await fixture.Store.GetTaskForSyncAsync(task.Uid, TestContext.Current.CancellationToken))!;
        IReadOnlyList<SyncChange> pending =
            await fixture.Store.GetPendingSyncChangesAsync(account.Id, TestContext.Current.CancellationToken);
        Assert.True(stored.IsDirty);
        Assert.Equal("Newer value", stored.Summary);
        Assert.Contains(pending, change => change.EntityUid == task.Uid);
    }

    [Fact]
    public async Task KeepBoth_PreservesRemoteOriginalAndQueuesLocalCopy()
    {
        await using SyncFixture fixture = await SyncFixture.CreateAsync();
        (Account account, TodoList list) = await fixture.CreateMappedListAsync();
        var remote = new TodoTask
        {
            Uid = Guid.NewGuid().ToString("D"),
            Summary = "Server value",
            Created = fixture.Clock.UtcNow,
            LastModified = fixture.Clock.UtcNow,
        };
        await fixture.Store.ApplyRemoteChangesAsync(
            list.Id,
            [new RemoteTaskUpdate
            {
                Task = remote,
                Href = "https://dav.example/cal/tasks/conflict.ics",
                ETag = "\"v1\"",
            }],
            [],
            TestContext.Current.CancellationToken);
        TodoTask local = (await fixture.Store.GetTaskForSyncAsync(remote.Uid, TestContext.Current.CancellationToken))!;
        local.Summary = "Local value";
        await fixture.Store.UpdateTaskAsync(local, TestContext.Current.CancellationToken);
        var conflict = new SyncConflict
        {
            AccountId = account.Id,
            ListId = list.Id,
            ListName = list.DisplayName,
            LocalTask = (await fixture.Store.GetTaskForSyncAsync(remote.Uid, TestContext.Current.CancellationToken))!,
            RemoteTask = new TodoTask
            {
                Uid = remote.Uid,
                Summary = "Server value 2",
                Created = fixture.Clock.UtcNow,
                LastModified = fixture.Clock.UtcNow,
            },
            RemoteHref = "https://dav.example/cal/tasks/conflict.ics",
            RemoteETag = "\"v2\"",
        };
        using var httpClient = new HttpClient(new ThrowingHandler());
        using var service = new CalDavSyncService(
            fixture.Store,
            new FakeCredentialStore("secret"),
            fixture.Clock,
            httpClient);

        await service.ResolveConflictAsync(conflict, SyncConflictChoice.KeepBoth, TestContext.Current.CancellationToken);

        IReadOnlyList<TodoTask> tasks = await fixture.Store.GetTasksAsync(new ListQuery
        {
            Kind = ListQueryKind.UserList,
            ListId = list.Id,
            IncludeCompleted = true,
        }, TestContext.Current.CancellationToken);
        Assert.Equal(2, tasks.Count);
        Assert.Contains(tasks, task => task.Uid == remote.Uid && task.Summary == "Server value 2");
        TodoTask copy = Assert.Single(tasks, task => task.Uid != remote.Uid);
        Assert.Equal("Local value", copy.Summary);
        Assert.True(copy.IsDirty);
    }

    [Fact]
    public async Task Discovery_DoesNotResurrectAListWithAPendingLocalDelete()
    {
        await using SyncFixture fixture = await SyncFixture.CreateAsync();
        (Account account, TodoList list) = await fixture.CreateMappedListAsync();
        await fixture.Store.DeleteListAsync(list.Id, TestContext.Current.CancellationToken);

        TodoList discovered = await fixture.Store.UpsertRemoteListAsync(
            account.Id,
            list.RemoteUrl!,
            list.DisplayName,
            "ctag-2",
            "token-2",
            TestContext.Current.CancellationToken);

        Assert.True(discovered.IsDeleted);
        SyncChange pending = Assert.Single(
            await fixture.Store.GetPendingSyncChangesAsync(account.Id, TestContext.Current.CancellationToken),
            change => change.EntityUid == list.Uid);
        Assert.Equal(ChangeOperation.Delete, pending.Operation);
        Assert.True(pending.List!.IsDeleted);
    }

    private sealed class SyncFixture : IAsyncDisposable
    {
        private readonly string _directory;

        private SyncFixture(string directory, SqliteTaskStore store, FixedClock clock)
        {
            _directory = directory;
            Store = store;
            Clock = clock;
        }

        internal SqliteTaskStore Store { get; }

        internal FixedClock Clock { get; }

        internal string DatabasePath => Path.Combine(_directory, "wintodo.db");

        internal static async Task<SyncFixture> CreateAsync()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "Wintodo.Sync.Tests",
                Guid.NewGuid().ToString("N"));
            var clock = new FixedClock();
            var store = new SqliteTaskStore(Path.Combine(directory, "wintodo.db"), clock);
            await store.InitializeAsync();
            return new SyncFixture(directory, store, clock);
        }

        internal async Task<(Account Account, TodoList List)> CreateMappedListAsync()
        {
            Account account = await Store.SaveAccountAsync(new Account
            {
                DisplayName = "Test",
                ServerUrl = "https://dav.example/dav/",
                Username = "alice",
                CredentialKey = "credential",
                PrincipalUrl = "https://dav.example/principals/alice/",
                HomeSetUrl = "https://dav.example/cal/",
            });
            TodoList list = await Store.CreateListAsync("Tasks");
            await Store.SetListSyncMetadataAsync(
                list.Id,
                account.Id,
                "https://dav.example/cal/tasks/",
                remoteCTag: null,
                syncToken: null,
                markListChangesSynced: true);
            list.AccountId = account.Id;
            list.RemoteUrl = "https://dav.example/cal/tasks/";
            list.IsSyncEnabled = true;
            return (account, list);
        }

        public async ValueTask DisposeAsync()
        {
            await Store.DisposeAsync();
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } =
            new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

        public TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class FakeCredentialStore : ICredentialStore
    {
        private readonly string _secret;

        internal FakeCredentialStore(string secret)
        {
            _secret = secret;
        }

        public Task SaveAsync(
            string resource,
            string username,
            string secret,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<string?> GetSecretAsync(
            string resource,
            string username,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>(_secret);
        }

        public Task DeleteAsync(
            string resource,
            string username,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            throw new HttpRequestException("offline", null, HttpStatusCode.ServiceUnavailable);
        }
    }
}
