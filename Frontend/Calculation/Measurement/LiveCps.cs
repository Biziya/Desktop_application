namespace Frontend.Calculation.Measurement;

/// <summary>Newest CPS reading of the recording detector, for the Live value above the Measurement graph.</summary>
public interface ILiveCpsReader
{
    /// <summary>The newest CPS value, or null when no recent reading is available. Never throws.</summary>
    double? ReadLatestCps();
}

/// <summary>
/// Reads the newest sample of the detector a capture would use (same resolver), as counts per second. A sample older
/// than <see cref="StaleAfter"/> is not "live" and gives null. Read-only; nothing is buffered or stored.
/// </summary>
public sealed class GammaGuardLiveCps(ICaptureDeviceResolver resolver, ICaptureClock? clock = null) : ILiveCpsReader
{
    /// <summary>Recent rows inspected for the newest reading.</summary>
    public const int RecentRows = 50;

    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(5);

    private readonly ICaptureClock clock = clock ?? SystemCaptureClock.Instance;

    public double? ReadLatestCps()
    {
        try
        {
            var device = resolver.Resolve();

            if (device.Samples.MaxSampleId() is not { } maxId)
            {
                return null;
            }

            var latest = device.Samples.ReadSamplesAfter(device.Identity.Serial, Math.Max(0, maxId - RecentRows))
                .Where(s => s.TsMs is not null && s.Count0 is { } count && double.IsFinite(count))
                .OrderBy(s => s.TsMs)
                .ThenBy(s => s.SourceId)
                .LastOrDefault();

            if (latest is null || clock.NowMs - latest.TsMs!.Value > (long)StaleAfter.TotalMilliseconds)
            {
                return null;
            }

            return SampleAveraging.PerSecond(latest.Count0);
        }
        catch (Exception)
        {
            // No database, no detector, two detectors, a locked file …: the box shows "—" until the next read.
            return null;
        }
    }
}
