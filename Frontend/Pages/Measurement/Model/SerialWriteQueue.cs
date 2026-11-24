namespace Frontend.Pages.Measurement;

/// <summary>Database writes run one after another on the thread pool, in the order they were requested.</summary>
public sealed class SerialWriteQueue
{
    // The tail of the queue. Never faults, so one failed write does not stop the next.
    private Task tail = Task.CompletedTask;

    /// <summary>Queues <paramref name="write"/> after every earlier one; the returned task faults if this write fails.</summary>
    public Task Enqueue(Func<Task> write)
    {
        var previous = tail;
        var current = Task.Run(async () =>
        {
            await previous;
            await write();
        });
        tail = current.ContinueWith(_ => { }, TaskScheduler.Default);
        return current;
    }

    /// <summary>Completes when every write queued so far has finished (successfully or not).</summary>
    public Task WhenIdle => tail;

    /// <summary>Waits up to <paramref name="timeout"/> for queued writes. Safe on the UI thread: writes never need it.</summary>
    public bool WaitForPending(TimeSpan timeout) => tail.Wait(timeout);
}
