using System.Diagnostics;

namespace Frontend.Pages.Measurement;

public sealed record CaptureData(
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    IReadOnlyList<double> Samples,
    string Unit,
    DateTimeOffset CompletedAt,
    DeviceReading? Reading = null);

/// <summary>
/// Device-reported values for one capture. Raw values are kept separate from processed values.
/// Every field is optional: a service that does not provide a value leaves it null (blank in table/CSV, NULL in SQLite).
/// The mock service provides none of them; a real CT007 service fills them from the device.
/// </summary>
public sealed record DeviceReading(
    long? TsMs = null,
    double? Count0 = null,
    double? Count0Raw = null,
    double? Hdm = null,
    double? HdmRaw = null,
    double? DoseRate = null)
{
    public static DeviceReading Empty { get; } = new();
}

public interface IMeasurementService
{
    /// <param name="duration">Length of this capture (the configured capture duration when it started; it never
    /// changes during the capture).</param>
    Task<CaptureData> CaptureAsync(
        DateTimeOffset start,
        TimeSpan duration,
        Action<TimeSpan> reportRemaining,
        CancellationToken cancellationToken);
}

public sealed class MockMeasurementService : IMeasurementService
{
    /// <summary>Sample rate of the simulation: one sample per second (unchanged).</summary>
    public static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(1);

    public async Task<CaptureData> CaptureAsync(
        DateTimeOffset start,
        TimeSpan duration,
        Action<TimeSpan> reportRemaining,
        CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        // One synthetic sample at each second boundary of the capture window (5 s → 5 samples, 12 s → 12 samples).
        // Scheduler delays never extend the logical capture window or change the sample count.
        var expected = Math.Max(1, (int)(duration.Ticks / SampleInterval.Ticks));
        var samples = new List<double>(expected);

        reportRemaining(duration);

        while (clock.Elapsed < duration)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var elapsed = clock.Elapsed;

            while (samples.Count < expected && elapsed >= SampleInterval * (samples.Count + 1))
            {
                samples.Add(10 + Random.Shared.NextDouble() * 5);
            }

            reportRemaining(duration > elapsed ? duration - elapsed : TimeSpan.Zero);

            var delay = duration - clock.Elapsed;

            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(
                    delay < TimeSpan.FromMilliseconds(40) ? delay : TimeSpan.FromMilliseconds(40),
                    cancellationToken);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        // A sample whose second boundary was reached but not yet taken (scheduler delay) is taken now.
        while (samples.Count < expected)
        {
            samples.Add(10 + Random.Shared.NextDouble() * 5);
        }

        reportRemaining(TimeSpan.Zero);

        return new CaptureData(start, start + duration, samples.AsReadOnly(), "cps", DateTimeOffset.Now);
    }
}
