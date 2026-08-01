namespace Wintodo.Core.Models;

public sealed class PendingReminder
{
    public string TaskUid { get; set; } = string.Empty;

    public string TaskSummary { get; set; } = string.Empty;

    public TaskAlarm Alarm { get; set; } = new();

    public DateTimeOffset EffectiveTrigger { get; set; }
}
