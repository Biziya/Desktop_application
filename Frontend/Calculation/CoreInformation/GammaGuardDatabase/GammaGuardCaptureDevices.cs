using Frontend.Calculation.Measurement;
using Frontend.Pages.Measurement;

namespace Frontend.Calculation.CoreInformation.GammaGuardDatabase;

/// <summary>
/// Picks the capture's detector from the Gamma Guard database alone: the serial with the newest samples. Refuses when
/// the database is unavailable, empty, or two detectors are recording at once.
/// Bluetooth status is deliberately not consulted (informational only); a future connection requirement belongs
/// here as a separate rule, not in the monitor.
/// </summary>
public sealed class GammaGuardCaptureDevices(DeviceStatusMonitor devices) : ICaptureDeviceResolver
{
    // Newest sample rows inspected to find the recording detector.
    public const int RecentRows = 200;

    // Two serials whose newest samples are this close are both recording: ambiguous.
    public static readonly TimeSpan SimultaneousWindow = TimeSpan.FromSeconds(5);

    public CaptureDevice Resolve()
    {
        if (devices.GammaGuardDatabase is not { State: LocalState.Available, Path: { Length: > 0 } path })
        {
            throw new CaptureException($"The Gamma Guard database is not available ({devices.GammaGuardDatabase.Text}).");
        }

        var source = new GammaGuardDeviceDatabase(path);
        var serial = Pick(source.RecentSerials(RecentRows));
        var identity = source.FindDetector(serial)?.Identity ?? new DetectorIdentity(serial, null, null, null, null);
        return new CaptureDevice(identity, source);
    }

    public static string Pick(IReadOnlyList<RecentSerial> recent)
    {
        if (recent.Count == 0)
        {
            throw new CaptureException("The Gamma Guard database has no samples yet. Check that Gamma Guard is recording the detector.");
        }

        var ordered = recent.OrderByDescending(r => r.LastTsMs).ToList();
        var newest = ordered[0];
        var simultaneous = ordered.Skip(1).Count(r => newest.LastTsMs - r.LastTsMs <= (long)SimultaneousWindow.TotalMilliseconds);

        if (simultaneous > 0)
        {
            throw new CaptureException(
                $"More than one detector is recording ({string.Join(", ", ordered.Take(simultaneous + 1).Select(r => r.Serial))}). Stop the others to measure.");
        }

        return newest.Serial;
    }
}
