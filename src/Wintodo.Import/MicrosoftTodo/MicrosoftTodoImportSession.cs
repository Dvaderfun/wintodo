namespace Wintodo.Import.MicrosoftTodo;

/// <summary>
/// Owns the authenticated connection and work lifetime of one import wizard.
/// Its owner calls lifecycle methods on the UI thread; ending waits for work,
/// including an already-started uncancellable save, before releasing the connection.
/// </summary>
public sealed class MicrosoftTodoImportSession
{
    private CancellationTokenSource? _cancellation;
    private TaskCompletionSource? _completion;
    private GraphTodoClient? _client;
    private Task? _ending;
    private bool _dismissed;

    public MicrosoftTodoReader? Reader { get; private set; }
    public int Generation { get; private set; }
    public bool IsDismissed => _dismissed;

    public void Reset()
    {
        if (_completion is not null || _ending is { IsCompleted: false })
            throw new InvalidOperationException("Import work must finish before resetting its session.");
        ReleaseConnection();
        _dismissed = false;
        _ending = null;
        Generation++;
    }

    public CancellationToken BeginWork()
    {
        if (_dismissed || _completion is not null)
            throw new InvalidOperationException("The import session cannot start this operation.");
        _cancellation = new CancellationTokenSource();
        _completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Generation++;
        return _cancellation.Token;
    }

    public bool IsCurrent(int generation) => !_dismissed && Generation == generation;

    public void Connect(GraphTodoClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (_dismissed || _cancellation?.IsCancellationRequested == true)
        {
            client.Dispose();
            throw new OperationCanceledException();
        }
        ReleaseConnection();
        _client = client;
        Reader = new MicrosoftTodoReader(client);
    }

    public void ReleaseConnection()
    {
        Reader = null;
        _client?.Dispose();
        _client = null;
    }

    public void CancelWork() => _cancellation?.Cancel();

    public void EndWork()
    {
        Generation++;
        _cancellation?.Dispose();
        _cancellation = null;
        TaskCompletionSource? completed = _completion;
        _completion = null;
        completed?.TrySetResult();
    }

    public Task EndAsync()
    {
        if (_ending is not null) return _ending;
        _dismissed = true;
        Generation++;
        CancelWork();
        _ending = EndCoreAsync(_completion?.Task ?? Task.CompletedTask);
        return _ending;
    }

    private async Task EndCoreAsync(Task completion)
    {
        await completion;
        ReleaseConnection();
    }
}
