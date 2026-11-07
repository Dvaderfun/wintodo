using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;
using Wintodo.Core.Models;
using Wintodo.Core.Services;
using Wintodo.Data;
using Wintodo.Sync;

namespace Wintodo.Sync.Tests;

public sealed partial class CalDavSyncServiceTests
{
    [Fact]
    public async Task RestoredList_RecreatesCollectionBeforePushingItsTasks()
    {
        await using SyncFixture f = await SyncFixture.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        (Account account, TodoList list) = await f.CreateMappedListAsync();
        TodoTask task = await f.Store.CreateTaskAsync(new TodoTask { ListId = list.Id, Summary = "Restore remotely" }, ct);
        await f.Store.DeleteListAsync(list.Id, ct);
        SyncChange deletion = Assert.Single(await f.Store.GetPendingSyncChangesAsync(account.Id, ct),
            change => change.EntityType == SyncEntityType.List);
        await f.Store.MarkListChangeSyncedAsync(deletion.Id, list.Uid, null, ct);
        await f.Store.RestoreListAsync(list.Id, ct);
        using var handler = new RestoreHandler();
        using var http = new HttpClient(handler);
        using var sync = new CalDavSyncService(f.Store, new FakeCredentialStore(), f.Clock, http);
        SyncRunResult result = await sync.SyncAccountAsync(account, ct);
        Assert.Equal(SyncRunStatus.Success, result.Status);
        Assert.Equal(new[] { "MKCALENDAR", "PUT" }, handler.Writes);
        Assert.Contains("Restore remotely", handler.Payload);
        Assert.Empty(await f.Store.GetPendingSyncChangesAsync(account.Id, ct));
        TodoTask restored = (await f.Store.GetTaskAsync(task.Uid, ct))!;
        Assert.Contains(list.Uid, restored.RemoteHref!);
        Assert.False(restored.IsDirty);
    }

    private sealed class RestoreHandler : HttpMessageHandler
    {
        internal List<string> Writes { get; } = [];
        internal string Payload { get; private set; } = string.Empty;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string method = request.Method.Method;
            if (method == "PROPFIND") return new HttpResponseMessage(HttpStatusCode.MultiStatus)
            { Content = new StringContent("<d:multistatus xmlns:d=\"DAV:\"/>") };
            Writes.Add(method);
            if (method == "PUT") Payload = await request.Content!.ReadAsStringAsync(cancellationToken);
            var response = new HttpResponseMessage(HttpStatusCode.Created);
            if (method == "PUT") response.Headers.ETag = new EntityTagHeaderValue("\"restored\"");
            return response;
        }
    }

    [Fact]
    public async Task TwoClients_ConvergeAndDetectConcurrentEdits()
    {
        using var server = new InMemoryCalDavHandler();
        await using SyncFixture first = await SyncFixture.CreateAsync();
        await using SyncFixture second = await SyncFixture.CreateAsync();
        (Account firstAccount, TodoList firstList) = await first.CreateMappedListAsync();
        (Account secondAccount, TodoList secondList) = await second.CreateMappedListAsync();
        using var firstHttp = new HttpClient(server, disposeHandler: false);
        using var secondHttp = new HttpClient(server, disposeHandler: false);
        using var firstSync = new CalDavSyncService(
            first.Store,
            new FakeCredentialStore(),
            first.Clock,
            firstHttp);
        using var secondSync = new CalDavSyncService(
            second.Store,
            new FakeCredentialStore(),
            second.Clock,
            secondHttp);

        TodoTask created = await first.Store.CreateTaskAsync(new TodoTask
        {
            ListId = firstList.Id,
            Summary = "Shared task",
        }, TestContext.Current.CancellationToken);
        Assert.Equal(SyncRunStatus.Success, (await firstSync.SyncAccountAsync(firstAccount, TestContext.Current.CancellationToken)).Status);
        Assert.Equal(SyncRunStatus.Success, (await secondSync.SyncAccountAsync(secondAccount, TestContext.Current.CancellationToken)).Status);
        TodoTask secondCopy = (await second.Store.GetTaskForSyncAsync(created.Uid, TestContext.Current.CancellationToken))!;
        Assert.Equal("Shared task", secondCopy.Summary);

        TodoTask firstCopy = (await first.Store.GetTaskForSyncAsync(created.Uid, TestContext.Current.CancellationToken))!;
        firstCopy.Summary = "Completed remotely";
        firstCopy.Status = Wintodo.Core.Models.TaskStatus.Completed;
        firstCopy.PercentComplete = 100;
        await first.Store.UpdateTaskAsync(firstCopy, TestContext.Current.CancellationToken);
        Assert.Equal(SyncRunStatus.Success, (await firstSync.SyncAccountAsync(firstAccount, TestContext.Current.CancellationToken)).Status);

        secondCopy.Summary = "Concurrent local edit";
        await second.Store.UpdateTaskAsync(secondCopy, TestContext.Current.CancellationToken);
        SyncRunResult conflictResult = await secondSync.SyncAccountAsync(secondAccount, TestContext.Current.CancellationToken);
        SyncConflict conflict = Assert.Single(conflictResult.Conflicts);
        Assert.Equal(SyncRunStatus.AttentionRequired, conflictResult.Status);
        Assert.Equal("Concurrent local edit", conflict.LocalTask.Summary);
        Assert.Equal("Completed remotely", conflict.RemoteTask!.Summary);

        await secondSync.ResolveConflictAsync(conflict, SyncConflictChoice.KeepTheirs, TestContext.Current.CancellationToken);
        TodoTask resolved = (await second.Store.GetTaskForSyncAsync(created.Uid, TestContext.Current.CancellationToken))!;
        Assert.Equal("Completed remotely", resolved.Summary);
        Assert.Equal(Wintodo.Core.Models.TaskStatus.Completed, resolved.Status);

        await second.Store.DeleteTaskAsync(created.Uid, TestContext.Current.CancellationToken);
        Assert.Equal(SyncRunStatus.Success, (await secondSync.SyncAccountAsync(secondAccount, TestContext.Current.CancellationToken)).Status);
        Assert.Equal(SyncRunStatus.Success, (await firstSync.SyncAccountAsync(firstAccount, TestContext.Current.CancellationToken)).Status);
        Assert.True((await first.Store.GetTaskForSyncAsync(created.Uid, TestContext.Current.CancellationToken))!.IsDeleted);
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
                ServerUrl = "https://dav.example/",
                Username = "alice",
                CredentialKey = "credential",
                PrincipalUrl = "https://dav.example/principal/",
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
            return Task.FromResult<string?>("secret");
        }

        public Task DeleteAsync(
            string resource,
            string username,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryCalDavHandler : HttpMessageHandler
    {
        private const string CollectionPath = "/cal/tasks/";
        private readonly Dictionary<string, StoredResource> _resources =
            new(StringComparer.Ordinal);
        private readonly List<JournalEntry> _journal = [];
        private int _version;

        internal Func<string, CancellationToken, Task>? BeforeGetAsync { get; set; }

        internal List<string> Writes { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath;
            string method = request.Method.Method;
            if (method == "PROPFIND" && path == "/cal/")
            {
                return XmlResponse(MultiStatus(CollectionResponse()));
            }

            if (method == "PROPFIND" && path == CollectionPath)
            {
                string? depth = GetHeader(request, "Depth");
                return depth == "0"
                    ? XmlResponse(MultiStatus(CollectionResponse()))
                    : XmlResponse(MultiStatus(
                        CollectionResponse()
                        + string.Concat(_resources.Select(ResourceResponse))
                        + $"<d:sync-token>token-{_version}</d:sync-token>"));
            }

            if (method == "REPORT" && path == CollectionPath)
            {
                string body = await request.Content!.ReadAsStringAsync(cancellationToken);
                XNamespace dav = "DAV:";
                int since = int.Parse(
                    XDocument.Parse(body).Descendants(dav + "sync-token").Single().Value[6..],
                    System.Globalization.CultureInfo.InvariantCulture);
                IEnumerable<JournalEntry> changes = _journal
                    .Where(entry => entry.Version > since)
                    .GroupBy(entry => entry.Path, StringComparer.Ordinal)
                    .Select(group => group.MaxBy(entry => entry.Version)!);
                string responses = string.Concat(changes.Select(ChangeResponse));
                return XmlResponse(MultiStatus(
                    responses + $"<d:sync-token>token-{_version}</d:sync-token>"));
            }

            if (method == "GET")
            {
                if (BeforeGetAsync is not null)
                {
                    await BeforeGetAsync(path, cancellationToken);
                }

                if (!_resources.TryGetValue(path, out StoredResource? resource))
                {
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                }

                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(resource.Content),
                };
                response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("text/calendar");
                response.Headers.ETag = new EntityTagHeaderValue(resource.ETag);
                return response;
            }

            if (method == "PUT")
            {
                Writes.Add(path);
                _resources.TryGetValue(path, out StoredResource? existing);
                if (GetHeader(request, "If-None-Match") == "*" && existing is not null
                    || GetHeader(request, "If-Match") is string expected
                        && !string.Equals(existing?.ETag, expected, StringComparison.Ordinal))
                {
                    return new HttpResponseMessage(HttpStatusCode.PreconditionFailed);
                }

                byte[] content = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
                int version = ++_version;
                var stored = new StoredResource(content, $"\"v{version}\"");
                _resources[path] = stored;
                _journal.Add(new JournalEntry(version, path, IsDeleted: false));
                var response = new HttpResponseMessage(HttpStatusCode.NoContent);
                response.Headers.ETag = new EntityTagHeaderValue(stored.ETag);
                return response;
            }

            if (method == "DELETE")
            {
                Writes.Add(path);
                if (!_resources.TryGetValue(path, out StoredResource? existing))
                {
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                }

                if (GetHeader(request, "If-Match") is string expected
                    && !string.Equals(existing.ETag, expected, StringComparison.Ordinal))
                {
                    return new HttpResponseMessage(HttpStatusCode.PreconditionFailed);
                }

                _resources.Remove(path);
                int version = ++_version;
                _journal.Add(new JournalEntry(version, path, IsDeleted: true));
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
        }

        private string CollectionResponse()
        {
            return $"""
                <d:response><d:href>{CollectionPath}</d:href><d:propstat><d:prop>
                  <d:resourcetype><d:collection/><c:calendar/></d:resourcetype>
                  <d:displayname>Tasks</d:displayname><d:sync-token>token-{_version}</d:sync-token>
                  <cs:getctag>ctag-{_version}</cs:getctag>
                  <c:supported-calendar-component-set><c:comp name="VTODO"/></c:supported-calendar-component-set>
                </d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>
                """;
        }

        private static string ResourceResponse(KeyValuePair<string, StoredResource> pair)
        {
            return $"""
                <d:response><d:href>{pair.Key}</d:href><d:propstat><d:prop>
                  <d:getetag>{pair.Value.ETag}</d:getetag><d:getcontenttype>text/calendar</d:getcontenttype>
                </d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>
                """;
        }

        private string ChangeResponse(JournalEntry entry)
        {
            if (entry.IsDeleted)
            {
                return $"<d:response><d:href>{entry.Path}</d:href><d:status>HTTP/1.1 404 Not Found</d:status></d:response>";
            }

            return ResourceResponse(
                new KeyValuePair<string, StoredResource>(entry.Path, _resources[entry.Path]));
        }

        private static string MultiStatus(string inner)
        {
            return $"""
                <d:multistatus xmlns:d="DAV:" xmlns:c="urn:ietf:params:xml:ns:caldav" xmlns:cs="http://calendarserver.org/ns/">
                {inner}
                </d:multistatus>
                """;
        }

        private static HttpResponseMessage XmlResponse(string body)
        {
            return new HttpResponseMessage(HttpStatusCode.MultiStatus)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/xml"),
            };
        }

        private static string? GetHeader(HttpRequestMessage request, string name)
        {
            return request.Headers.TryGetValues(name, out IEnumerable<string>? values)
                ? values.Single()
                : null;
        }

        private sealed record StoredResource(byte[] Content, string ETag);

        private sealed record JournalEntry(int Version, string Path, bool IsDeleted);
    }
}
