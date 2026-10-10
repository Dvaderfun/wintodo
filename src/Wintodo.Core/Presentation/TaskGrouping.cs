using Wintodo.Core.Models;
using Wintodo.Core.Queries;
using Wintodo.Core.Services;

namespace Wintodo.Core.Presentation;

public enum TaskGroupKind
{
    Hidden,
    Search,
    Planned,
    Completed,
}

public sealed record TaskGroupList(long Id, string Uid, double SortOrder);

public sealed record TaskGroup(
    TaskGroupKind Kind,
    string Key,
    IReadOnlyList<string> TaskUids,
    int Count,
    bool IsExpanded = true,
    long? ListId = null);

/// <summary>Groups already ordered query results without changing their order.</summary>
public static class TaskGrouping
{
    private static readonly string[] PlannedBuckets =
        ["Overdue", "Today", "Tomorrow", "ThisWeek", "Later", "NoDate"];

    public static IReadOnlyList<TaskGroup> Build(
        ListQuery? query,
        IReadOnlyList<TodoTask> active,
        IReadOnlyList<TodoTask> completed,
        bool showCompleted,
        string searchText,
        IClock clock,
        DayOfWeek weekStart,
        IReadOnlyList<TaskGroupList> lists,
        bool completedExpanded)
    {
        var groups = new List<TaskGroup>();
        if (!string.IsNullOrEmpty(searchText))
        {
            foreach (TaskGroupList list in lists.OrderBy(list => list.SortOrder))
            {
                string[] matches = active.Concat(completed)
                    .Where(task => task.ListId == list.Id).Select(task => task.Uid).ToArray();
                if (matches.Length > 0)
                {
                    groups.Add(new(TaskGroupKind.Search, list.Uid, matches, matches.Length,
                        ListId: list.Id));
                }
            }
        }
        else if (query?.Kind == ListQueryKind.Planned)
        {
            var buckets = PlannedBuckets.ToDictionary(
                key => key, _ => new List<string>(), StringComparer.Ordinal);
            foreach (TodoTask task in active.Concat(completed))
            {
                buckets[GetPlannedBucket(task.Due, clock, weekStart)].Add(task.Uid);
            }
            foreach (string key in PlannedBuckets)
            {
                if (buckets[key].Count > 0)
                {
                    groups.Add(new(TaskGroupKind.Planned, key, buckets[key], buckets[key].Count));
                }
            }
        }
        else if (query?.Kind == ListQueryKind.Completed)
        {
            AddHidden(completed);
        }
        else
        {
            AddHidden(active);
            if (showCompleted && completed.Count > 0)
            {
                groups.Add(new(TaskGroupKind.Completed, "Completed",
                    completedExpanded ? completed.Select(task => task.Uid).ToArray() : [],
                    completed.Count, completedExpanded));
            }
        }
        return groups;

        void AddHidden(IReadOnlyList<TodoTask> tasks)
        {
            if (tasks.Count > 0)
            {
                groups.Add(new(TaskGroupKind.Hidden, string.Empty,
                    tasks.Select(task => task.Uid).ToArray(), tasks.Count));
            }
        }
    }

    public static string GetPlannedBucket(DateTimeOffset? due, IClock clock, DayOfWeek weekStart)
    {
        if (due is null)
        {
            return "NoDate";
        }
        DateOnly dueDate = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(due.Value, clock.LocalTimeZone).DateTime);
        DateOnly today = clock.Today;
        int offset = dueDate.DayNumber - today.DayNumber;
        if (offset < 0)
        {
            return "Overdue";
        }
        if (offset == 0)
        {
            return "Today";
        }
        if (offset == 1)
        {
            return "Tomorrow";
        }
        int dayIndex = ((int)today.DayOfWeek - (int)weekStart + 7) % 7;
        return dueDate <= today.AddDays(6 - dayIndex) ? "ThisWeek" : "Later";
    }
}
