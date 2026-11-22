namespace Wintodo.Core.Models;

public sealed class TodoTask
{
    public long Id { get; set; }

    public string Uid { get; set; } = string.Empty;

    public long ListId { get; set; }

    public string? ParentUid { get; set; }

    public string Summary { get; set; } = string.Empty;

    public string? Description { get; set; }

    public TaskStatus Status { get; set; }

    public int? PercentComplete { get; set; }

    public int Priority { get; set; }

    public DateTimeOffset? Due { get; set; }

    public bool DueIsDateOnly { get; set; }

    public string? DueTzid { get; set; }

    public DateTimeOffset? Start { get; set; }

    public bool StartIsDateOnly { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset Created { get; set; }

    public DateTimeOffset LastModified { get; set; }

    public int Sequence { get; set; }

    public string? RRule { get; set; }

    public DateOnly? MyDayDate { get; set; }
    public DateOnly? MyDayExcludedDate { get; set; }

    public double SortOrder { get; set; }

    public string? Location { get; set; }

    public string? Url { get; set; }

    public Classification Class { get; set; }

    public List<string> Categories { get; } = [];

    public List<TaskAlarm> Alarms { get; } = [];

    public List<TaskAttachment> Attachments { get; } = [];

    public string? IcalUnknown { get; set; }

    public string? RemoteHref { get; set; }

    public string? RemoteETag { get; set; }

    public bool IsDirty { get; set; }

    public bool IsDeleted { get; set; }
}
