namespace Wintodo.Core.Services;

public interface ICredentialStore
{
    Task SaveAsync(
        string resource,
        string username,
        string secret,
        CancellationToken cancellationToken = default);

    Task<string?> GetSecretAsync(
        string resource,
        string username,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string resource,
        string username,
        CancellationToken cancellationToken = default);
}
