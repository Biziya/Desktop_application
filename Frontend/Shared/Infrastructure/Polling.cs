namespace Frontend.Shared.Infrastructure;

/// <summary>The app's background checks: run a step, wait, repeat until cancelled.</summary>
public static class Polling
{
    /// <summary>
    /// Runs <paramref name="step"/> now and then every <paramref name="every"/> until <paramref name="token"/> is
    /// cancelled or <paramref name="keepGoing"/> (checked before each step) returns false. Cancellation during the wait
    /// ends the loop quietly; an exception from the step ends it and reaches the caller.
    /// </summary>
    public static async Task RunAsync(Func<Task> step, TimeSpan every, CancellationToken token, Func<bool>? keepGoing = null)
    {
        while (!token.IsCancellationRequested && (keepGoing?.Invoke() ?? true))
        {
            await step();

            try
            {
                await Task.Delay(every, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
