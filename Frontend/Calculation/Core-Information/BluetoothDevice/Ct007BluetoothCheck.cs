using System.Globalization;

namespace Frontend.Calculation.CoreInformation.BluetoothDevice;

/// <summary>A Bluetooth device known to this laptop (paired/remembered or connected).</summary>
/// <param name="Connected">An active connection exists right now (for the CT-007's serial port profile: a program
/// has its Bluetooth COM port open). Paired alone is not connected.</param>
public sealed record KnownBluetoothDevice(string Name, ulong Address, bool Connected, bool Paired);

/// <summary>
/// A CT007-M device the user can pick for monitoring. Identified by its Bluetooth address (stable; the name is only
/// shown): two options are the same device when their addresses match, even if Windows reports another name.
/// </summary>
public sealed record BluetoothDeviceOption(ulong Address, string Name)
{
    public string AddressText => string.Join(":", Address.ToString("X12", CultureInfo.InvariantCulture).Chunk(2).Select(c => new string(c)));

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "Name unavailable" : Name.Trim();

    /// <summary>Dropdown text: name and address, so two devices with similar names stay distinguishable.</summary>
    public string Display => $"{DisplayName}  ({AddressText})";

    public bool Equals(BluetoothDeviceOption? other) => other is not null && other.Address == Address;

    public override int GetHashCode() => Address.GetHashCode();

    public override string ToString() => Display;
}

/// <summary>
/// Local Bluetooth information about CT007-M detectors on THIS laptop (not the backend's computer), read from Windows'
/// own device list. A device is a CT007-M when its name begins with <see cref="DeviceSettings.Ct007NamePrefix"/>
/// ("CT007-M-") — or, when <see cref="DeviceSettings.Ct007Address"/> is set, when it has that address.
/// <see cref="ListDevices"/> fills the selection dropdown; <see cref="CheckSelected"/> checks the ONE device the user
/// picked, by address. Reading only: no inquiry scan, no pairing, no connection is ever started.
/// </summary>
public sealed class Ct007BluetoothCheck(DeviceSettings settings, Func<IReadOnlyList<KnownBluetoothDevice>> listDevices)
{
    public Ct007BluetoothCheck(DeviceSettings settings) : this(settings, WindowsBluetooth.ListKnownDevices)
    {
    }

    /// <summary>Status shown before the user has picked a device.</summary>
    public static LocalStatus NotSelected { get; } = new(LocalState.NotSelected, "No device selected", "Select the CT007-M device to monitor.");

    /// <summary>CT007-M devices paired with or connected to this laptop, by name. Throws when Windows cannot be asked.</summary>
    public IReadOnlyList<BluetoothDeviceOption> ListDevices() =>
        listDevices().Where(IsCt007)
            .GroupBy(d => d.Address)
            .Select(g => new BluetoothDeviceOption(g.Key, g.First().Name))
            .OrderBy(o => o.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(o => o.Address)
            .ToList();

    /// <summary>
    /// The selected device's connection, found by address: Connected only when Windows reports an active connection;
    /// Disconnected when the check worked and it is only paired (or no longer known); Status unavailable when the
    /// check itself failed (no adapter, Bluetooth off, API error) — never Disconnected then.
    /// </summary>
    public LocalStatus CheckSelected(BluetoothDeviceOption selected)
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

        var matches = devices.Where(d => d.Address == selected.Address).ToList();
        var name = matches.Select(d => d.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))?.Trim() ?? selected.DisplayName;

        if (matches.Any(d => d.Connected))
        {
            return new LocalStatus(LocalState.Connected, "Connected", name);
        }

        return matches.Count > 0
            ? new LocalStatus(LocalState.Disconnected, "Disconnected", $"{name} — paired with this laptop, not connected")
            : new LocalStatus(LocalState.Disconnected, "Disconnected", $"{name} — no longer paired with or known to this laptop");
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

    private static ulong? ParseAddress(string text)
    {
        var hex = new string(text.Where(Uri.IsHexDigit).ToArray());
        return hex.Length == 12 ? Convert.ToUInt64(hex, 16) : null;
    }
}
