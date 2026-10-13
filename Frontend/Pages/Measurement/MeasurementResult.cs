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

    // Groups every row captured between one Start and the next Start. Not shown in the table.
    public required Guid MeasurementSessionId { get; init; }

    // Stable within a session: never reused after delete or reorder.
    public required int CaptureNumber { get; init; }

    public required DateTimeOffset StartTime { get; init; }

    public required DateTimeOffset EndTime { get; init; }

    public required int SampleCount { get; init; }

    public DeviceReading Reading { get; init; } = DeviceReading.Empty;

    // In-memory capture details. They are not part of the stored/exported column set,
    // so rows reloaded from SQLite leave them null.

    // Actual completion is independent of the intended five-second EndTime boundary.
    public DateTimeOffset? CompletedAt { get; init; }

    public double? AverageValue { get; init; }

    public string? Unit { get; init; }

    // Snapshot keeps each result associated with the core that was saved at capture start.
    public CoreSnapshot? Core { get; init; }
}
