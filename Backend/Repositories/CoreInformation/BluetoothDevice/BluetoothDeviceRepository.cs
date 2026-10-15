using Backend.Models.CoreInformation.BluetoothDevice;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;
using Windows.Foundation;
// The enclosing namespace is also called "BluetoothDevice", so the WinRT type needs an alias.
using WinBluetoothDevice = Windows.Devices.Bluetooth.BluetoothDevice;

namespace Backend.Repositories.CoreInformation.BluetoothDevice;

/// <summary>
/// Detects a CT007-M probe that is currently connected to this Windows computer.
/// Both Bluetooth Classic and Bluetooth LE devices are checked, and every match is
/// re-verified through its live ConnectionStatus, so paired-but-disconnected devices are ignored.
/// </summary>
public sealed class BluetoothDeviceRepository(ILogger<BluetoothDeviceRepository> logger)
{
    private const string DeviceNamePrefix = "CT007-M";

    public async Task<BluetoothDeviceResponse> GetConnectedDeviceAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!await IsBluetoothOnAsync(cancellationToken))
            {
                return BluetoothDeviceResponse.NotConnected;
            }

            var classicNames = await FindConnectedDeviceNamesAsync(
                WinBluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected),
                WinBluetoothDevice.FromIdAsync,
                device => device.ConnectionStatus == BluetoothConnectionStatus.Connected,
                cancellationToken);

            var lowEnergyNames = await FindConnectedDeviceNamesAsync(
                BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected),
                BluetoothLEDevice.FromIdAsync,
                device => device.ConnectionStatus == BluetoothConnectionStatus.Connected,
                cancellationToken);

            // Deterministic choice when several probes are connected: first name in case-insensitive order.
            var deviceName = classicNames
                .Concat(lowEnergyNames)
                .Order(StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            return deviceName is null
                ? BluetoothDeviceResponse.NotConnected
                : new BluetoothDeviceResponse(true, deviceName);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Missing drivers, disabled services or WinRT errors must never crash the API.
            logger.LogWarning(ex, "Bluetooth device enumeration failed.");
            return BluetoothDeviceResponse.NotConnected;
        }
    }

    private static async Task<bool> IsBluetoothOnAsync(CancellationToken cancellationToken)
    {
        var adapter = await BluetoothAdapter.GetDefaultAsync().AsTask(cancellationToken);
        if (adapter is null)
        {
            return false;
        }

        var radio = await adapter.GetRadioAsync().AsTask(cancellationToken);
        return radio?.State == RadioState.On;
    }

    private static async Task<List<string>> FindConnectedDeviceNamesAsync<TDevice>(
        string selector,
        Func<string, IAsyncOperation<TDevice>> openDevice,
        Func<TDevice, bool> isConnected,
        CancellationToken cancellationToken)
        where TDevice : class, IDisposable
    {
        var candidates = await DeviceInformation.FindAllAsync(selector).AsTask(cancellationToken);
        var names = new List<string>();

        foreach (var candidate in candidates.Where(info => HasProbeName(info.Name)))
        {
            using var device = await openDevice(candidate.Id).AsTask(cancellationToken);
            if (device is not null && isConnected(device))
            {
                names.Add(candidate.Name);
            }
        }

        return names;
    }

    private static bool HasProbeName(string? name) =>
        name is not null && name.StartsWith(DeviceNamePrefix, StringComparison.OrdinalIgnoreCase);
}
