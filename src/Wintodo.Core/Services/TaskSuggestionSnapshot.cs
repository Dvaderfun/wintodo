using Wintodo.Core.Models;

namespace Wintodo.Core.Services;

/// <summary>The occurrence displayed to the user before a suggestion action is requested.</summary>
public sealed record TaskSuggestionSnapshot(string Uid, int Sequence, string? RemoteETag,
    DateTimeOffset? Due, bool DueIsDateOnly, DateTimeOffset? Start, bool StartIsDateOnly,
    string? RRule, DateOnly? MyDayExcludedDate)
{
    public static TaskSuggestionSnapshot FromTask(TodoTask task) => new(task.Uid, task.Sequence,
        task.RemoteETag, task.Due, task.DueIsDateOnly, task.Start, task.StartIsDateOnly,
        task.RRule, task.MyDayExcludedDate);
}
