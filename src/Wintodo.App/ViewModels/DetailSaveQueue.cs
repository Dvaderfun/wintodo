namespace Wintodo.ViewModels;

// Accessed on the editor's synchronization context. Only the delay is cancellable;
// once a write starts, the next edit boundary waits for it before saving newer text.
internal sealed class DetailSaveQueue
{
    private CancellationTokenSource? _delay;
    private PendingSave? _pending;
    private Task<bool>? _writing;

    internal Func<TimeSpan, CancellationToken, Task> DelayAsync { get; init; } = Task.Delay;
    internal int Revision { get; private set; }
    internal bool HasPendingEdits => _pending is not null || _writing is not null;

    internal void Schedule(TimeSpan delay, Func<Task<bool>> save, bool writesWholeTask = false)
    {
        Revision++;
        // A whole-task save also reads the latest title and note. Do not
        // replace a pending repeat edit with a later text-only write.
        if (writesWholeTask || _pending is not { WritesWholeTask: true })
            _pending = new PendingSave(save, writesWholeTask);
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
                if (await save.WriteAsync()) continue;
                if (save.WritesWholeTask || _pending is null) _pending = save;
                _writing = null;
                completion.SetResult(false);
                return;
            }
            _writing = null;
            completion.SetResult(true);
        }
        catch (Exception exception)
        {
            _writing = null;
            completion.SetException(exception);
        }
    }

    private sealed record PendingSave(Func<Task<bool>> WriteAsync, bool WritesWholeTask);

    private void CancelDelay()
    {
        _delay?.Cancel();
        _delay?.Dispose();
        _delay = null;
    }
}
