namespace Wintodo.Core.Services;

/// <summary>Session-only undo history shared by the shell's task editors.</summary>
public sealed class UndoService
{
    private readonly object _gate = new();
    private readonly List<Entry> _entries = [];
    private bool _isUndoing;

    public event EventHandler? Changed;
    public int Count { get { lock (_gate) return _entries.Count; } }
    public string? Label { get { lock (_gate) return _entries.LastOrDefault()?.Label; } }
    public bool CanUndo { get { lock (_gate) return !_isUndoing && _entries.Count > 0; } }

    public void Record(string label, IEnumerable<string> taskUids, Func<CancellationToken, Task> revert)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(taskUids);
        ArgumentNullException.ThrowIfNull(revert);
        lock (_gate)
        {
            _entries.Add(new Entry(label, taskUids.ToHashSet(StringComparer.Ordinal), revert));
            if (_entries.Count > 20) _entries.RemoveAt(0);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Invalidate(IEnumerable<string> taskUids)
    {
        var changed = taskUids.ToHashSet(StringComparer.Ordinal);
        int removed;
        lock (_gate) removed = _entries.RemoveAll(entry => entry.TaskUids.Overlaps(changed));
        if (removed > 0) Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<bool> UndoAsync(CancellationToken cancellationToken = default)
    {
        Entry entry;
        lock (_gate)
        {
            if (_isUndoing || _entries.Count == 0) return false;
            entry = _entries[^1];
            _isUndoing = true;
        }
        Changed?.Invoke(this, EventArgs.Empty);
        try
        {
            await entry.Revert(cancellationToken);
            lock (_gate) _entries.Remove(entry);
            return true;
        }
        finally
        {
            lock (_gate) _isUndoing = false;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed record Entry(string Label, HashSet<string> TaskUids, Func<CancellationToken, Task> Revert);
}
