using Windows.Security.Credentials;
using Wintodo.Core.Services;

namespace Wintodo.Services;

public sealed class PasswordVaultCredentialStore : ICredentialStore
{
    private readonly PasswordVault _vault = new();

    public Task SaveAsync(
        string resource,
        string username,
        string secret,
        CancellationToken cancellationToken = default)
    {
        resource = VaultResource(resource, username);
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        cancellationToken.ThrowIfCancellationRequested();
        RemoveMatching(resource, username);
        _vault.Add(new PasswordCredential(resource, username, secret));
        return Task.CompletedTask;
    }

    public Task<string?> GetSecretAsync(
        string resource,
        string username,
        CancellationToken cancellationToken = default)
    {
        resource = VaultResource(resource, username);
        cancellationToken.ThrowIfCancellationRequested();
        PasswordCredential? credential = _vault.RetrieveAll().FirstOrDefault(value =>
            string.Equals(value.Resource, resource, StringComparison.Ordinal)
            && string.Equals(value.UserName, username, StringComparison.Ordinal));
        if (credential is null)
        {
            return Task.FromResult<string?>(null);
        }

        credential.RetrievePassword();
        return Task.FromResult<string?>(credential.Password);
    }

    public Task DeleteAsync(
        string resource,
        string username,
        CancellationToken cancellationToken = default)
    {
        resource = VaultResource(resource, username);
        cancellationToken.ThrowIfCancellationRequested();
        RemoveMatching(resource, username);
        return Task.CompletedTask;
    }

    private void RemoveMatching(string resource, string username)
    {
        foreach (PasswordCredential credential in _vault.RetrieveAll().Where(value =>
                     string.Equals(value.Resource, resource, StringComparison.Ordinal)
                     && string.Equals(value.UserName, username, StringComparison.Ordinal)))
        {
            _vault.Remove(credential);
        }
    }

    private static string VaultResource(string resource, string username)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
#if WINTODO_PRIVATE_PREVIEW
        return "Wintodo.Hybrid.Dev." + resource;
#else
        return resource;
#endif
    }
}
