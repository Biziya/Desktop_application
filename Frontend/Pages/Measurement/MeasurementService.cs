using Frontend.Calculation.Measurement;

namespace Frontend.Pages.Measurement;

/// <summary>What a capture delivered.</summary>
/// <param name="Samples">Plain sample values (used by test services; the device capture leaves it empty and fills
/// <paramref name="DeviceSamples"/>).</param>
/// <param name="DeviceSamples">Raw Gamma Guard samples accepted for the capture window (null for non-device services).</param>
/// <param name="Device">Detector the samples came from, frozen at the click.</param>
/// <param name="ExpectedSamples">Samples the window should contain (one per second of the duration).</param>
public sealed record CaptureData(
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    IReadOnlyList<double> Samples,
    string Unit,
    DateTimeOffset CompletedAt,
    DeviceReading? Reading = null,
    IReadOnlyList<DeviceSample>? DeviceSamples = null,
    DetectorIdentity? Device = null,
    int? ExpectedSamples = null);

/// <summary>
/// Device values of one capture (the averages of its accepted samples; TsMs is the first accepted sample's ts_ms).
/// Every field is optional: a value the device did not provide stays null (blank in table/CSV, NULL in SQLite).
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

/// <summary>A capture could not produce a result; the message is shown as an error toast.</summary>
public sealed class CaptureException(string message) : Exception(message);

public interface IMeasurementService
{
    /// <param name="start">The click (trigger) time, recorded when the mouse button went down.</param>
    /// <param name="duration">Length of this capture (the configured capture duration when it started; it never
    /// changes during the capture).</param>
    Task<CaptureData> CaptureAsync(
        DateTimeOffset start,
        TimeSpan duration,
        Action<TimeSpan> reportRemaining,
        CancellationToken cancellationToken);
}
