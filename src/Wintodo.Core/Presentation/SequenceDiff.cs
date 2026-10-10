namespace Wintodo.Core.Presentation;

public enum SequenceChangeKind { Insert, Remove, Move }

public readonly record struct SequenceChange<T>(SequenceChangeKind Kind, T Key, int Index, int Destination = -1);

/// <summary>Diffs unique keys, keeping a longest increasing subsequence in place.</summary>
public static class SequenceDiff
{
    public static IReadOnlyList<SequenceChange<T>> Build<T>(IReadOnlyList<T> before, IReadOnlyList<T> after)
        where T : notnull
    {
        if (before.SequenceEqual(after))
        {
            return [];
        }
        if (before.Count == 0)
        {
            return after.Select((key, index) => new SequenceChange<T>(SequenceChangeKind.Insert, key, index)).ToArray();
        }

        var targetKeys = new HashSet<T>(after);
        var oldIndices = new Dictionary<T, int>();
        for (int i = 0; i < before.Count; i++)
        {
            if (!oldIndices.TryAdd(before[i], i))
            {
                throw new ArgumentException("Sequence keys must be unique.", nameof(before));
            }
        }
        if (targetKeys.Count != after.Count)
        {
            throw new ArgumentException("Sequence keys must be unique.", nameof(after));
        }

        var changes = new List<SequenceChange<T>>();
        var current = before.ToList();
        for (int i = current.Count - 1; i >= 0; i--)
        {
            if (!targetKeys.Contains(current[i]))
            {
                changes.Add(new(SequenceChangeKind.Remove, current[i], i));
                current.RemoveAt(i);
            }
        }

        // Find stable retained keys in O(n log n); new keys do not affect moves.
        var retained = after.Where(oldIndices.ContainsKey).ToArray();
        int[] tails = new int[retained.Length];
        int[] previous = new int[retained.Length];
        int length = 0;
        for (int i = 0; i < retained.Length; i++)
        {
            int value = oldIndices[retained[i]];
            int low = 0, high = length;
            while (low < high)
            {
                int middle = (low + high) / 2;
                if (oldIndices[retained[tails[middle]]] < value) low = middle + 1;
                else high = middle;
            }
            previous[i] = low > 0 ? tails[low - 1] : -1;
            tails[low] = i;
            if (low == length) length++;
        }
        var stable = new HashSet<T>();
        for (int i = length > 0 ? tails[length - 1] : -1; i >= 0; i = previous[i])
        {
            stable.Add(retained[i]);
        }

        // Work backwards, inserting/moving immediately before the next target key.
        for (int i = after.Count - 1; i >= 0; i--)
        {
            T key = after[i];
            if (stable.Contains(key)) continue;
            int destination = i + 1 < after.Count ? current.IndexOf(after[i + 1]) : current.Count;
            if (!oldIndices.ContainsKey(key))
            {
                current.Insert(destination, key);
                changes.Add(new(SequenceChangeKind.Insert, key, destination));
            }
            else
            {
                int source = current.IndexOf(key);
                if (source < destination) destination--;
                current.RemoveAt(source);
                current.Insert(destination, key);
                changes.Add(new(SequenceChangeKind.Move, key, source, destination));
            }
        }
        return changes;
    }
}
