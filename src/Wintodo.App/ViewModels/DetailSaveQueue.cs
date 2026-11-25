using Wintodo.Core.Services;

namespace Wintodo.ViewModels;

// Accessed on the editor's synchronization context. Only the delay is cancellable;
// once a write starts, the next edit boundary waits for it before saving newer text.
internal sealed class DetailSaveQueue
{
    private CancellationTokenSource? _delay;
    private PendingSave? _pending;
    private Task<bool>? _writing;
    private TaskDetailFields _writingFields;

    internal Func<TimeSpan, CancellationToken, Task> DelayAsync { get; init; } = Task.Delay;
    internal int Revision { get; private set; }
    internal bool HasPendingEdits => _pending is not null || _writing is not null;

    internal bool IsSaving(TaskDetailFields field) =>
        ((_writingFields | (_pending?.Fields ?? TaskDetailFields.None)) & field) != 0;

    internal void Schedule(TimeSpan delay, TaskDetailFields fields, Func<TaskDetailFields, Task<bool>> save)
    {
        Revision++;
        // A selection change drains this queue. Coalesce only the fields edited by this owner.
        _pending = new PendingSave(save, fields | (_pending?.Fields ?? TaskDetailFields.None));
        CancelDelay();
        _delay = new CancellationTokenSource();
        _ = SaveAfterDelayAsync(delay, _delay.Token);
    }

    private async Task SaveAfterDelayAsync(TimeSpan delay, CancellationToken token)
    {
        try
        {
            await DelayAsync(delay, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return;
        }

        if (token.IsCancellationRequested) return;
        CancelDelay();
        await SaveReadyEditsAsync();
    }

    internal async Task<bool> FlushAsync()
    {
        while (HasPendingEdits)
        {
            CancelDelay();
            if (!await SaveReadyEditsAsync()) return false;
        }
        return true;
    }

    private Task<bool> SaveReadyEditsAsync()
    {
        if (_writing is not null) return _writing;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _writing = completion.Task;
        _ = WriteAsync(completion);
        return completion.Task;
    }

    private async Task WriteAsync(TaskCompletionSource<bool> completion)
    {
        try
        {
            while (_delay is null && _pending is { } save)
            {
                _pending = null;
                _writingFields = save.Fields;
                bool saved = await save.WriteAsync(save.Fields);
                _writingFields = TaskDetailFields.None;
                if (saved) continue;
                _pending = _pending is { } newer
                    ? newer with { Fields = newer.Fields | save.Fields }
                    : save;
                _writing = null;
                completion.SetResult(false);
                return;
            }
            _writing = null;
            completion.SetResult(true);
        }
        catch (Exception exception)
        {
            _writingFields = TaskDetailFields.None;
            _writing = null;
            completion.SetException(exception);
        }
    }

    private sealed record PendingSave(Func<TaskDetailFields, Task<bool>> WriteAsync, TaskDetailFields Fields);

    private void CancelDelay()
    {
        _delay?.Cancel();
        _delay?.Dispose();
        _delay = null;
    }
}
