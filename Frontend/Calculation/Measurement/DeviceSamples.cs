namespace Frontend.Calculation.Measurement;

/// <summary>
/// One row of the Gamma Guard <c>samples</c> table, read as is. <see cref="SourceId"/> is that table's <c>id</c>
/// (the row's identity in the device database); <see cref="DeviceSessionId"/> is the DEVICE's <c>session_id</c>
/// (a Gamma Guard recording session) and has nothing to do with this app's measurement session.
/// <see cref="TsMs"/> is Unix epoch milliseconds (UTC) — checked by <see cref="DeviceTime"/> before each capture.
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
/// Result of one capture: arithmetic means of the accepted samples, per field, over that field's valid (non-null,
/// finite) values only — a missing value is never counted as zero, and a field without any valid value stays null.
/// Identifiers, sequence numbers and timestamps are not averaged: <see cref="FirstTsMs"/> is the ts_ms of the first
/// accepted sample (in time order) and <see cref="DeviceSessionId"/> is the device session when every accepted sample
/// has the same one (null when they differ; each raw sample keeps its own).
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
            Mean(s => s.Count0),
            Mean(s => s.Count0Raw),
            Mean(s => s.Hdm),
            Mean(s => s.HdmRaw),
            Mean(s => s.DoseRate),
            Mean(s => s.Temperature),
            Mean(s => s.Pressure),
            sessions.Count == 1 ? sessions[0] : null);
    }
}

/// <summary>
/// Which samples belong to a capture: <c>trigger &lt; ts_ms &lt;= trigger + duration</c> (epoch milliseconds), from
/// the frozen detector only, each source row once. A sample at or before the click never counts; the window never
/// grows to replace missing samples. A second row with the same device session, sequence number and timestamp as an
/// accepted one (a duplicate write) is ignored.
/// </summary>
public sealed class CaptureWindow(string serial, long triggerMs, long durationMs)
{
    private readonly HashSet<long> seenIds = [];
    private readonly HashSet<(string?, long, long)> seenReadings = [];
    private readonly List<DeviceSample> accepted = [];

    public long TriggerMs { get; } = triggerMs;

    public long EndMs { get; } = triggerMs + durationMs;

    /// <summary>Samples the device normally records in the window: one per second.</summary>
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
/// The time basis of <c>samples.ts_ms</c>: Unix epoch milliseconds (UTC), comparable with the trigger's
/// <see cref="DateTimeOffset.ToUnixTimeMilliseconds"/>. Before a capture the newest ts_ms of the device is checked
/// against this laptop's clock; a value in seconds, a device-relative counter or a clock far off is refused instead of
/// being compared with the click.
/// </summary>
public static class DeviceTime
{
    /// <summary>How far the newest sample may be from this laptop's clock.</summary>
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
