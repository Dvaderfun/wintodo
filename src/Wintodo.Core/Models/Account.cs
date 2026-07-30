namespace Wintodo.Core.Models;

public sealed class Account
{
    public long Id { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string ServerUrl { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string CredentialKey { get; set; } = string.Empty;

    public string? PrincipalUrl { get; set; }

    public string? HomeSetUrl { get; set; }

    public DateTimeOffset? LastSync { get; set; }
}
