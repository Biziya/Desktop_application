using System.ComponentModel;
using Frontend.Calculation.CoreInformation;
using Frontend.Calculation.CoreInformation.GammaGuardDatabase;
using Frontend.Shared.Infrastructure;

namespace Frontend.Pages.Monitoring;

/// <summary>
/// Monitoring page: live status and readings of every detector in the CT008 database. It reads the same database the
/// app already uses (Core Information → Device Status), strictly read-only. Polls only while the page is shown or a
/// 5-second sample is running; the read runs on the thread pool and its result is applied on the UI thread.
/// </summary>
public sealed class MonitoringViewModel : ObservableObject, INavigationAware, IDisposable
{
    /// <summary>Upper bound on rows read in one poll, so a long pause never turns into one huge read.</summary>
    private const int MaxSamplesPerPoll = 5000;

    private readonly Func<string?> databasePath;
    private readonly Func<string, long?, int, MonitorSnapshot> read;
    private readonly Func<long> clock;
    private readonly TimeSpan interval;
    private readonly DeviceStatusMonitor? devices;
    private CancellationTokenSource? polling;
    private Task? running;
    private string? connectedPath;
    private bool isShown;
    private bool disposed;
    private ConnectionState connection = ConnectionState.NoDatabase;
    private string connectionText = "No database";
    private string connectionDetail = "";
    private string warningText = "";
    private DateTimeOffset? lastPoll;

    public enum ConnectionState
    {
        NoDatabase,
        Connecting,
        Connected,
        Error
    }

    /// <summary>Reads the database the Device Status monitor found or the user selected.</summary>
    public MonitoringViewModel(DeviceStatusMonitor devices, MonitoringSettings settings, Action showCoreInformation)
        : this(() => UsablePath(devices.GammaGuardDatabase), settings, showCoreInformation)
    {
        this.devices = devices;
        devices.PropertyChanged += OnDevicesChanged;
    }

    /// <param name="read">Reads one snapshot (path, cursor, limit); tests pass a fake. Defaults to the database.</param>
    /// <param name="clock">Unix milliseconds now; tests pass a fixed clock.</param>
    public MonitoringViewModel(
        Func<string?> databasePath,
        MonitoringSettings settings,
        Action showCoreInformation,
        Func<string, long?, int, MonitorSnapshot>? read = null,
        Func<long>? clock = null)
    {
        this.databasePath = databasePath;
        this.read = read ?? ((path, after, limit) => new GammaGuardDeviceDatabase(path).ReadMonitorSnapshot(after, limit));
        this.clock = clock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        interval = TimeSpan.FromMilliseconds(settings.PollIntervalMs);
        Engine = new MonitorEngine(settings);
        ReconnectCommand = new RelayCommand(_ => Reconnect());
        SampleCommand = new RelayCommand(p => StartSample(p as DetectorRow));
        ShowCoreInformationCommand = new RelayCommand(_ => showCoreInformation());
    }

    public MonitorEngine Engine { get; }

    public RelayCommand ReconnectCommand { get; }

    public RelayCommand SampleCommand { get; }

    public RelayCommand ShowCoreInformationCommand { get; }

    public ConnectionState Connection
    {
        get => connection;
        private set
        {
            if (Set(ref connection, value))
            {
                Changed(nameof(ConnectionTone));
                Changed(nameof(HasDatabase));
            }
        }
    }

    public bool HasDatabase => Connection != ConnectionState.NoDatabase;

    public StatusTone ConnectionTone => Connection switch
    {
        ConnectionState.Connected => StatusTone.Success,
        ConnectionState.Error => StatusTone.Danger,
        ConnectionState.Connecting => StatusTone.Info,
        _ => StatusTone.Neutral
    };

    /// <summary>Short status for the chip: "Connected", "Connecting", "Read error", "No database".</summary>
    public string ConnectionText { get => connectionText; private set => Set(ref connectionText, value); }

    /// <summary>Path, schema version and last poll time; or the error message.</summary>
    public string ConnectionDetail { get => connectionDetail; private set => Set(ref connectionDetail, value); }

    /// <summary>Response-time warning for the banner; empty when there is nothing to warn about.</summary>
    public string WarningText { get => warningText; private set => Set(ref warningText, value); }

    public bool IsPolling => polling is not null;

    public void OnShown()
    {
        isShown = true;

        if (polling is null)
        {
            // Coming back to the page starts fresh from the newest data, as a reconnect does; samples are kept.
            Engine.Reset();
            connectedPath = null;
            StartPolling();
        }
    }

    public void OnHidden() => isShown = false;

    public void Reconnect()
    {
        Engine.Reset();
        connectedPath = null;
        _ = PollOnceAsync();
    }

    /// <summary>
    /// Reads the database once and applies the result. A poll already in progress is reused, so reads never overlap.
    /// Public so tests can drive polling step by step.
    /// </summary>
    public Task PollOnceAsync() => running is { IsCompleted: false } ? running : running = PollAsync();

    private async Task PollAsync()
    {
        if (disposed)
        {
            return;
        }

        if (databasePath() is not { Length: > 0 } path)
        {
            if (connectedPath is not null)
            {
                Engine.Reset();
                connectedPath = null;
            }

            Connection = ConnectionState.NoDatabase;
            ConnectionText = "No database";
            ConnectionDetail = "";
            Engine.Tick(clock());
            UpdateWarning();
            return;
        }

        if (!string.Equals(path, connectedPath, StringComparison.OrdinalIgnoreCase))
        {
            Engine.Reset();
            connectedPath = path;
            Connection = ConnectionState.Connecting;
            ConnectionText = "Connecting";
            ConnectionDetail = path;
        }

        var after = Engine.Cursor;

        try
        {
            var snapshot = await Task.Run(() => read(path, after, MaxSamplesPerPoll));

            // A reconnect or a database change while reading: this result belongs to the old state.
            if (disposed || connectedPath != path || Engine.Cursor != after)
            {
                return;
            }

            Engine.Apply(snapshot, clock());
            lastPoll = DateTimeOffset.Now;
            Connection = ConnectionState.Connected;
            ConnectionText = "Connected";
            ConnectionDetail = $"{path}  ·  schema v{snapshot.SchemaVersion}  ·  updated {lastPoll:HH:mm:ss}";
        }
        catch (Exception error)
        {
            // A locked file, a missing table or a file that moved: show it and try again on the next poll.
            Connection = ConnectionState.Error;
            ConnectionText = "Read error";
            ConnectionDetail = $"{error.Message} Retrying every {interval.TotalSeconds:0.#} s.";
            Engine.Tick(clock());
        }

        UpdateWarning();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        polling?.Cancel();
        polling = null;

        if (devices is not null)
        {
            devices.PropertyChanged -= OnDevicesChanged;
        }
    }

    private void StartSample(DetectorRow? row)
    {
        if (row is not null && Engine.StartSample(row.Serial, clock()))
        {
            StartPolling();
        }
    }

    private void StartPolling()
    {
        if (polling is not null || disposed)
        {
            return;
        }

        polling = new CancellationTokenSource();
        Changed(nameof(IsPolling));
        _ = PollLoopAsync(polling.Token);
    }

    private async Task PollLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested && (isShown || Engine.HasRunningSamples))
            {
                await PollOnceAsync();
                await Task.Delay(interval, token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (!disposed)
            {
                polling = null;
                Changed(nameof(IsPolling));
            }
        }
    }

    private void UpdateWarning()
    {
        var warnings = Engine.ResponseTimeWarnings;
        WarningText = warnings.Count == 0
            ? ""
            : $"Response time must be {DetectorRow.ExpectedResponseTime} s for 5-second samples. Change it on the detector: {string.Join(", ", warnings)}.";
    }

    private void OnDevicesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DeviceStatusMonitor.GammaGuardDatabase) && polling is not null &&
            !string.Equals(databasePath(), connectedPath, StringComparison.OrdinalIgnoreCase))
        {
            _ = PollOnceAsync();
        }
    }

    // Available or briefly busy: the file is there and readable. Anything else shows "No database".
    private static string? UsablePath(LocalStatus status) =>
        status is { State: LocalState.Available or LocalState.Busy, Path: { Length: > 0 } path } ? path : null;
}
