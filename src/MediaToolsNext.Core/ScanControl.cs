namespace MediaToolsNext.Core;

public sealed class ScanControl
{
    private readonly object _gate = new();
    private TaskCompletionSource _resumeSignal = NewSignal(completed: true);

    public bool IsPaused { get; private set; }

    public void Pause()
    {
        lock (_gate)
        {
            if (IsPaused) return;
            IsPaused = true;
            _resumeSignal = NewSignal(completed: false);
        }
    }

    public void Resume()
    {
        TaskCompletionSource signal;
        lock (_gate)
        {
            if (!IsPaused) return;
            IsPaused = false;
            signal = _resumeSignal;
        }
        signal.TrySetResult();
    }

    public Task WaitIfPausedAsync(CancellationToken cancellationToken)
    {
        Task task;
        lock (_gate)
            task = _resumeSignal.Task;
        return task.WaitAsync(cancellationToken);
    }

    private static TaskCompletionSource NewSignal(bool completed)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (completed) tcs.SetResult();
        return tcs;
    }
}
