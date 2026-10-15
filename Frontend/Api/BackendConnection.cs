using Frontend.Shared.Infrastructure;

namespace Frontend.Api;

public enum BackendState
{
    Checking,
    Online,
    Offline
}

/// <summary>
/// Backend reachability for the sidebar indicator only. There is no health endpoint, so the periodic check is a HEAD
/// request to the base URL (<see cref="ApiClient.ProbeAsync"/>): nothing is downloaded and no status endpoint is used.
/// Every other request also updates the state through <see cref="ApiClient.Reached"/>. Device Status (Bluetooth and
/// databases) is checked locally and never affects this state. One instance lives as long as the main window: checks
/// never overlap and stop on <see cref="Dispose"/>.
/// </summary>
public sealed class BackendConnection : ObservableObject, IDisposable
{
    private readonly ApiClient api;
    private readonly TimeSpan interval;
    private readonly CancellationTokenSource lifetime = new();
    private Task? check;
    private BackendState state = BackendState.Checking;
    private DateTimeOffset? lastChecked;
    private bool started;
    private bool disposed;

    public BackendConnection(ApiClient api, TimeSpan interval)
    {
        this.api = api;
        this.interval = interval;
        api.Reached += OnReached;
    }

    /// <summary>Raised when the backend answers again after being offline (or for the first time).</summary>
    public event Action? CameOnline;

    public BackendState State
    {
        get => state;
        private set
        {
            if (Set(ref state, value))
            {
                Changed(nameof(StateText));
            }
        }
    }

    public string StateText => State switch
    {
        BackendState.Online => "Online",
        BackendState.Offline => "Offline",
        _ => "Checking…"
    };

    public string Tooltip =>
        $"Backend {api.BaseUrl}: {StateText}" + (lastChecked is { } time ? $" (checked {time:HH:mm:ss})" : "");

    /// <summary>Starts the periodic check (once).</summary>
    public void Start()
    {
        if (!started)
        {
            started = true;
            _ = RunAsync();
        }
    }

    /// <summary>Checks now; while a check is running, returns that check instead of starting another.</summary>
    public Task CheckNowAsync()
    {
        if (disposed)
        {
            return Task.CompletedTask;
        }

        if (check is { IsCompleted: false })
        {
            return check;
        }

        return check = CheckAsync();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        api.Reached -= OnReached;
        lifetime.Cancel();
        lifetime.Dispose();
    }

    private async Task RunAsync()
    {
        var token = lifetime.Token;

        while (!token.IsCancellationRequested)
        {
            await CheckNowAsync();

            try
            {
                await Task.Delay(interval, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task CheckAsync()
    {
        try
        {
            await api.ProbeAsync(lifetime.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void OnReached(bool reachable)
    {
        var wasOnline = State == BackendState.Online;
        lastChecked = DateTimeOffset.Now;
        State = reachable ? BackendState.Online : BackendState.Offline;
        Changed(nameof(Tooltip));

        if (reachable && !wasOnline)
        {
            CameOnline?.Invoke();
        }
    }
}
