using Wintodo.Core.Services;

namespace Wintodo.ViewModels;

public sealed partial class ShellViewModel
{
    private Task PrepareUndoAsync() => IsDetailPaneOpen ? Detail.FlushUndoEditorAsync() : Task.CompletedTask;

    private void OnUndoStoreChanged(object? sender, StoreChangedEventArgs args)
    {
        if (args.IsRemoteChange) Undo.Invalidate(args.TaskUids);
    }

    private async Task RefreshAfterUndoAsync()
    {
        await RefreshNavigationAsync();
        await TaskList.ReloadAsync();
        if (IsDetailPaneOpen)
        {
            if (await _taskStore.GetTaskAsync(Detail.Uid) is not null) await Detail.LoadAsync(Detail.Uid);
            else CloseDetail();
        }
    }
}
