using Wintodo.Core.Models;

namespace Wintodo.Core.Import;

/// <summary>
/// Everything read from one account of another task manager, already mapped to
/// Wintodo models. External ids stay on the snapshot so the store can link
/// imported rows back to their source and skip them on the next run.
/// </summary>
public sealed class ImportSnapshot
{
    public string Provider { get; init; } = string.Empty;

    public string AccountKey { get; init; } = string.Empty;

    public string AccountDisplayName { get; init; } = string.Empty;

    public List<ImportList> Lists { get; } = [];

    /// <summary>
    /// Extension properties the importer owns. On re-import these are replaced
    /// in a task's unknown-property passthrough; every other entry is kept.
    /// </summary>
    public List<string> ManagedPropertyNames { get; } = [];
}

public enum ImportListKind
{
    User = 0,
    Default = 1,
    FlaggedEmail = 2,
}

public sealed class ImportList
{
    public string ExternalId { get; init; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? IconEmoji { get; set; }

    public ImportListKind Kind { get; init; }

    public bool IsShared { get; init; }

    public bool IsOwner { get; init; } = true;

    public List<ImportTask> Tasks { get; } = [];
}

public sealed class ImportTask
{
    public string ExternalId { get; init; } = string.Empty;

    /// <summary>
    /// The mapped task. <see cref="TodoTask.Uid"/>, <see cref="TodoTask.ListId"/>
    /// and <see cref="TodoTask.ParentUid"/> are assigned by the store.
    /// </summary>
    public TodoTask Task { get; init; } = new();

    public List<ImportTask> Steps { get; } = [];

    public List<ImportAttachment> Attachments { get; } = [];
}

public sealed class ImportAttachment
{
    public string ExternalId { get; init; } = string.Empty;

    public string FileName { get; init; } = string.Empty;

    public string? MimeType { get; init; }

    public long SizeBytes { get; set; }

    /// <summary>Opaque reference the source's reader uses to download the file.</summary>
    public string? SourcePath { get; init; }

    /// <summary>
    /// Absolute path of the downloaded file. The store moves it under the
    /// attachments folder after the import commits; attachments without a
    /// staged file are skipped.
    /// </summary>
    public string? StagedPath { get; set; }
}

public sealed class ImportOptions
{
    /// <summary>When set, a task changed on both sides takes the source version.</summary>
    public bool OverwriteLocalEdits { get; init; }

    /// <summary>App data folder that holds <c>attachments\</c>. Required to import attachments.</summary>
    public string? AttachmentRoot { get; init; }
}
