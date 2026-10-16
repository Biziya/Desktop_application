namespace Frontend.Calculation.CoreInformation.BluetoothDevice;

/// <summary>A Bluetooth device known to this laptop (paired/remembered or connected).</summary>
/// <param name="Connected">An active connection exists right now (for the CT-007's serial port profile: a program
/// has its Bluetooth COM port open). Paired alone is not connected.</param>
public sealed record KnownBluetoothDevice(string Name, ulong Address, bool Connected, bool Paired);

/// <summary>Result of one Bluetooth check: the row status and the CT007-M devices connected right now.</summary>
public sealed record BluetoothCheckResult(LocalStatus Status, IReadOnlyList<KnownBluetoothDevice> Connected);

/// <summary>
/// Bluetooth status of the CT007-M detector as seen by THIS laptop (not the backend's computer), from Windows' own
/// connection information. A device is a CT007-M when its name begins with <see cref="DeviceSettings.Ct007NamePrefix"/>
/// ("CT007-M-") — or, when <see cref="DeviceSettings.Ct007Address"/> is set, when it has that address. Connected only
/// when Windows reports an active connection (paired alone is not connected); Disconnected only when the check worked
/// and none is connected; Status unavailable when the check itself failed (no adapter, Bluetooth off, API error).
/// Every check is a fresh reading, so a device that disconnects stops showing as connected at the next check.
/// </summary>
public sealed class Ct007BluetoothCheck(DeviceSettings settings, Func<IReadOnlyList<KnownBluetoothDevice>> listDevices)
{
    public Ct007BluetoothCheck(DeviceSettings settings) : this(settings, WindowsBluetooth.ListKnownDevices)
    {
    }

    public LocalStatus Check() => CheckDevices().Status;

    public BluetoothCheckResult CheckDevices()
    {
        IReadOnlyList<KnownBluetoothDevice> devices;

        try
        {
            devices = listDevices();
        }
        catch (Exception error)
        {
            return new BluetoothCheckResult(new LocalStatus(LocalState.Unavailable, "Status unavailable", error.Message), []);
        }

        var matches = devices.Where(IsCt007).ToList();
        var connected = matches.Where(d => d.Connected).ToList();

        if (connected.Count == 1)
        {
            return new BluetoothCheckResult(new LocalStatus(LocalState.Connected, "Connected", Display(connected[0])), connected);
        }

        if (connected.Count > 1)
        {
            return new BluetoothCheckResult(new LocalStatus(LocalState.Connected, "Connected",
                $"{string.Join(", ", connected.Select(Display))} — {connected.Count} devices connected; captures need exactly one"), connected);
        }

        return new BluetoothCheckResult(matches.FirstOrDefault() is { } paired
            ? new LocalStatus(LocalState.Disconnected, "Disconnected", $"{Display(paired)} — paired with this laptop, not connected")
            : new LocalStatus(LocalState.Disconnected, "Disconnected", "No CT007-M device is paired with or connected to this laptop"), []);
    }

    /// <summary>The identification rule: configured address, otherwise a name beginning with the configured prefix.</summary>
    public bool IsCt007(KnownBluetoothDevice device)
    {
        if (ParseAddress(settings.Ct007Address) is { } address)
        {
            return device.Address == address;
        }

        return settings.Ct007NamePrefix.Length > 0 &&
               device.Name.Trim().StartsWith(settings.Ct007NamePrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string Display(KnownBluetoothDevice device) =>
        string.IsNullOrWhiteSpace(device.Name) ? "Name unavailable" : device.Name.Trim();

    private static ulong? ParseAddress(string text)
    {
        var hex = new string(text.Where(Uri.IsHexDigit).ToArray());
        return hex.Length == 12 ? Convert.ToUInt64(hex, 16) : null;
    }
}
