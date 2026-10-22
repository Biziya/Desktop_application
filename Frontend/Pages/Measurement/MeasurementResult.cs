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

    // Session the row belongs to (MeasurementRecords.session_id). Not shown in the table.
    public required Guid SessionId { get; init; }

    // Stable within a session: never reused after delete.
    public required int CaptureNumber { get; init; }

    // MeasurementRecords.id of a stored row (loaded from SQLite); null for a row captured in this run. Only used as
    // the tie-breaker of the display order (see Calculation/Measurement/MeasurementOrder.cs).
    public long? RecordId { get; init; }

    public required DateTimeOffset StartTime { get; init; }

    public required DateTimeOffset EndTime { get; init; }

    // Samples the result is the average of (accepted samples actually received; never the configured duration).
    public required int SampleCount { get; init; }

    // Samples the capture window should have contained (one per second of the duration); null for older rows.
    public int? ExpectedSamples { get; init; }

    // Length of the capture window in milliseconds (EndTime - StartTime); null for rows stored before it was recorded.
    public long? DurationMs { get; init; }

    // Fewer samples arrived than the window should contain: the average is over the samples received.
    public bool IsIncomplete => ExpectedSamples is { } expected && SampleCount < expected;

    // Table text of the Sample column: "5", or "3 of 5" for an incomplete capture.
    public string SampleDisplay => IsIncomplete ? $"{SampleCount} of {ExpectedSamples}" : SampleCount.ToString(System.Globalization.CultureInfo.CurrentCulture);

    // Averages of the accepted samples' temperature and pressure (null when none had a value). Stored, not in the table.
    public double? Temperature { get; init; }

    public double? Pressure { get; init; }

    // Source detector of the capture (Gamma Guard detector_status) and the DEVICE's own session id (Gamma Guard
    // samples.session_id — not this app's SessionId). Null for rows from before real device capture.
    public string? DeviceSerial { get; init; }

    public string? DeviceName { get; init; }

    public string? DeviceMac { get; init; }

    public string? DeviceSessionId { get; init; }

    // Raw samples accepted for this capture, kept in memory until they are saved with the row (CaptureSamples).
    // Null for rows loaded from the database.
    public IReadOnlyList<Frontend.Calculation.Measurement.DeviceSample>? Samples { get; init; }

    public DeviceReading Reading { get; init; } = DeviceReading.Empty;

    // DEMO / PREVIEW ONLY: a random ts_ms shown while the device does not report one (see
    // MeasurementViewModel.ShowDemoTsMs). Generated once per row and kept in memory only: never saved to SQLite and
    // never exported — storage and CSV read Reading.TsMs.
    public long? DemoTsMs { get; set; }

    // ts_ms shown in the table: the device value, or the demo value when there is none.
    public long? DisplayTsMs => Reading.TsMs ?? DemoTsMs;

    public bool IsDemoTsMs => Reading.TsMs is null && DemoTsMs is not null;

    // Depth interval of the capture in millimetres (see Depth.cs): To = From + Step, fixed when the capture began.
    // Null for rows stored before depths were recorded. Sorting never changes them.
    public long? FromMm { get; init; }

    public long? ToMm { get; init; }

    public long? StepMm { get; init; }

    // The same depths for display, converted exactly: From (m) and To (m) = mm / 1000, Step (cm) = mm / 10.
    // StepMetres is kept for CSV (step_m), so the CSV units never change.
    public decimal? FromMetres => FromMm / 1000m;

    public decimal? ToMetres => ToMm / 1000m;

    public decimal? StepMetres => StepMm / 1000m;

    public decimal? StepCentimetres => StepMm / 10m;

    // In-memory capture details. They are not part of the stored/exported column set,
    // so rows reloaded from SQLite leave them null.

    // Actual completion is independent of the intended five-second EndTime boundary.
    public DateTimeOffset? CompletedAt { get; init; }

    public double? AverageValue { get; init; }

    public string? Unit { get; init; }

    // Snapshot keeps each result associated with the core that was saved at capture start.
    public CoreSnapshot? Core { get; init; }
}
