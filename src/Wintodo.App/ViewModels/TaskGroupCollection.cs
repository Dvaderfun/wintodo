using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Windows.Foundation.Collections;

namespace Wintodo.ViewModels;

// Partial concrete type lets CsWinRT generate collection interfaces for Release/AOT.
public sealed partial class TaskGroupCollection : ObservableCollection<object>, IObservableVector<object>
{
    public event VectorChangedEventHandler<object>? VectorChanged;

    protected override void MoveItem(int oldIndex, int newIndex)
    {
        // WinRT has no Move event. Notify after each actual mutation so native
        // consumers see the collection state described by each notification.
        object item = this[oldIndex];
        base.RemoveItem(oldIndex);
        base.InsertItem(newIndex, item);
    }

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        base.OnCollectionChanged(e);
        CollectionChange kind = e.Action switch
        {
            NotifyCollectionChangedAction.Add => CollectionChange.ItemInserted,
            NotifyCollectionChangedAction.Remove => CollectionChange.ItemRemoved,
            NotifyCollectionChangedAction.Replace => CollectionChange.ItemChanged,
            _ => CollectionChange.Reset,
        };
        int index = e.Action == NotifyCollectionChangedAction.Remove ? e.OldStartingIndex : e.NewStartingIndex;
        VectorChanged?.Invoke(this, new Change(kind, (uint)Math.Max(0, index)));
    }

    private sealed partial class Change(CollectionChange change, uint index) : IVectorChangedEventArgs
    {
        public CollectionChange CollectionChange => change;
        public uint Index => index;
    }
}
