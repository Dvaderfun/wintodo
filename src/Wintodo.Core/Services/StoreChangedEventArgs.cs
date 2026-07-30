namespace Wintodo.Core.Services;

public sealed class StoreChangedEventArgs : EventArgs
{
    public StoreChangedEventArgs(IEnumerable<long> listIds, IEnumerable<string> taskUids)
    {
        ListIds = listIds.Distinct().ToArray();
        TaskUids = taskUids.Distinct(StringComparer.Ordinal).ToArray();
    }

    public IReadOnlyList<long> ListIds { get; }

    public IReadOnlyList<string> TaskUids { get; }
}
