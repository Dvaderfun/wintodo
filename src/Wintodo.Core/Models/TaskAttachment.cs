namespace Wintodo.Core.Models;

public sealed class TaskAttachment
{
    public long Id { get; set; }

    public long TaskId { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string RelativePath { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public string? MimeType { get; set; }

    public DateTimeOffset Added { get; set; }
}
