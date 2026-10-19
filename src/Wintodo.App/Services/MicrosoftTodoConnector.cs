using Wintodo.Core.Services;
using Wintodo.Import.MicrosoftTodo;

namespace Wintodo.Services;

/// <summary>Opens a read-only Graph session for one import.</summary>
internal interface IMicrosoftTodoConnector
{
    /// <summary>False when the build has no Microsoft app registration.</summary>
    bool IsConfigured { get; }

    Task<GraphTodoClient> ConnectAsync(CancellationToken cancellationToken);
}

internal static class MicrosoftTodoRegistration
{
    /// <summary>
    /// Application (client) id of the Entra public-client registration
    /// ("personal and work/school accounts", redirect <c>http://localhost</c>,
    /// delegated <c>Tasks.ReadWrite</c> + <c>User.Read</c>). A public client id is
    /// not a secret. Registered 2026-10-19 as "Wintodo" in the owner's Entra
    /// directory, ADR-016.
    /// </summary>
    internal const string ClientId = "038f1bee-9c89-4f6c-9061-15210945a03e";
}

internal enum MicrosoftSignInFailure
{
    Cancelled,
    Failed,
}

internal sealed class MicrosoftSignInException(MicrosoftSignInFailure failure, string? detail)
    : Exception(detail ?? failure.ToString())
{
    internal MicrosoftSignInFailure Failure { get; } = failure;
}

/// <summary>Real connector: system-browser sign-in, token kept in memory only.</summary>
internal sealed class MicrosoftGraphConnector(HttpClient httpClient, string signInCompletePageText, IClock clock)
    : IMicrosoftTodoConnector
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(MicrosoftTodoRegistration.ClientId);

    public async Task<GraphTodoClient> ConnectAsync(CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("No Microsoft client id is configured.");
        }

        string token = await MicrosoftSignInService.SignInAsync(
            MicrosoftTodoRegistration.ClientId,
            signInCompletePageText,
            cancellationToken);
        return new GraphTodoClient(httpClient, token, clock: clock);
    }
}

#if DEBUG
/// <summary>
/// UI-gate connector: serves a recorded Graph folder, no sign-in. With
/// <paramref name="expireFirstSignIn"/>, the first session's token is rejected
/// when attachments download, like a sign-in that expired on the review step.
/// </summary>
internal sealed class FixtureMicrosoftTodoConnector(string fixtureDirectory, IClock clock, bool expireFirstSignIn)
    : IMicrosoftTodoConnector
{
    private bool _expireNext = expireFirstSignIn;

    public bool IsConfigured => true;

    public async Task<GraphTodoClient> ConnectAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        var handler = new FixtureGraphHandler(fixtureDirectory, rejectAttachmentContent: _expireNext);
        _expireNext = false;
        return new GraphTodoClient(new HttpClient(handler), "fixture", clock: clock);
    }
}
#endif

/// <summary>What the import surfaces need from the launch environment.</summary>
internal sealed class ImportEnvironment(IMicrosoftTodoConnector connector, bool onboardingAllowed, string attachmentRoot)
{
    internal IMicrosoftTodoConnector Connector { get; } = connector;

    /// <summary>Scratch databases used by UI gates opt in with <c>--show-onboarding</c>.</summary>
    internal bool OnboardingAllowed { get; } = onboardingAllowed;

    internal string AttachmentRoot { get; } = attachmentRoot;
}
