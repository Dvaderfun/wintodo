namespace Wintodo.Core.Import;

/// <summary>
/// Counts produced by both the dry run and the real import. Task counts cover
/// root tasks; steps are counted separately.
/// </summary>
public sealed class ImportOutcome
{
    public int NewLists { get; set; }

    public int ExistingLists { get; set; }

    public int SkippedLists { get; set; }

    public int NewTasks { get; set; }

    public int NewSteps { get; set; }

    public int UpdatedTasks { get; set; }

    public int UnchangedTasks { get; set; }

    public int KeptLocalTasks { get; set; }

    public int SkippedDeletedTasks { get; set; }

    public int PossibleDuplicates { get; set; }

    public int NewAttachments { get; set; }

    /// <summary>Identifies the run so its new items can be removed together. Empty for a dry run.</summary>
    public string RunId { get; set; } = string.Empty;

    public bool HasChanges =>
        NewLists > 0 || NewTasks > 0 || NewSteps > 0 || UpdatedTasks > 0 || NewAttachments > 0;
}

public sealed class ImportSourceInfo
{
    public string Provider { get; init; } = string.Empty;

    public string AccountKey { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public DateTimeOffset? LastImport { get; init; }
}
