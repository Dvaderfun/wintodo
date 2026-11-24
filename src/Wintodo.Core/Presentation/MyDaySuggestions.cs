using Wintodo.Core.Models;
using Wintodo.Core.Services;

namespace Wintodo.Core.Presentation;

public enum SuggestionSection { Overdue, Upcoming, Today, Yesterday, Undated }

public sealed record MyDaySuggestion(TodoTask Task, SuggestionSection Section, DateOnly? DueDate);

public static class MyDaySuggestions
{
    public static IReadOnlyList<MyDaySuggestion> Build(IEnumerable<TodoTask> tasks, IClock clock)
    {
        DateOnly today = clock.Today;
        return tasks.Select(task => Classify(task, today, clock))
            .OfType<MyDaySuggestion>()
            .OrderBy(item => item.Section)
            .ThenBy(item => item.DueDate)
            .ThenBy(item => item.Task.DueIsDateOnly ? null : item.Task.Due)
            .ThenBy(item => item.Task.Uid, StringComparer.Ordinal)
            .ToArray();
    }

    public static MyDaySuggestion? Classify(TodoTask task, DateOnly today, IClock clock)
    {
        if (task.IsDeleted || task.ParentUid is not null || task.MyDayDate == today
            || task.Status is not (Models.TaskStatus.NeedsAction or Models.TaskStatus.InProcess))
            return null;

        DateOnly? due = TaskDateLabels.GetDueDate(task, clock);
        if (due == today && task.MyDayExcludedDate != today) return null;
        if (due < today) return new(task, SuggestionSection.Overdue, due);
        if (due == today) return new(task, SuggestionSection.Today, due);
        if (due > today) return new(task, SuggestionSection.Upcoming, due);
        return new(task, task.MyDayDate?.DayNumber == today.DayNumber - 1
            ? SuggestionSection.Yesterday : SuggestionSection.Undated, null);
    }
}
