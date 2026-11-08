using System.Net;
using Wintodo.Core.Models;
using Wintodo.Sync;

namespace Wintodo.Sync.Tests;

public sealed partial class CalDavSyncServiceTests
{
    [Theory]
    [InlineData(false, SyncRunStatus.Offline, "TYPE=Http;STATUS=503")]
    [InlineData(true, SyncRunStatus.Error, "TYPE=Other;STATUS=403")]
    public async Task FailedSync_ReportsSafeCodesWithoutPrivateServerContent(
        bool protocolFailure, SyncRunStatus expectedStatus, string expectedDiagnostic)
    {
        await using SyncFixture fixture = await SyncFixture.CreateAsync();
        var (account, _) = await fixture.CreateMappedListAsync();
        using var handler = new PrivateFailureHandler(protocolFailure);
        using var http = new HttpClient(handler);
        using var sync = new CalDavSyncService(fixture.Store,
            new FakeCredentialStore(), fixture.Clock, http);
        var messages = new List<string?>();
        sync.StatusChanged += (_, args) => messages.Add(args.Message);

        SyncRunResult result = await sync.SyncAccountAsync(account,
            TestContext.Current.CancellationToken);

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedDiagnostic, result.ErrorMessage);
        Assert.Equal(expectedDiagnostic, Assert.Single(messages, message => message is not null));
    }

    private sealed class PrivateFailureHandler(bool protocolFailure) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            const string markers = "Bearer SYNTHETIC-TOKEN password=SYNTHETIC-PASSWORD "
                + "person@example.invalid SYNTHETIC-TASK SYNTHETIC-ACCOUNT "
                + "https://example.invalid/SYNTHETIC-ENDPOINT C:\\Users\\SYNTHETIC-OWNER\\private.db";
            Exception failure = protocolFailure
                ? new CalDavProtocolException(markers, HttpStatusCode.Forbidden, markers)
                : new HttpRequestException(markers, new Exception(markers), HttpStatusCode.ServiceUnavailable);
            return Task.FromException<HttpResponseMessage>(failure);
        }
    }
}
