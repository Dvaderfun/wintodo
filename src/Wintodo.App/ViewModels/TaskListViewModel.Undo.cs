using Microsoft.UI.Dispatching;
using Wintodo.Core.Services;
using Wintodo.Services;

namespace Wintodo.ViewModels;

public sealed partial class TaskListViewModel
{
    private readonly DispatcherQueue _undoDispatcher = DispatcherQueue.GetForCurrentThread();
    internal event Func<Task>? UndoCompleted;
    internal event Func<Task>? UndoPreparing;

    internal void DisposeUndo()
    {
        _undo.Changed -= OnUndoChanged;
        _undoCancellation?.Cancel();
        _undoCancellation?.Dispose();
    }

    private void OnUndoChanged(object? sender, EventArgs args)
    {
        if (_undoDispatcher.HasThreadAccess) RefreshUndo();
        else _undoDispatcher.TryEnqueue(RefreshUndo);
    }

    private void RefreshUndo()
    {
        UndoDeleteCommand.NotifyCanExecuteChanged();
        _undoCancellation?.Cancel();
        _undoCancellation?.Dispose();
        _undoCancellation = null;
        UndoMessage = _undo.Label ?? string.Empty;
        IsUndoVisible = _undo.Count > 0;
        if (IsUndoVisible)
        {
            _undoCancellation = new CancellationTokenSource();
            _ = DismissUndoAsync(_undoCancellation.Token);
        }
    }

    private async Task DismissUndoAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            IsUndoVisible = false;
        }
        catch (OperationCanceledException) { }
    }

    private void RecordTaskUndo(IReadOnlyList<TaskUndoState> states, string key)
    {
        _undo.Record(string.Format(DisplayCulture.Current, _resources.GetString(key),
            states.Count == 1 ? states[0].Task.Summary : states.Count),
            states.Select(state => state.Task.Uid), token => _taskStore.RestoreTaskFieldsAsync(states, token));
    }

    private static TaskUndoFields UndoFields(Core.Services.TaskBatchAction action) => action switch
    {
        TaskBatchAction.Complete or TaskBatchAction.Uncomplete => TaskUndoFields.Completion,
        TaskBatchAction.MarkImportant or TaskBatchAction.RemoveImportance => TaskUndoFields.Importance,
        TaskBatchAction.AddToMyDay or TaskBatchAction.RemoveFromMyDay => TaskUndoFields.MyDay,
        TaskBatchAction.SetDueDate => TaskUndoFields.Due,
        TaskBatchAction.Move => TaskUndoFields.List,
        TaskBatchAction.Delete => TaskUndoFields.Deleted,
        _ => TaskUndoFields.None,
    };
}
