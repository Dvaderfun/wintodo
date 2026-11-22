using Wintodo.Core.Services;
using Wintodo.Services;

namespace Wintodo.ViewModels;

public sealed partial class TaskDetailViewModel
{
    internal async Task FlushUndoEditorAsync()
    {
        if (_saveCancellation is null) return;
        CancelPendingSave();
        if (!await PersistModelAsync()) throw new InvalidOperationException("The editor has unsaved changes.");
    }

    private void RecordUndo(IReadOnlyList<TaskUndoState> states, string key)
    {
        _undo.Record(string.Format(DisplayCulture.Current, _resources.GetString(key), states[0].Task.Summary),
            states.Select(state => state.Task.Uid).Append(states[0].Task.ParentUid ?? states[0].Task.Uid),
            token => _taskStore.RestoreTaskFieldsAsync(states, token));
    }

    private async Task<IReadOnlyList<TaskUndoState>> CaptureUndoAsync(TaskUndoFields fields)
    {
        // Include pending title/note edits before capturing a destructive field change.
        await FlushUndoEditorAsync();
        return await _taskStore.CaptureTaskUndoAsync([_model!.Uid], fields);
    }
}
