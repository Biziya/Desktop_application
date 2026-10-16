using Frontend.Calculation.CoreInformation.BluetoothDevice;
using Frontend.Calculation.Measurement;
using Frontend.Pages.Measurement;

namespace Frontend.Calculation.CoreInformation.GammaGuardDatabase;

/// <summary>
/// Picks the detector for a capture at the click: the one CT007-M device that Windows reports as CONNECTED right now
/// (from the 1-second Bluetooth check), matched to its detector_status row (serial) in the locally discovered Gamma
/// Guard database. A stored detector_status state is supporting information only — never proof of a connection.
/// Refuses (with a message for the error toast) when no device or more than one is connected, the Bluetooth result is
/// stale, the database is not available, or the device has no detector_status row.
/// </summary>
public sealed class GammaGuardCaptureDevices(DeviceStatusMonitor devices, Func<DateTimeOffset>? now = null) : ICaptureDeviceResolver
{
    /// <summary>A Bluetooth result older than this is not trusted for a capture (checks run every second).</summary>
    public static readonly TimeSpan MaxBluetoothAge = TimeSpan.FromSeconds(3);

    private readonly Func<DateTimeOffset> now = now ?? (() => DateTimeOffset.Now);

    public CaptureDevice Resolve()
    {
        var connected = devices.ConnectedDevices;

        if (devices.BluetoothCheckedAt is not { } checkedAt || now() - checkedAt > MaxBluetoothAge)
        {
            throw new CaptureException("The CT-007 Bluetooth status is not current. Try again in a moment.");
        }

        if (connected.Count == 0)
        {
            throw new CaptureException("No CT007-M device is connected to this laptop.");
        }

        if (connected.Count > 1)
        {
            throw new CaptureException(
                $"More than one CT007-M device is connected ({string.Join(", ", connected.Select(d => d.Name))}). Disconnect the others to measure.");
        }

        if (devices.GammaGuardDatabase is not { State: LocalState.Available, Path: { Length: > 0 } path })
        {
            throw new CaptureException($"The Gamma Guard database is not available ({devices.GammaGuardDatabase.Text}).");
        }

        var source = new GammaGuardDeviceDatabase(path);
        var detector = source.FindDetector(connected[0])
                       ?? throw new CaptureException($"{connected[0].Name} is connected but has no detector_status entry in the Gamma Guard database.");

        return new CaptureDevice(detector.Identity, source);
    }
}
