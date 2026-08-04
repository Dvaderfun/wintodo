using System.Net;

namespace Wintodo.Sync;

public sealed class CalDavDiscovery
{
    public Uri PrincipalUrl { get; set; } = null!;

    public Uri HomeSetUrl { get; set; } = null!;
}

public sealed class CalDavCollection
{
    public Uri Url { get; set; } = null!;

    public string DisplayName { get; set; } = string.Empty;

    public string? CTag { get; set; }

    public string? SyncToken { get; set; }
}

public sealed class CalDavResource
{
    public Uri Url { get; set; } = null!;

    public string Href { get; set; } = string.Empty;

    public string? ETag { get; set; }

    public bool IsDeleted { get; set; }
}

public sealed class CalDavDelta
{
    public bool IsFullSnapshot { get; set; }

    public string? SyncToken { get; set; }

    public string? CTag { get; set; }

    public List<CalDavResource> Resources { get; } = [];
}

public sealed class CalDavContent
{
    public byte[] Content { get; set; } = [];

    public string? ETag { get; set; }
}

public sealed class CalDavWriteResult
{
    public HttpStatusCode StatusCode { get; set; }

    public string? ETag { get; set; }

    public bool IsSuccess { get; set; }

    public bool IsConflict { get; set; }
}

public sealed class SyncStatusChangedEventArgs : EventArgs
{
    public SyncStatusChangedEventArgs(
        Wintodo.Core.Models.SyncRunStatus status,
        string? message)
    {
        Status = status;
        Message = message;
    }

    public Wintodo.Core.Models.SyncRunStatus Status { get; }

    public string? Message { get; }
}
