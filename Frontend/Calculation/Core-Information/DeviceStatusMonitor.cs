using Frontend.Calculation.CoreInformation.BluetoothDevice;
using Frontend.Calculation.CoreInformation.GammaGuardDatabase;
using Frontend.Calculation.CoreInformation.MeasurementDatabase;
using Frontend.Shared.Infrastructure;

namespace Frontend.Calculation.CoreInformation;

/// <summary>
/// Local Device Status checks: the user-selected CT007-M's connection (never auto-selected), the device list and
/// both databases. Checks run on the thread pool and publish on the UI context; checks of one kind never overlap,
/// and a result for a previously selected device is discarded. Informational only: never affects a capture.
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
    private Task? bluetoothListRun;
    private Task? measurementDatabaseRun;
    private Task? gammaGuardRun;
    private LocalStatus bluetooth = Ct007BluetoothCheck.NotSelected;
    private LocalStatus measurementDatabase;
    private LocalStatus gammaGuardDatabase = LocalStatus.Checking();
    private IReadOnlyList<BluetoothDeviceOption> bluetoothDevices = [];
    private string bluetoothDevicesMessage = "";
    private BluetoothDeviceOption? selectedBluetoothDevice;
    private int selection;
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

    public LocalStatus Bluetooth
    {
        get => bluetooth;
        private set => Set(ref bluetooth, value);
    }

    public IReadOnlyList<BluetoothDeviceOption> BluetoothDevices
    {
        get => bluetoothDevices;
        private set => Set(ref bluetoothDevices, value);
    }

    public string BluetoothDevicesMessage
    {
        get => bluetoothDevicesMessage;
        private set => Set(ref bluetoothDevicesMessage, value);
    }

    // Null is ignored, so a list refresh can never clear or switch the user's selection.
    public BluetoothDeviceOption? SelectedBluetoothDevice
    {
        get => selectedBluetoothDevice;
        set => SelectBluetoothDevice(value);
    }

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

    public void Start()
    {
        if (!started && !disposed)
        {
            started = true;
            _ = RunAsync(CheckBluetoothAsync, bluetoothInterval);
            _ = RunAsync(() => Task.WhenAll(RefreshBluetoothDevicesAsync(), CheckMeasurementDatabaseAsync(), CheckGammaGuardDatabaseAsync()), interval);
        }
    }

    // Monitoring only: no pairing or connection is started.
    public void SelectBluetoothDevice(BluetoothDeviceOption? device)
    {
        if (disposed || device is null || device.Equals(selectedBluetoothDevice))
        {
            return;
        }

        selection++;
        selectedBluetoothDevice = device;
        Changed(nameof(SelectedBluetoothDevice));
        Bluetooth = new LocalStatus(LocalState.Checking, "Checking…", device.DisplayName);
        _ = CheckBluetoothAsync();
    }

    public Task CheckBluetoothAsync()
    {
        if (disposed || selectedBluetoothDevice is not { } device)
        {
            return Task.CompletedTask;
        }

        // One check at a time. If it is for a previous selection, it starts a check of the new device when it ends.
        return bluetoothRun is { IsCompleted: false } ? bluetoothRun : bluetoothRun = CheckSelectedAsync(device, selection);
    }

    public Task RefreshBluetoothDevicesAsync() =>
        Run(ref bluetoothListRun, () => (Devices: bluetoothCheck.ListDevices(), Error: (string?)null), PublishDevices,
            error => (Devices: (IReadOnlyList<BluetoothDeviceOption>)[], Error: (string?)error.Message));

    public Task CheckMeasurementDatabaseAsync() =>
        Run(ref measurementDatabaseRun, measurementDatabaseCheck.Check, status => MeasurementDatabase = status, Unavailable);

    /// <param name="rediscover">Search for the database again instead of reusing the path found earlier (Retry).</param>
    public Task CheckGammaGuardDatabaseAsync(bool rediscover = false) =>
        Run(ref gammaGuardRun, () => gammaGuardCheck.Check(rediscover), status => GammaGuardDatabase = status, Unavailable);

    /// <summary>
    /// Switches to the user's Gamma Guard database if valid; otherwise the returned status says why. Queued behind
    /// any running Gamma Guard check so an older result never replaces the new selection.
    /// </summary>
    public Task<LocalStatus> SelectGammaGuardDatabaseAsync(string path)
    {
        var selecting = SelectAfterAsync(gammaGuardRun ?? Task.CompletedTask);
        gammaGuardRun = selecting;
        return selecting;

        async Task<LocalStatus> SelectAfterAsync(Task before)
        {
            await before;
            LocalStatus status;

            try
            {
                status = await Task.Run(() => gammaGuardCheck.Select(path));
            }
            catch (Exception error)
            {
                status = new LocalStatus(LocalState.Unavailable, "Not saved", $"The selection could not be saved: {error.Message}", path);
            }

            if (!disposed && status.State == LocalState.Available)
            {
                GammaGuardDatabase = status;
            }

            return status;
        }
    }

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

    private async Task CheckSelectedAsync(BluetoothDeviceOption device, int forSelection)
    {
        LocalStatus status;

        try
        {
            status = await Task.Run(() => bluetoothCheck.CheckSelected(device));
        }
        catch (Exception error)
        {
            status = Unavailable(error);
        }

        if (disposed)
        {
            return;
        }

        if (forSelection == selection)
        {
            Bluetooth = status;
            return;
        }

        // The selection changed while this check ran: drop its result and check the device selected now.
        bluetoothRun = null;
        _ = CheckBluetoothAsync();
    }

    private void PublishDevices((IReadOnlyList<BluetoothDeviceOption> Devices, string? Error) result)
    {
        if (result.Error is { } error)
        {
            // Keep the last good list (and the selection); only say that it could not be refreshed.
            BluetoothDevicesMessage = $"Bluetooth devices could not be read: {error}";
            return;
        }

        var list = result.Devices.ToList();

        // The selected device stays in the list even if Windows no longer knows it, so the selection is never lost.
        if (selectedBluetoothDevice is { } selected && !list.Contains(selected))
        {
            list.Add(selected);
        }

        if (!list.SequenceEqual(BluetoothDevices) || list.Zip(BluetoothDevices).Any(p => p.First.Name != p.Second.Name))
        {
            BluetoothDevices = list;
        }

        BluetoothDevicesMessage = result.Devices.Count == 0 ? "No CT007-M device is paired with this laptop." : "";
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

        return running is { IsCompleted: false } ? running : running = RunCheckAsync(check, publish, failed);
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
