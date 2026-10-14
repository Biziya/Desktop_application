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

/// <summary>Identity of the connected CT007, as reported by the connection.</summary>
public sealed record DeviceConnection(string Name, string Identifier);

public interface IMeasurementService
{
    /// <summary>The connected CT007, or null when no device is connected.</summary>
    DeviceConnection? ConnectedDevice { get; }

    /// <summary>Raised when a device connects or disconnects (may be raised on any thread).</summary>
    event EventHandler? ConnectionChanged;

    /// <summary>Length of one capture. The ViewModel derives its countdown text and progress from this.</summary>
    TimeSpan Duration { get; }

    Task<CaptureData> CaptureAsync(
        DateTimeOffset start,
        Action<TimeSpan> reportRemaining,
        CancellationToken cancellationToken);
}

public sealed class MockMeasurementService : IMeasurementService
{
    public TimeSpan Duration { get; } = TimeSpan.FromSeconds(5);

    // Simulation only: there is no hardware connection, so no device is ever reported.
    public DeviceConnection? ConnectedDevice => null;

    public event EventHandler? ConnectionChanged
    {
        add { }
        remove { }
    }

    public async Task<CaptureData> CaptureAsync(
        DateTimeOffset start,
        Action<TimeSpan> reportRemaining,
        CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var samples = new List<double>(5);

        // Five synthetic samples, one at each second boundary. Scheduler delays never
        // extend the logical capture window or change the sample count.
        reportRemaining(Duration);

        while (clock.Elapsed < Duration)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var elapsed = clock.Elapsed;

            while (samples.Count < 5 && elapsed.TotalSeconds >= samples.Count + 1)
            {
                samples.Add(10 + Random.Shared.NextDouble() * 5);
            }

            reportRemaining(Duration > elapsed ? Duration - elapsed : TimeSpan.Zero);

            var delay = Duration - clock.Elapsed;

            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(
                    delay < TimeSpan.FromMilliseconds(40) ? delay : TimeSpan.FromMilliseconds(40),
                    cancellationToken);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        while (samples.Count < 5)
        {
            samples.Add(10 + Random.Shared.NextDouble() * 5);
        }

        reportRemaining(TimeSpan.Zero);

        return new CaptureData(start, start + Duration, samples.AsReadOnly(), "cps", DateTimeOffset.Now);
    }
}
