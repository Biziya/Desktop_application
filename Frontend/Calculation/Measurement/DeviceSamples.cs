namespace Frontend.Calculation.Measurement;

/// <summary>
/// One Gamma Guard <c>samples</c> row as is. <see cref="DeviceSessionId"/> is the device's recording session, unrelated
/// to this app's measurement session. <see cref="TsMs"/> is Unix ms (UTC); Count0/Count0Raw are shocks per minute.
/// </summary>
public sealed record DeviceSample(
    long SourceId,
    string Serial,
    string? DeviceSessionId,
    long? Seq,
    long? TsMs,
    double? Count0,
    double? Count0Raw,
    double? Hdm,
    double? HdmRaw,
    double? DoseRate,
    double? Temperature,
    double? Pressure);

/// <summary>The detector a capture reads from (Gamma Guard <c>detector_status</c>), frozen when the capture starts.</summary>
public sealed record DetectorIdentity(string Serial, string? Name, string? Mac, string? Firmware, string? DeviceType);

/// <summary>
/// Per-field means over valid (non-null, finite) values only; a missing value never counts as zero. Count0/Count0Raw
/// are shocks per second. <see cref="DeviceSessionId"/> is null unless all samples share one.
/// </summary>
public sealed record CaptureSummary(
    int SampleCount,
    long? FirstTsMs,
    double? Count0,
    double? Count0Raw,
    double? Hdm,
    double? HdmRaw,
    double? DoseRate,
    double? Temperature,
    double? Pressure,
    string? DeviceSessionId);

public static class SampleAveraging
{
    private const double SecondsPerMinute = 60.0;

    public static CaptureSummary Summarize(IReadOnlyList<DeviceSample> samples)
    {
        double? Mean(Func<DeviceSample, double?> field)
        {
            var values = samples.Select(field).Where(v => v is { } x && double.IsFinite(x)).Select(v => v!.Value).ToList();
            return values.Count == 0 ? null : values.Sum() / values.Count;
        }

        var sessions = samples.Select(s => s.DeviceSessionId).Distinct().ToList();

        return new CaptureSummary(
            samples.Count,
            samples.Where(s => s.TsMs is not null).OrderBy(s => s.TsMs).ThenBy(s => s.SourceId).FirstOrDefault()?.TsMs,
            Mean(s => s.Count0 / SecondsPerMinute),        // shocks/min -> shocks/s
            Mean(s => s.Count0Raw / SecondsPerMinute),     // shocks/min -> shocks/s
            Mean(s => s.Hdm),
            Mean(s => s.HdmRaw),
            Mean(s => s.DoseRate),
            Mean(s => s.Temperature),
            Mean(s => s.Pressure),
            sessions.Count == 1 ? sessions[0] : null);
    }
}

/// <summary>
/// Samples of one detector in <c>trigger &lt; ts_ms &lt;= trigger + duration</c>, each source row once. A duplicate
/// write (same device session, seq and timestamp) is ignored.
/// </summary>
public sealed class CaptureWindow(string serial, long triggerMs, long durationMs)
{
    private readonly HashSet<long> seenIds = [];
    private readonly HashSet<(string?, long, long)> seenReadings = [];
    private readonly List<DeviceSample> accepted = [];

    public long TriggerMs { get; } = triggerMs;

    public long EndMs { get; } = triggerMs + durationMs;

    // The device records one sample per second.
    public int ExpectedSamples { get; } = (int)Math.Max(1, durationMs / 1000);

    public IReadOnlyList<DeviceSample> Accepted => accepted.OrderBy(s => s.TsMs).ThenBy(s => s.SourceId).ToList();

    public int Count => accepted.Count;

    public bool Contains(long tsMs) => tsMs > TriggerMs && tsMs <= EndMs;

    /// <summary>Adds the eligible rows; returns how many were new.</summary>
    public int Add(IEnumerable<DeviceSample> rows)
    {
        var added = 0;

        foreach (var row in rows)
        {
            if (!string.Equals(row.Serial, serial, StringComparison.Ordinal) || row.TsMs is not { } ts || !Contains(ts) ||
                !seenIds.Add(row.SourceId))
            {
                continue;
            }

            if (row.Seq is { } seq && !seenReadings.Add((row.DeviceSessionId, seq, ts)))
            {
                continue;
            }

            accepted.Add(row);
            added++;
        }

        return added;
    }
}

/// <summary>
/// Checks that <c>samples.ts_ms</c> is Unix ms comparable with the click time: seconds, a power-on counter or a far-off
/// clock are refused rather than silently mismatched.
/// </summary>
public static class DeviceTime
{
    public static readonly TimeSpan MaxClockDifference = TimeSpan.FromHours(12);

    /// <summary>Null when ts_ms is usable; otherwise why not.</summary>
    public static string? Problem(long latestTsMs, long nowMs)
    {
        if (latestTsMs < 100_000_000_000) // before 1973 in ms — seconds, or a counter since power-on
        {
            return $"Gamma Guard ts_ms ({latestTsMs}) is not in Unix milliseconds, so samples cannot be matched to the click time.";
        }

        if (Math.Abs(nowMs - latestTsMs) > (long)MaxClockDifference.TotalMilliseconds)
        {
            return $"Gamma Guard ts_ms is {TimeSpan.FromMilliseconds(Math.Abs(nowMs - latestTsMs)).TotalHours:0.#} h away from this laptop's clock. " +
                   "Check the clocks before measuring.";
        }

        return null;
    }
}