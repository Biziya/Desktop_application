using Frontend.Calculation.Measurement;
using Frontend.Pages.Measurement;

namespace Frontend.Calculation.CoreInformation.GammaGuardDatabase;

/// <summary>
/// Picks the detector for a capture at the click, from the Gamma Guard database alone: the serial that wrote the
/// newest samples. Its detector_status row (name, MAC, firmware) is supporting metadata only. The device is frozen for
/// the whole capture by <see cref="DeviceCaptureService"/>, so samples of two detectors are never mixed.
/// Bluetooth monitoring (Core Information → Device Status) is deliberately NOT consulted: it is informational only, and
/// a missing selection, a disconnected device or a failed Bluetooth check never blocks or changes a capture. A future
/// connection requirement belongs here, as a separate rule, not inside the monitor.
/// Refuses (with a message for the error toast) when the database is not available, holds no samples, or two
/// detectors are writing at the same time (their samples cannot be told apart by the click alone).
/// </summary>
public sealed class GammaGuardCaptureDevices(DeviceStatusMonitor devices) : ICaptureDeviceResolver
{
    /// <summary>How many of the newest sample rows are looked at to find the detector that is recording.</summary>
    public const int RecentRows = 200;

    /// <summary>Two serials whose newest samples are this close are both recording: ambiguous.</summary>
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

    /// <summary>The serial with the newest sample; refused when none, or when another serial recorded just as recently.</summary>
    public static string Pick(IReadOnlyList<RecentSerial> recent)
    {
        if (recent.Count == 0)
        {
            throw new CaptureException("The Gamma Guard database has no samples yet. Check that Gamma Guard is recording the detector.");
        }

        var ordered = recent.OrderByDescending(r => r.LastTsMs).ToList();
        var newest = ordered[0];
        var simultaneous = ordered.Skip(1).Where(r => newest.LastTsMs - r.LastTsMs <= (long)SimultaneousWindow.TotalMilliseconds).ToList();

        if (simultaneous.Count > 0)
        {
            throw new CaptureException(
                $"More than one detector is recording ({string.Join(", ", ordered.Take(simultaneous.Count + 1).Select(r => r.Serial))}). Stop the others to measure.");
        }

        return newest.Serial;
    }
}
