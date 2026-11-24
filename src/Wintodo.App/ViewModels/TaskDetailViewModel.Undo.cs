using Wintodo.Core.Services;
using Wintodo.Services;

namespace Wintodo.ViewModels;

public sealed partial class TaskDetailViewModel
{
    internal async Task FlushUndoEditorAsync()
    {
        if (!await FlushPendingEditsAsync()) throw new InvalidOperationException("Cannot undo while the editor has unsaved changes.");
    }

    private void RecordUndo(IReadOnlyList<TaskUndoState> states, string key)
    {
        _undo.Record(string.Format(DisplayCulture.Current, _resources.GetString(key), states[0].Task.Summary),
            states.Select(state => state.Task.Uid).Append(states[0].Task.ParentUid ?? states[0].Task.Uid),
            token => _taskStore.RestoreTaskFieldsAsync(states, token));
    }

    private async Task<IReadOnlyList<TaskUndoState>> CaptureUndoAsync(string ownerUid, TaskUndoFields fields)
    {
        // Include pending title/note edits before capturing a destructive field change.
        if (!await FlushPendingEditsAsync()) throw new InvalidOperationException("Cannot capture an unsaved task.");
        return await _taskStore.CaptureTaskUndoAsync([ownerUid], fields);
    }
}
