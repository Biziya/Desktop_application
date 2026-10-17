using Frontend.Calculation.CoreInformation.BluetoothDevice;
using Frontend.Calculation.CoreInformation.GammaGuardDatabase;
using Frontend.Calculation.CoreInformation.MeasurementDatabase;
using Frontend.Shared.Infrastructure;

namespace Frontend.Calculation.CoreInformation;

/// <summary>
/// Runs the local Device Status checks on this laptop. Bluetooth: the user picks one CT007-M device
/// (<see cref="SelectedBluetoothDevice"/>, by Bluetooth address); only that device's connection is checked, every
/// <see cref="DeviceSettings.BluetoothCheckInterval"/> (1 s). Nothing is checked before a selection, and no device is
/// ever selected or switched automatically. The dropdown list and the two databases are refreshed every
/// <see cref="DeviceSettings.CheckInterval"/> (and the list when the dropdown opens; the databases on Retry).
/// Checks run on the thread pool (Bluetooth enumeration and file access never block the UI); results are published on
/// the caller's (UI) context. A check never overlaps another of its kind. A result for a previously selected device
/// is discarded. One instance lives as long as the main window (navigation never creates another), so there is one
/// polling loop of each kind; everything stops on <see cref="Dispose"/>.
/// Bluetooth monitoring is informational only: nothing here enables, blocks or changes a measurement capture.
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

    /// <summary>Connection of the selected CT007-M device ("No device selected" before a selection).</summary>
    public LocalStatus Bluetooth
    {
        get => bluetooth;
        private set => Set(ref bluetooth, value);
    }

    /// <summary>CT007-M devices paired with or connected to this laptop (dropdown items).</summary>
    public IReadOnlyList<BluetoothDeviceOption> BluetoothDevices
    {
        get => bluetoothDevices;
        private set => Set(ref bluetoothDevices, value);
    }

    /// <summary>Note under the dropdown: no CT007-M paired, or the list could not be read ("" otherwise).</summary>
    public string BluetoothDevicesMessage
    {
        get => bluetoothDevicesMessage;
        private set => Set(ref bluetoothDevicesMessage, value);
    }

    /// <summary>
    /// The device the user chose to monitor. Set only by the user (the dropdown); null is ignored, so a list refresh
    /// can never clear or switch the selection.
    /// </summary>
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

    /// <summary>Starts the periodic checks (once).</summary>
    public void Start()
    {
        if (!started && !disposed)
        {
            started = true;
            _ = RunAsync(CheckBluetoothAsync, bluetoothInterval);
            _ = RunAsync(() => Task.WhenAll(RefreshBluetoothDevicesAsync(), CheckMeasurementDatabaseAsync(), CheckGammaGuardDatabaseAsync()), interval);
        }
    }

    /// <summary>
    /// Starts monitoring <paramref name="device"/> instead of the previous one: the status shows Checking at once and a
    /// check of the new device follows; a result still arriving for the previous device is discarded. Monitoring only:
    /// no pairing or connection is started.
    /// </summary>
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

    /// <summary>Checks the selected device's connection (nothing to do before a selection).</summary>
    public Task CheckBluetoothAsync()
    {
        if (disposed || selectedBluetoothDevice is not { } device)
        {
            return Task.CompletedTask;
        }

        // One check at a time. If it is for a previous selection, it starts a check of the new device when it ends.
        if (bluetoothRun is { IsCompleted: false })
        {
            return bluetoothRun;
        }

        return bluetoothRun = CheckSelectedAsync(device, selection);
    }

    /// <summary>Reads the CT007-M devices known to this laptop for the dropdown.</summary>
    public Task RefreshBluetoothDevicesAsync() =>
        Run(ref bluetoothListRun, () => (Devices: bluetoothCheck.ListDevices(), Error: (string?)null), PublishDevices,
            error => (Devices: (IReadOnlyList<BluetoothDeviceOption>)[], Error: (string?)error.Message));

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
