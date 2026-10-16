using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;
using Windows.Foundation;
// The enclosing namespace is called "BluetoothDevice", so the WinRT type needs an alias.
using WinBluetoothDevice = Windows.Devices.Bluetooth.BluetoothDevice;

namespace Frontend.Calculation.CoreInformation.BluetoothDevice;

/// <summary>
/// Reads Bluetooth devices of THIS laptop with the Windows Runtime Bluetooth API — the detection logic of the former
/// backend's BluetoothDeviceRepository, which worked on the measurement laptop: Bluetooth Classic and Bluetooth LE are
/// both enumerated, and every device's state is re-read from its live <c>ConnectionStatus</c>, so a paired but
/// disconnected device is never reported as connected. Differences from the backend: paired devices are listed too
/// (for the selection dropdown, marked not connected), a device is identified by its Bluetooth address and Windows
/// device ID, and a failure is thrown (shown as "Status unavailable") instead of being reported as not connected.
/// Reading only: no inquiry scan, no pairing, no connection. Every Windows call is bounded by <see cref="CallTimeout"/>.
/// </summary>
internal sealed class WindowsBluetooth : IBluetoothReader
{
    /// <summary>A Windows Bluetooth call that takes longer than this fails the check (it never hangs the monitor).</summary>
    public static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(3);

    public IReadOnlyList<KnownBluetoothDevice> ListDevices(Func<string, bool> nameMatches)
    {
        EnsureRadioOn();
        var candidates = new Dictionary<string, (DeviceInformation Info, bool LowEnergy)>(StringComparer.OrdinalIgnoreCase);
        var selectors = new[]
        {
            (WinBluetoothDevice.GetDeviceSelectorFromPairingState(true), false),
            (WinBluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected), false),
            (BluetoothLEDevice.GetDeviceSelectorFromPairingState(true), true),
            (BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected), true)
        };

        foreach (var (selector, lowEnergy) in selectors)
        {
            foreach (var info in Wait(DeviceInformation.FindAllAsync(selector)))
            {
                if (nameMatches(info.Name ?? ""))
                {
                    candidates.TryAdd(info.Id, (info, lowEnergy));
                }
            }
        }

        var devices = new List<KnownBluetoothDevice>();

        foreach (var (id, (info, lowEnergy)) in candidates)
        {
            if (Open(id, lowEnergy) is { } device)
            {
                devices.Add(device with { Name = string.IsNullOrWhiteSpace(info.Name) ? device.Name : info.Name, Paired = device.Paired || info.Pairing.IsPaired });
            }
        }

        return devices;
    }

    public KnownBluetoothDevice? ReadDevice(BluetoothDeviceOption selected)
    {
        EnsureRadioOn();

        if (selected.DeviceId is { Length: > 0 } id && Open(id, selected.LowEnergy) is { } byId)
        {
            return byId;
        }

        // No Windows ID (or it is gone): look the address up, as Bluetooth Classic first (the CT-007's serial port).
        using (var classic = Wait(WinBluetoothDevice.FromBluetoothAddressAsync(selected.Address)))
        {
            if (classic is not null)
            {
                return Read(classic);
            }
        }

        using var lowEnergyDevice = Wait(BluetoothLEDevice.FromBluetoothAddressAsync(selected.Address));
        return lowEnergyDevice is null ? null : Read(lowEnergyDevice);
    }

    private static KnownBluetoothDevice? Open(string id, bool lowEnergy)
    {
        if (lowEnergy)
        {
            using var device = Wait(BluetoothLEDevice.FromIdAsync(id));
            return device is null ? null : Read(device);
        }

        using (var device = Wait(WinBluetoothDevice.FromIdAsync(id)))
        {
            return device is null ? null : Read(device);
        }
    }

    private static KnownBluetoothDevice Read(WinBluetoothDevice device) =>
        new(device.Name ?? "", device.BluetoothAddress, device.ConnectionStatus == BluetoothConnectionStatus.Connected,
            device.DeviceInformation?.Pairing?.IsPaired == true, device.DeviceId, false);

    private static KnownBluetoothDevice Read(BluetoothLEDevice device) =>
        new(device.Name ?? "", device.BluetoothAddress, device.ConnectionStatus == BluetoothConnectionStatus.Connected,
            device.DeviceInformation?.Pairing?.IsPaired == true, device.DeviceId, true);

    // Without an adapter whose radio is on, the device state cannot be known: the check fails (Status unavailable).
    private static void EnsureRadioOn()
    {
        var adapter = Wait(BluetoothAdapter.GetDefaultAsync())
                      ?? throw new InvalidOperationException("No Bluetooth adapter is available on this laptop.");
        var radio = Wait(adapter.GetRadioAsync());

        if (radio?.State != RadioState.On)
        {
            throw new InvalidOperationException("Bluetooth is turned off on this laptop.");
        }
    }

    // Runs on the monitor's thread-pool thread (never the UI thread), so waiting here cannot deadlock.
    private static T Wait<T>(IAsyncOperation<T> operation)
    {
        using var timeout = new CancellationTokenSource(CallTimeout);

        try
        {
            return operation.AsTask(timeout.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException("Windows did not answer the Bluetooth check in time.");
        }
    }
}
