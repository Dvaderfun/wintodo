using Wintodo.Core.Models;
using Wintodo.Core.Queries;
using Wintodo.Core.Services;

namespace Wintodo.Core.Presentation;

public enum TaskMetadataKind
{
    ListName,
    DueDate,
    Repeat,
    MyDay,
}

public sealed record TaskMetadataPart(TaskMetadataKind Kind, string? Text = null, DateOnly? Date = null);

public sealed record TaskMetadataResult(
    IReadOnlyList<TaskMetadataPart> Parts, bool IsOverdue, bool IsDueToday);

/// <summary>Builds the currently shipped subtitle parts; the App owns localisation.</summary>
public static class TaskMetadataLine
{
    public static TaskMetadataResult Build(TodoTask task, ListQueryKind? context, IClock clock, string? listName)
    {
        DateOnly? dueDate = task.Due is { } due
            ? DateOnly.FromDateTime(task.DueIsDateOnly
                ? due.DateTime : TimeZoneInfo.ConvertTime(due, clock.LocalTimeZone).DateTime)
            : null;
        bool completed = task.Status == Models.TaskStatus.Completed;
        var parts = new List<TaskMetadataPart>(4);
        if (context != ListQueryKind.UserList && !string.IsNullOrEmpty(listName))
        {
            parts.Add(new(TaskMetadataKind.ListName, Text: listName));
        }
        if (dueDate is not null)
        {
            parts.Add(new(TaskMetadataKind.DueDate, Date: dueDate));
        }
        if (!string.IsNullOrWhiteSpace(task.RRule))
        {
            parts.Add(new(TaskMetadataKind.Repeat));
        }
        if (context != ListQueryKind.MyDay && TaskDateLabels.IsInMyDay(task, clock))
        {
            parts.Add(new(TaskMetadataKind.MyDay));
        }
        return new(parts, dueDate < clock.Today && !completed, dueDate == clock.Today && !completed);
    }
}
