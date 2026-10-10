namespace Wintodo.Core.Services;

public sealed class StoreChangedEventArgs : EventArgs
{
    public StoreChangedEventArgs(IEnumerable<long> listIds, IEnumerable<string> taskUids, bool isRemoteChange = false)
    {
        ListIds = listIds.Distinct().ToArray();
        TaskUids = taskUids.Distinct(StringComparer.Ordinal).ToArray();
        IsRemoteChange = isRemoteChange;
    }

    public IReadOnlyList<long> ListIds { get; }

    public IReadOnlyList<string> TaskUids { get; }

    public bool IsRemoteChange { get; }
}
