namespace Wintodo.Core.Models;

public enum SyncEntityType
{
    Task = 0,
    List = 1,
}

public enum SyncRunStatus
{
    Idle = 0,
    Syncing,
    Success,
    Offline,
    AttentionRequired,
    Error,
}

public enum SyncConflictChoice
{
    KeepMine = 0,
    KeepTheirs,
    KeepBoth,
}

public sealed class SyncChange
{
    public long Id { get; set; }

    public SyncEntityType EntityType { get; set; }

    public string EntityUid { get; set; } = string.Empty;

    public ChangeOperation Operation { get; set; }

    public DateTimeOffset Changed { get; set; }

    public TodoList? List { get; set; }

    public TodoTask? Task { get; set; }
}

public sealed class RemoteTaskState
{
    public string Uid { get; set; } = string.Empty;

    public string RemoteHref { get; set; } = string.Empty;

    public string? RemoteETag { get; set; }

    public bool IsDirty { get; set; }

    public bool IsDeleted { get; set; }

    /// <summary>
    /// Task SEQUENCE observed before pull application. Local task and owned-
    /// collection writes increment it even when the clock has not advanced.
    /// </summary>
    public int Sequence { get; set; }
}

public sealed class RemotePullApplyResult
{
    public int AppliedCount { get; set; }

    public bool HasDeferredChanges { get; set; }

    public List<SyncConflict> Conflicts { get; } = [];
}

public sealed class RemoteTaskUpdate
{
    public TodoTask Task { get; set; } = new();

    public string Href { get; set; } = string.Empty;

    public string? ETag { get; set; }
}

public sealed class SyncConflict
{
    public long AccountId { get; set; }

    public long ListId { get; set; }

    public string ListName { get; set; } = string.Empty;

    public TodoTask LocalTask { get; set; } = new();

    public TodoTask? RemoteTask { get; set; }

    public string RemoteHref { get; set; } = string.Empty;

    public string? RemoteETag { get; set; }

    public bool RemoteDeleted { get; set; }
}

public sealed class SyncRunResult
{
    public SyncRunStatus Status { get; set; }

    public int PulledCount { get; set; }

    public int PushedCount { get; set; }

    public string? ErrorMessage { get; set; }

    public List<SyncConflict> Conflicts { get; } = [];
}
