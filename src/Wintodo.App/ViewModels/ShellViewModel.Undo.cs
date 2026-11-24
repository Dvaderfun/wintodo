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
            if (TaskList.SelectedTask is { } selected) await OpenDetailAsync(selected, refresh: true);
            else CloseDetail();
        }
    }
}
