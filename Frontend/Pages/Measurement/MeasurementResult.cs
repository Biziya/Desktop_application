using CoreInformationData = Frontend.Shared.Models.CoreInformation;
using Frontend.Shared.Infrastructure;
namespace Frontend.Pages.Measurement;
public sealed class MeasurementResult : ObservableObject
{
    private int number;
    public int Number { get => number; set => Set(ref number, value); }
    public required int CaptureNumber { get; init; }
    public required DateTimeOffset StartTime { get; init; }
    public required DateTimeOffset EndTime { get; init; }
    public required int SampleCount { get; init; }
    public required double AverageValue { get; init; }
    public required string Unit { get; init; }
    // Snapshot keeps each result associated with the core that was saved at capture start.
    public required CoreInformationData Core { get; init; }
}
