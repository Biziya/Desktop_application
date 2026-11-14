using Frontend.Calculation.Measurement;

namespace Frontend.Pages.Measurement;

/// <summary>What a capture delivered.</summary>
/// <param name="Samples">Plain sample values from test services; empty for device captures.</param>
/// <param name="DeviceSamples">Raw Gamma Guard samples (null for non-device services).</param>
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

/// <summary>Averaged device values of one capture; TsMs is the first sample's. Missing values stay null.</summary>
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
    /// <param name="start">The time the mouse button went down.</param>
    Task<CaptureData> CaptureAsync(
        DateTimeOffset start,
        TimeSpan duration,
        Action<TimeSpan> reportRemaining,
        CancellationToken cancellationToken);
}
