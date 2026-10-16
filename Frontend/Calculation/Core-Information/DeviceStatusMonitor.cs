using Frontend.Calculation.CoreInformation.BluetoothDevice;
using Frontend.Calculation.CoreInformation.GammaGuardDatabase;
using Frontend.Calculation.CoreInformation.MeasurementDatabase;
using Frontend.Shared.Infrastructure;

namespace Frontend.Calculation.CoreInformation;

/// <summary>
/// Runs the three local Device Status checks on this laptop: the Bluetooth connection every
/// <see cref="DeviceSettings.BluetoothCheckInterval"/> (1 s), the two databases every
/// <see cref="DeviceSettings.CheckInterval"/>, and on Retry. Checks run on the thread pool (Bluetooth enumeration and file access never block the UI); results are
/// published on the caller's (UI) context. A check of one kind never overlaps another check of the same kind — a Retry
/// during a running check reuses it. One instance lives as long as the main window (navigation never creates another)
/// and stops on <see cref="Dispose"/>. Independent of the backend: it works offline and never changes Online/Offline.
/// </summary>
public sealed class DeviceStatusMonitor : ObservableObject, IDisposable
{
    private readonly Ct007BluetoothCheck bluetoothCheck;
    private readonly MeasurementDatabaseCheck measurementDatabaseCheck;
    private readonly GammaGuardDatabaseCheck gammaGuardCheck;
    private readonly TimeSpan interval;
    private readonly TimeSpan bluetoothInterval;
    private readonly CancellationTokenSource lifetime = new();
    private Task? bluetoothRun;
    private Task? measurementDatabaseRun;
    private Task? gammaGuardRun;
    private LocalStatus bluetooth = LocalStatus.Checking();
    private LocalStatus measurementDatabase;
    private LocalStatus gammaGuardDatabase = LocalStatus.Checking();
    private IReadOnlyList<KnownBluetoothDevice> connectedDevices = [];
    private DateTimeOffset? bluetoothCheckedAt;
    private bool started;
    private bool disposed;

    public DeviceStatusMonitor(
        Ct007BluetoothCheck bluetoothCheck,
        MeasurementDatabaseCheck measurementDatabaseCheck,
        GammaGuardDatabaseCheck gammaGuardCheck,
        TimeSpan interval,
        TimeSpan? bluetoothInterval = null)
    {
        this.bluetoothInterval = bluetoothInterval ?? interval;
        this.bluetoothCheck = bluetoothCheck;
        this.measurementDatabaseCheck = measurementDatabaseCheck;
        this.gammaGuardCheck = gammaGuardCheck;
        this.interval = interval;
        measurementDatabase = LocalStatus.Checking(measurementDatabaseCheck.DatabasePath);
    }

    /// <summary>SPP CT-007 Bluetooth status on this laptop.</summary>
    public LocalStatus Bluetooth
    {
        get => bluetooth;
        private set => Set(ref bluetooth, value);
    }

    /// <summary>CT007-M devices Windows reported as connected at the last Bluetooth check (empty when none or failed).</summary>
    public IReadOnlyList<KnownBluetoothDevice> ConnectedDevices => connectedDevices;

    /// <summary>When the last Bluetooth check finished (null before the first one).</summary>
    public DateTimeOffset? BluetoothCheckedAt => bluetoothCheckedAt;

    public LocalStatus MeasurementDatabase
    {
        get => measurementDatabase;
        private set => Set(ref measurementDatabase, value);
    }

    public LocalStatus GammaGuardDatabase
    {
        get => gammaGuardDatabase;
        private set => Set(ref gammaGuardDatabase, value);
    }

    /// <summary>Starts the periodic checks (once).</summary>
    public void Start()
    {
        if (!started && !disposed)
        {
            started = true;
            _ = RunAsync(CheckBluetoothAsync, bluetoothInterval);
            _ = RunAsync(() => Task.WhenAll(CheckMeasurementDatabaseAsync(), CheckGammaGuardDatabaseAsync()), interval);
        }
    }

    public Task CheckAllAsync() => Task.WhenAll(CheckBluetoothAsync(), CheckMeasurementDatabaseAsync(), CheckGammaGuardDatabaseAsync());

    // A failed or disconnected check publishes an empty device list at once, so no stale "connected" device remains.
    public Task CheckBluetoothAsync() =>
        Run(ref bluetoothRun, bluetoothCheck.CheckDevices, result =>
        {
            connectedDevices = result.Connected;
            bluetoothCheckedAt = DateTimeOffset.Now;
            Bluetooth = result.Status;
            Changed(nameof(ConnectedDevices));
        }, error => new BluetoothCheckResult(Unavailable(error), []));

    public Task CheckMeasurementDatabaseAsync() =>
        Run(ref measurementDatabaseRun, measurementDatabaseCheck.Check, status => MeasurementDatabase = status, Unavailable);

    /// <param name="rediscover">Search for the database again instead of reusing the path found earlier (Retry).</param>
    public Task CheckGammaGuardDatabaseAsync(bool rediscover = false) =>
        Run(ref gammaGuardRun, () => gammaGuardCheck.Check(rediscover), status => GammaGuardDatabase = status, Unavailable);

    private static LocalStatus Unavailable(Exception error) => new(LocalState.Unavailable, "Status unavailable", error.Message);

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        lifetime.Cancel();
        lifetime.Dispose();
    }

    private async Task RunAsync(Func<Task> check, TimeSpan every)
    {
        var token = lifetime.Token;

        while (!token.IsCancellationRequested)
        {
            await check();

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

    // Reuses a running check of the same kind; otherwise runs the check off the UI thread and publishes the result.
    private Task Run<T>(ref Task? running, Func<T> check, Action<T> publish, Func<Exception, T> failed)
    {
        if (disposed)
        {
            return Task.CompletedTask;
        }

        if (running is { IsCompleted: false })
        {
            return running;
        }

        return running = RunCheckAsync(check, publish, failed);
    }

    private async Task RunCheckAsync<T>(Func<T> check, Action<T> publish, Func<Exception, T> failed)
    {
        T result;

        try
        {
            result = await Task.Run(check);
        }
        catch (Exception error)
        {
            result = failed(error);
        }

        if (!disposed)
        {
            publish(result);
        }
    }
}
