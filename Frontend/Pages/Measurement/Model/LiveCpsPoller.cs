using Frontend.Calculation.Measurement;

namespace Frontend.Pages.Measurement;

/// <summary>
/// Reads the newest CPS off the UI thread about once per interval and publishes it on the caller's context.
/// A read that finishes after <see cref="Stop"/> is dropped.
/// </summary>
public sealed class LiveCpsPoller(ILiveCpsReader reader, TimeSpan interval)
{
    private CancellationTokenSource? polling;

    public bool IsRunning => polling is not null;

    /// <param name="publish">Gets the finite reading, or null when there is none.</param>
    public void Start(Action<double?> publish, CancellationToken lifetime)
    {
        if (polling is not null)
        {
            return;
        }

        polling = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        _ = PollAsync(publish, polling.Token);
    }

    public void Stop()
    {
        polling?.Cancel();
        polling?.Dispose();
        polling = null;
    }

    private async Task PollAsync(Action<double?> publish, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var cps = await Task.Run(reader.ReadLatestCps, token);

                if (token.IsCancellationRequested)
                {
                    return;
                }

                publish(cps is { } value && double.IsFinite(value) ? value : null);
                await Task.Delay(interval, token);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
