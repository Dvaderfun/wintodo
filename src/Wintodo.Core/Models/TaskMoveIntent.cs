namespace Wintodo.Core.Models;

public enum TaskMovePhase
{
    Planned = 0,
    DestinationAttempted,
    DestinationConfirmed,
    SourceRemoved,
}

/// <summary>Durable remote obligations for one UID; contains no credential.</summary>
public sealed class TaskMoveIntent
{
    public long Id { get; set; }
    public string TaskUid { get; set; } = string.Empty;
    public string GroupId { get; set; } = string.Empty;
    public long? PredecessorId { get; set; }
    public int Version { get; set; }
    public long? SourceAccountId { get; set; }
    public long SourceListId { get; set; }
    public string SourceListUid { get; set; } = string.Empty;
    public string? SourceHref { get; set; }
    public string? SourceETag { get; set; }
    public byte[]? SourcePayload { get; set; }
    public bool SourceRestored { get; set; }
    public string? RestoredSourceETag { get; set; }
    public bool SourceBindingVerified { get; set; } = true;
    public long? DestinationAccountId { get; set; }
    public long DestinationListId { get; set; }
    public string DestinationListUid { get; set; } = string.Empty;
    public string? DestinationHref { get; set; }
    public int LocalSequence { get; set; }
    public long ChangeId { get; set; }
    public TaskMovePhase Phase { get; set; }
    public byte[]? AttemptedPayload { get; set; }
    public int? AttemptedSequence { get; set; }
    public long? AttemptedChangeId { get; set; }
    public string? DestinationETag { get; set; }
    public SyncConflictChoice? ResolutionChoice { get; set; }
    public int? ResolutionSequence { get; set; }
    public long? ResolutionListId { get; set; }
    public byte[]? ResolutionSourcePayload { get; set; }
    public string? ResolutionSourceETag { get; set; }
    public string? ResolutionCopyUid { get; set; }
    public string? ResolutionCopyHref { get; set; }
    public byte[]? ResolutionCopyPayload { get; set; }
    public string? ResolutionCopyETag { get; set; }
    public bool ResolutionCopyConfirmed { get; set; }
    public bool ResolutionDestinationRemoved { get; set; }
    public bool ResolutionIsDestinationConflict { get; set; }
    public bool DestinationRemoved { get; set; }
    public DateTimeOffset Created { get; set; }
    public DateTimeOffset Updated { get; set; }
}

public sealed class TaskMoveAttempt
{
    public long MoveId { get; set; }
    public int ExpectedSequence { get; set; }
    public string DestinationHref { get; set; } = string.Empty;
    public byte[] Payload { get; set; } = [];
}

public sealed class TaskMoveSourceRestore
{
    public long MoveId { get; set; }
    public int ExpectedVersion { get; set; }
    public TodoTask SourceTask { get; set; } = new();
    public string SourceHref { get; set; } = string.Empty;
    public string SourceETag { get; set; } = string.Empty;
    public int ExpectedSequence { get; set; }
}

public sealed class TaskMoveUpdate
{
    public TaskMoveIntent Intent { get; set; } = new();
    public int ExpectedVersion { get; set; }
    public int? ExpectedTaskSequence { get; set; }
}

public sealed class PendingTaskMoveException : InvalidOperationException
{
    public PendingTaskMoveException()
        : base("Pending task moves must finish before changing their account or deleting their list.") { }
}
