namespace Wintodo.Core.Models;

public sealed class TaskAlarm
{
    public long Id { get; set; }

    public long TaskId { get; set; }

    public string Uid { get; set; } = string.Empty;

    public AlarmAction Action { get; set; }

    public DateTimeOffset? Trigger { get; set; }

    public long? TriggerOffsetSeconds { get; set; }

    public AlarmRelation TriggerRelated { get; set; }

    public string? Description { get; set; }

    public DateTimeOffset? SnoozedUntil { get; set; }

    public bool IsAcknowledged { get; set; }

    public string? ScheduledToastTag { get; set; }
}
