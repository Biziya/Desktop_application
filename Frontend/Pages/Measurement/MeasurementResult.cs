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

    // Stable within a session: never reused after delete or reorder.
    public required int CaptureNumber { get; init; }

    public required DateTimeOffset StartTime { get; init; }

    public required DateTimeOffset EndTime { get; init; }

    public required int SampleCount { get; init; }

    public DeviceReading Reading { get; init; } = DeviceReading.Empty;

    // DEMO / PREVIEW ONLY: a random ts_ms shown while the device does not report one (see
    // MeasurementViewModel.ShowDemoTsMs). Generated once per row and kept in memory only: never saved to SQLite and
    // never exported — storage and CSV read Reading.TsMs.
    public long? DemoTsMs { get; set; }

    // ts_ms shown in the table and chart: the device value, or the demo value when there is none.
    public long? DisplayTsMs => Reading.TsMs ?? DemoTsMs;

    public bool IsDemoTsMs => Reading.TsMs is null && DemoTsMs is not null;

    // Depth interval of the capture in millimetres (see Depth.cs): To = From + Step, fixed when the capture began.
    // Null for rows stored before depths were recorded. Reordering never changes them.
    public long? FromMm { get; init; }

    public long? ToMm { get; init; }

    public long? StepMm { get; init; }

    // The same depths in metres, for display (table columns From (m), To (m), Step (m)). Exact: mm / 1000.
    public decimal? FromMetres => FromMm / 1000m;

    public decimal? ToMetres => ToMm / 1000m;

    public decimal? StepMetres => StepMm / 1000m;

    // In-memory capture details. They are not part of the stored/exported column set,
    // so rows reloaded from SQLite leave them null.

    // Actual completion is independent of the intended five-second EndTime boundary.
    public DateTimeOffset? CompletedAt { get; init; }

    public double? AverageValue { get; init; }

    public string? Unit { get; init; }

    // Snapshot keeps each result associated with the core that was saved at capture start.
    public CoreSnapshot? Core { get; init; }
}
