using System.Globalization;
using System.Text;
using Wintodo.Core.Models;
using Wintodo.Core.Queries;

namespace Wintodo.Core.Presentation;

public enum TaskSortField
{
    ManualOrder,
    UndefinedPriority,
    Priority,
    MissingDue,
    Due,
    Summary,
    Created,
    Completed,
    Id,
}

public readonly record struct TaskSortTerm(TaskSortField Field, bool Descending);

/// <summary>The existing query ordering policy, shared by SQL and pure comparisons.</summary>
public static class TaskSorting
{
    public static SortMode GetDefaultMode(ListQueryKind kind) => kind switch
    {
        ListQueryKind.Important => SortMode.Importance,
        ListQueryKind.Planned => SortMode.DueDate,
        ListQueryKind.All or ListQueryKind.Completed => SortMode.CreationDate,
        _ => SortMode.Manual,
    };

    /// <summary>Compares effective ordering, including Completed's fixed date ordering.</summary>
    public static bool IsDefault(SortMode mode, bool reverse, ListQueryKind kind) =>
        GetTerms(mode, reverse, kind).SequenceEqual(GetTerms(GetDefaultMode(kind), false, kind));

    public static IReadOnlyList<TaskSortTerm> GetTerms(
        SortMode mode, bool reverse, ListQueryKind kind = ListQueryKind.UserList)
    {
        if (kind == ListQueryKind.Completed)
        {
            return [new(TaskSortField.Completed, !reverse), new(TaskSortField.Id, !reverse)];
        }
        if (kind == ListQueryKind.Important && mode == SortMode.Importance)
        {
            return [new(TaskSortField.Priority, reverse), new(TaskSortField.Id, reverse)];
        }
        if (kind == ListQueryKind.Planned && mode == SortMode.DueDate)
        {
            return [new(TaskSortField.Due, reverse), new(TaskSortField.Id, reverse)];
        }
        return mode switch
        {
            SortMode.Manual => [new(TaskSortField.ManualOrder, reverse), new(TaskSortField.Id, reverse)],
            // The first term stays ascending even in reverse, matching the SQL expression.
            SortMode.Importance => [new(TaskSortField.UndefinedPriority, false), new(TaskSortField.Priority, reverse), new(TaskSortField.Id, reverse)],
            SortMode.DueDate => [new(TaskSortField.MissingDue, false), new(TaskSortField.Due, reverse), new(TaskSortField.Id, reverse)],
            SortMode.Alphabetical => [new(TaskSortField.Summary, reverse), new(TaskSortField.Id, reverse)],
            SortMode.CreationDate => [new(TaskSortField.Created, reverse), new(TaskSortField.Id, reverse)],
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown sort mode."),
        };
    }

    public static Comparison<TodoTask> Compare(
        SortMode mode, bool reverse, ListQueryKind kind = ListQueryKind.UserList)
    {
        IReadOnlyList<TaskSortTerm> terms = GetTerms(mode, reverse, kind);
        return (left, right) =>
        {
            foreach (TaskSortTerm term in terms)
            {
                int comparison = term.Field switch
                {
                    TaskSortField.ManualOrder => left.SortOrder.CompareTo(right.SortOrder),
                    TaskSortField.UndefinedPriority => (left.Priority == 0).CompareTo(right.Priority == 0),
                    TaskSortField.Priority => left.Priority.CompareTo(right.Priority),
                    TaskSortField.MissingDue => (left.Due is null).CompareTo(right.Due is null),
                    TaskSortField.Due => string.CompareOrdinal(StoredDue(left), StoredDue(right)),
                    TaskSortField.Summary => CompareSqliteNoCase(left.Summary, right.Summary),
                    TaskSortField.Created => left.Created.CompareTo(right.Created),
                    TaskSortField.Completed => Nullable.Compare(left.CompletedAt, right.CompletedAt),
                    TaskSortField.Id => left.Id.CompareTo(right.Id),
                    _ => throw new InvalidOperationException("Unknown sort field."),
                };
                if (comparison != 0)
                {
                    return term.Descending ? -Math.Sign(comparison) : Math.Sign(comparison);
                }
            }
            return 0;
        };
    }

    // In-place manual insertion intentionally keeps equal sort orders in input
    // order rather than applying the persisted query's id tie-breaker.
    public static int CompareManualOrder(double left, double right, bool reverse) =>
        reverse ? right.CompareTo(left) : left.CompareTo(right);

    private static string? StoredDue(TodoTask task)
    {
        if (task.Due is not { } due)
        {
            return null;
        }
        return task.DueIsDateOnly
            ? due.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : due.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
    }

    private static int CompareSqliteNoCase(string left, string right)
    {
        // SQLite NOCASE folds only ASCII, then compares the stored UTF-8 bytes.
        byte[] a = Encoding.UTF8.GetBytes(left);
        byte[] b = Encoding.UTF8.GetBytes(right);
        for (int index = 0; index < Math.Min(a.Length, b.Length); index++)
        {
            byte leftByte = Fold(a[index]);
            byte rightByte = Fold(b[index]);
            if (leftByte != rightByte)
            {
                return leftByte.CompareTo(rightByte);
            }
        }
        return a.Length.CompareTo(b.Length);

        static byte Fold(byte value) => value is >= (byte)'A' and <= (byte)'Z'
            ? (byte)(value + ('a' - 'A')) : value;
    }
}
