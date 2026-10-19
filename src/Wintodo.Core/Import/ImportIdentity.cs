using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Wintodo.Core.Import;

public enum ImportEntityType
{
    Task = 0,
    List = 1,
    Step = 2,
    Attachment = 3,
}

/// <summary>Stable identities for imported items.</summary>
public static class ImportIdentity
{
    // Random namespace for Wintodo imports (RFC 9562 name-based UUIDs).
    private static readonly Guid Namespace = new("5b0f6a3c-1f3e-4a8f-9c55-0f1c3e7d2a91");

    /// <summary>
    /// UUIDv5 of provider, account, entity type and external id. Importing the
    /// same item from the same account always yields the same UID.
    /// </summary>
    public static string DeterministicUid(
        string provider,
        string accountKey,
        ImportEntityType entityType,
        string externalId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);
        string name = string.Join(
            '\n',
            provider,
            accountKey,
            ((int)entityType).ToString(CultureInfo.InvariantCulture),
            externalId);
        return NameBasedUuid(Namespace, name).ToString("D");
    }

    /// <summary>
    /// Creation instant (to the second) plus the normalised title. Survives the
    /// external id change that Microsoft Graph applies when a task moves list.
    /// </summary>
    public static string Fingerprint(DateTimeOffset created, string title)
    {
        string instant = created.ToUniversalTime().ToString(
            "yyyy-MM-dd'T'HH:mm:ss'Z'",
            CultureInfo.InvariantCulture);
        return instant + "|" + NormalizeTitle(title);
    }

    public static string NormalizeTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(title.Length);
        bool pendingSpace = false;
        foreach (char character in title.Trim())
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = true;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    internal static Guid NameBasedUuid(Guid namespaceId, string name)
    {
        Span<byte> namespaceBytes = stackalloc byte[16];
        namespaceId.TryWriteBytes(namespaceBytes, bigEndian: true, out _);
        byte[] nameBytes = Encoding.UTF8.GetBytes(name);
        byte[] input = new byte[16 + nameBytes.Length];
        namespaceBytes.CopyTo(input);
        nameBytes.CopyTo(input, 16);

        // RFC 9562 mandates SHA-1 for version 5; this is an identifier, not a security boundary.
        byte[] hash = SHA1.HashData(input);
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash.AsSpan(0, 16), bigEndian: true);
    }
}
