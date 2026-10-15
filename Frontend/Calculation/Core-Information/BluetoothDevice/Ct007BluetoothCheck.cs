namespace Frontend.Calculation.CoreInformation.BluetoothDevice;

/// <summary>A Bluetooth device known to this laptop (paired/remembered or connected).</summary>
/// <param name="Connected">An active connection exists right now (for the CT-007's serial port profile: a program
/// has its Bluetooth COM port open). Paired alone is not connected.</param>
public sealed record KnownBluetoothDevice(string Name, ulong Address, bool Connected, bool Paired);

/// <summary>
/// Bluetooth status of the SPP CT-007 as seen by THIS laptop (not the backend's computer). The device is identified
/// by its configured address, otherwise by its name (see <see cref="DeviceSettings"/>). Connected only when Windows
/// reports an active connection; Disconnected only when the check succeeded and no CT-007 is connected; Status
/// unavailable when the check itself failed (no adapter, Bluetooth off, API error).
/// </summary>
public sealed class Ct007BluetoothCheck(DeviceSettings settings, Func<IReadOnlyList<KnownBluetoothDevice>> listDevices)
{
    public Ct007BluetoothCheck(DeviceSettings settings) : this(settings, WindowsBluetooth.ListKnownDevices)
    {
    }

    public LocalStatus Check()
    {
        IReadOnlyList<KnownBluetoothDevice> devices;

        try
        {
            devices = listDevices();
        }
        catch (Exception error)
        {
            return new LocalStatus(LocalState.Unavailable, "Status unavailable", error.Message);
        }

        var matches = devices.Where(IsCt007).ToList();

        if (matches.FirstOrDefault(d => d.Connected) is { } connected)
        {
            return new LocalStatus(LocalState.Connected, "Connected", Display(connected));
        }

        return matches.FirstOrDefault() is { } paired
            ? new LocalStatus(LocalState.Disconnected, "Disconnected", $"{Display(paired)} — paired with this laptop, not connected")
            : new LocalStatus(LocalState.Disconnected, "Disconnected", "No CT-007 is paired with or connected to this laptop");
    }

    /// <summary>The CT-007 identification rule: configured address, otherwise a configured name fragment.</summary>
    public bool IsCt007(KnownBluetoothDevice device)
    {
        if (ParseAddress(settings.Ct007Address) is { } address)
        {
            return device.Address == address;
        }

        var name = Normalize(device.Name);
        return name.Length > 0 && settings.Ct007Names.Any(n => Normalize(n) is { Length: > 0 } fragment && name.Contains(fragment, StringComparison.Ordinal));
    }

    private static string Display(KnownBluetoothDevice device) =>
        string.IsNullOrWhiteSpace(device.Name) ? "Name unavailable" : device.Name.Trim();

    // "CT-007", "ct 007" and "CT007" are the same name.
    private static string Normalize(string text) =>
        new(text.Where(c => !char.IsWhiteSpace(c) && c is not '-' and not '_').Select(char.ToUpperInvariant).ToArray());

    private static ulong? ParseAddress(string text)
    {
        var hex = new string(text.Where(Uri.IsHexDigit).ToArray());
        return hex.Length == 12 ? Convert.ToUInt64(hex, 16) : null;
    }
}
