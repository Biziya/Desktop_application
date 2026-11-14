using System.Globalization;

namespace Frontend.Calculation.CoreInformation.BluetoothDevice;

/// <summary>A paired or connected Bluetooth device. <c>Connected</c> is the live status; paired alone is not connected.</summary>
public sealed record KnownBluetoothDevice(string Name, ulong Address, bool Connected, bool Paired, string? DeviceId = null, bool LowEnergy = false);

/// <summary>Bluetooth device source (Windows in the app, a fake in tests). Throws when the state cannot be read.</summary>
public interface IBluetoothReader
{
    IReadOnlyList<KnownBluetoothDevice> ListDevices(Func<string, bool> nameMatches);

    /// <summary>The live state of one device, or null when Windows no longer knows it.</summary>
    KnownBluetoothDevice? ReadDevice(BluetoothDeviceOption device);
}

/// <summary>A selectable CT007-M. Equality is by Bluetooth address only; the name may change.</summary>
/// <param name="DeviceId">Windows device ID used to open the device; null means look it up by address.</param>
public sealed record BluetoothDeviceOption(ulong Address, string Name, string? DeviceId = null, bool LowEnergy = false)
{
    public string AddressText => string.Join(":", Address.ToString("X12", CultureInfo.InvariantCulture).Chunk(2).Select(c => new string(c)));

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "Name unavailable" : Name.Trim();

    // Address included so similarly named devices stay distinguishable.
    public string Display => $"{DisplayName}  ({AddressText})";

    public bool Equals(BluetoothDeviceOption? other) => other is not null && other.Address == Address;

    public override int GetHashCode() => Address.GetHashCode();

    public override string ToString() => Display;
}

/// <summary>
/// CT007-M detection on this laptop: a device matches <see cref="DeviceSettings.Ct007Address"/> when set, otherwise
/// its name starts with <see cref="DeviceSettings.Ct007NamePrefix"/>. Read-only; never pairs or connects.
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

    public static LocalStatus NotSelected { get; } = new(LocalState.NotSelected, "No device selected", "Select the CT007-M device to monitor.");

    public IReadOnlyList<BluetoothDeviceOption> ListDevices() =>
        reader.ListDevices(NameMayMatch).Where(IsCt007)
            .GroupBy(d => d.Address)
            .Select(g => g.OrderByDescending(d => d.Connected).ThenBy(d => d.LowEnergy).First())
            .Select(d => new BluetoothDeviceOption(d.Address, d.Name, d.DeviceId, d.LowEnergy))
            .OrderBy(o => o.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(o => o.Address)
            .ToList();

    // A failed check is "Status unavailable", never "Disconnected".
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
