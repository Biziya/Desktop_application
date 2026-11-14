using Frontend.Shared.Infrastructure;
using Frontend.Shared.Models;

namespace Frontend.Pages.Measurement;

public sealed class MeasurementResult : ObservableObject
{
    private int number;

    // Display position only; Renumber keeps it equal to the row's place in the table.
    public int Number
    {
        get => number;
        set => Set(ref number, value);
    }

    public required Guid SessionId { get; init; }

    // Stable within a session: never reused after delete.
    public required int CaptureNumber { get; init; }

    // MeasurementRecords.id of a stored row; null for a row captured in this run. Tie-breaker for MeasurementOrder.
    public long? RecordId { get; init; }

    public required DateTimeOffset StartTime { get; init; }

    public required DateTimeOffset EndTime { get; init; }

    // Samples actually received and averaged.
    public required int SampleCount { get; init; }

    // Null for rows stored before these were recorded.
    public int? ExpectedSamples { get; init; }

    public long? DurationMs { get; init; }

    public bool IsIncomplete => ExpectedSamples is { } expected && SampleCount < expected;

    // "5", or "3 of 5" for an incomplete capture.
    public string SampleDisplay => IsIncomplete ? $"{SampleCount} of {ExpectedSamples}" : SampleCount.ToString(System.Globalization.CultureInfo.CurrentCulture);

    public double? Temperature { get; init; }

    public double? Pressure { get; init; }

    // Source detector; DeviceSessionId is the device's own session id, not this app's SessionId.
    public string? DeviceSerial { get; init; }

    public string? DeviceName { get; init; }

    public string? DeviceMac { get; init; }

    public string? DeviceSessionId { get; init; }

    // Raw samples saved with the row; null for rows loaded from the database.
    public IReadOnlyList<Frontend.Calculation.Measurement.DeviceSample>? Samples { get; init; }

    public DeviceReading Reading { get; init; } = DeviceReading.Empty;

    // Demo only (MeasurementViewModel.ShowDemoTsMs): never saved or exported.
    public long? DemoTsMs { get; set; }

    public long? DisplayTsMs => Reading.TsMs ?? DemoTsMs;

    public bool IsDemoTsMs => Reading.TsMs is null && DemoTsMs is not null;

    // Depth interval in millimetres, fixed when the capture began; null for rows stored before depths were recorded.
    public long? FromMm { get; init; }

    public long? ToMm { get; init; }

    public long? StepMm { get; init; }

    // StepMetres is kept for the CSV step_m column.
    public decimal? FromMetres => FromMm / 1000m;

    public decimal? ToMetres => ToMm / 1000m;

    public decimal? StepMetres => StepMm / 1000m;

    public decimal? StepCentimetres => StepMm / 10m;

    // In-memory only: rows reloaded from SQLite leave these null.
    public DateTimeOffset? CompletedAt { get; init; }

    public double? AverageValue { get; init; }

    public string? Unit { get; init; }

    // The core saved at capture start.
    public CoreSnapshot? Core { get; init; }
}
