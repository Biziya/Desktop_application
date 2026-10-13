namespace Frontend.Pages.Measurement;

/// <summary>One measurement column. See <see cref="MeasurementColumns"/>.</summary>
/// <param name="Binding">Stable property path on <see cref="MeasurementResult"/>. Used for the table binding.</param>
/// <param name="Label">Frontend header text. Display only: changing it affects nothing else.</param>
/// <param name="CsvHeader">CSV header text. Independent of <paramref name="Label"/>.</param>
/// <param name="ShowInTable">Whether the frontend table shows the column.</param>
/// <param name="IncludeInCsv">Whether CSV export writes the column.</param>
/// <param name="Value">Reads the stored value for CSV export.</param>
/// <param name="DisplayFormat">Optional table string format. Never applied to CSV or storage.</param>
public sealed record MeasurementColumn(
    string Binding,
    string Label,
    string CsvHeader,
    bool ShowInTable,
    bool IncludeInCsv,
    Func<MeasurementResult, object?> Value,
    string? DisplayFormat = null);

/// <summary>
/// Measurement table and CSV column configuration. List order is the column order for both the table and the CSV.
/// Sizes, fonts and multipliers stay in MeasurementUiConstants.xaml; SQLite column names live in MeasurementRepository.cs.
/// </summary>
public static class MeasurementColumns
{
    private const string Reading = nameof(MeasurementResult.Reading) + ".";

    public static IReadOnlyList<MeasurementColumn> All { get; } =
    [
        new(nameof(MeasurementResult.Number), "No", "No", true, true, r => r.Number),
        new(nameof(MeasurementResult.CaptureNumber), "Capture No", "Capture No", true, true, r => r.CaptureNumber),
        new(nameof(MeasurementResult.StartTime), "Start Time", "Start Time", false, true, r => r.StartTime, "yyyy-MM-dd HH:mm:ss"),
        new(nameof(MeasurementResult.EndTime), "End Time", "End Time", false, true, r => r.EndTime, "yyyy-MM-dd HH:mm:ss"),
        new(nameof(MeasurementResult.SampleCount), "Sample", "Sample", true, true, r => r.SampleCount),
        new(Reading + nameof(DeviceReading.TsMs), "ts_ms", "ts_ms", true, true, r => r.Reading.TsMs),
        new(Reading + nameof(DeviceReading.Count0), "count0", "count0", true, true, r => r.Reading.Count0),
        new(Reading + nameof(DeviceReading.Count0Raw), "count0_raw", "count0_raw", true, true, r => r.Reading.Count0Raw),
        new(Reading + nameof(DeviceReading.Hdm), "hdm", "hdm", true, true, r => r.Reading.Hdm),
        new(Reading + nameof(DeviceReading.HdmRaw), "hdm_raw", "hdm_raw", true, true, r => r.Reading.HdmRaw),
        new(Reading + nameof(DeviceReading.DoseRate), "dose_rate", "dose_rate", true, true, r => r.Reading.DoseRate),
        // Always last in the CSV so exported rows stay identifiable.
        new(nameof(MeasurementResult.MeasurementSessionId), "Session", "MeasurementSessionId", false, true, r => r.MeasurementSessionId)
    ];
}
