using System.Globalization;

namespace Frontend.Calculation.CoreInformation.BluetoothDevice;

/// <summary>A Bluetooth device known to this laptop (paired or connected).</summary>
/// <param name="Connected">Windows reports the device's live ConnectionStatus as Connected. Paired alone is not
/// connected.</param>
/// <param name="DeviceId">Windows device ID (stable for a paired device); null when unknown.</param>
/// <param name="LowEnergy">Found as a Bluetooth LE device (otherwise Bluetooth Classic).</param>
public sealed record KnownBluetoothDevice(string Name, ulong Address, bool Connected, bool Paired, string? DeviceId = null, bool LowEnergy = false);

/// <summary>Reads Bluetooth devices of this laptop (Windows in the app, a fake in tests). Both methods throw when the
/// state cannot be read (no adapter, Bluetooth off, API error, timeout).</summary>
public interface IBluetoothReader
{
    /// <summary>Paired or connected devices whose name passes <paramref name="nameMatches"/>.</summary>
    IReadOnlyList<KnownBluetoothDevice> ListDevices(Func<string, bool> nameMatches);

    /// <summary>The live state of one device, or null when Windows does not know it (any more).</summary>
    KnownBluetoothDevice? ReadDevice(BluetoothDeviceOption device);
}

/// <summary>
/// A CT007-M device the user can pick for monitoring. Identified by its Bluetooth address (stable; the name is only
/// shown): two options are the same device when their addresses match, even if Windows reports another name.
/// </summary>
/// <param name="DeviceId">Windows device ID, used to open the device for each check (null: looked up by address).</param>
/// <param name="LowEnergy">Bluetooth LE device (otherwise Bluetooth Classic).</param>
public sealed record BluetoothDeviceOption(ulong Address, string Name, string? DeviceId = null, bool LowEnergy = false)
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
/// Local Bluetooth information about CT007-M detectors on THIS laptop (not the backend's computer), read from Windows
/// (<see cref="WindowsBluetooth"/>: the Windows Runtime Bluetooth API, Classic and LE, live ConnectionStatus). A device is a CT007-M when its name begins with <see cref="DeviceSettings.Ct007NamePrefix"/>
/// ("CT007-M-") — or, when <see cref="DeviceSettings.Ct007Address"/> is set, when it has that address.
/// <see cref="ListDevices"/> fills the selection dropdown; <see cref="CheckSelected"/> checks the ONE device the user
/// picked, by address. Reading only: no inquiry scan, no pairing, no connection is ever started.
/// </summary>
public sealed class Ct007BluetoothCheck(DeviceSettings settings, IBluetoothReader reader)
{
    public Ct007BluetoothCheck(DeviceSettings settings) : this(settings, new WindowsBluetooth())
    {
    }

    /// <summary>Reads from a fixed device list (tests).</summary>
    public Ct007BluetoothCheck(DeviceSettings settings, Func<IReadOnlyList<KnownBluetoothDevice>> listDevices) : this(settings, new ListReader(listDevices))
    {
    }

    /// <summary>Status shown before the user has picked a device.</summary>
    public static LocalStatus NotSelected { get; } = new(LocalState.NotSelected, "No device selected", "Select the CT007-M device to monitor.");

    /// <summary>CT007-M devices paired with or connected to this laptop, by name. Throws when Windows cannot be asked.</summary>
    public IReadOnlyList<BluetoothDeviceOption> ListDevices() =>
        reader.ListDevices(NameMayMatch).Where(IsCt007)
            .GroupBy(d => d.Address)
            .Select(g => g.OrderByDescending(d => d.Connected).ThenBy(d => d.LowEnergy).First())
            .Select(d => new BluetoothDeviceOption(d.Address, d.Name, d.DeviceId, d.LowEnergy))
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
        KnownBluetoothDevice? device;

        try
        {
            device = reader.ReadDevice(selected);
        }
        catch (Exception error)
        {
            return new LocalStatus(LocalState.Unavailable, "Status unavailable", error.Message);
        }

        // A device found under the same ID but another address is not the selected one.
        if (device is not null && device.Address != selected.Address)
        {
            device = null;
        }

        var name = string.IsNullOrWhiteSpace(device?.Name) ? selected.DisplayName : device.Name.Trim();

        if (device is { Connected: true })
        {
            return new LocalStatus(LocalState.Connected, "Connected", name);
        }

        return device switch
        {
            { Paired: true } => new LocalStatus(LocalState.Disconnected, "Disconnected", $"{name} — paired with this laptop, not connected"),
            not null => new LocalStatus(LocalState.Disconnected, "Disconnected", $"{name} — not paired, not connected"),
            null => new LocalStatus(LocalState.Disconnected, "Disconnected", $"{name} — no longer paired with or known to this laptop")
        };
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

    // Only devices that can be CT007-M are opened (with a configured address, the name does not matter).
    private bool NameMayMatch(string name) =>
        ParseAddress(settings.Ct007Address) is not null ||
        (settings.Ct007NamePrefix.Length > 0 && name.Trim().StartsWith(settings.Ct007NamePrefix, StringComparison.OrdinalIgnoreCase));

    private static ulong? ParseAddress(string text)
    {
        var hex = new string(text.Where(Uri.IsHexDigit).ToArray());
        return hex.Length == 12 ? Convert.ToUInt64(hex, 16) : null;
    }

    /// <summary>A fixed list as the device source: the selected device is looked up in it by address.</summary>
    private sealed class ListReader(Func<IReadOnlyList<KnownBluetoothDevice>> list) : IBluetoothReader
    {
        public IReadOnlyList<KnownBluetoothDevice> ListDevices(Func<string, bool> nameMatches) => list().Where(d => nameMatches(d.Name)).ToList();

        public KnownBluetoothDevice? ReadDevice(BluetoothDeviceOption device)
        {
            var matches = list().Where(d => d.Address == device.Address).ToList();
            return matches.FirstOrDefault(d => d.Connected) ?? matches.FirstOrDefault();
        }
    }
}
