namespace Frontend.Pages.Measurement;

/// <summary>One measurement column. See <see cref="MeasurementColumns"/>.</summary>
/// <param name="Binding">Stable property path on <see cref="MeasurementResult"/>; the column's identifier and table binding.</param>
/// <param name="Label">Frontend header text. Display only: changing it affects nothing else.</param>
/// <param name="CsvHeader">CSV header text. Independent of <paramref name="Label"/>.</param>
/// <param name="IncludeInCsv">Whether CSV export writes the column.</param>
/// <param name="Value">Reads the stored value for CSV export.</param>
/// <param name="DisplayFormat">Optional table string format. Never applied to CSV or storage.</param>
public sealed record MeasurementColumn(
    string Binding,
    string Label,
    string CsvHeader,
    bool IncludeInCsv,
    Func<MeasurementResult, object?> Value,
    string? DisplayFormat = null);

/// <summary>
/// What each measurement column is: identifier, labels, CSV inclusion and value. List order is the column order for both
/// the table and the CSV. Table visibility and widths are in MeasurementColumnLayout.cs; fonts and other sizes in
/// MeasurementUiConstants.xaml; SQLite column names in MeasurementRepository.cs. Hiding a column in the table never
/// removes it from storage or CSV.
/// </summary>
public static class MeasurementColumns
{
    private const string Reading = nameof(MeasurementResult.Reading) + ".";

    public static readonly MeasurementColumn Number =
        new(nameof(MeasurementResult.Number), "No", "No", true, r => r.Number);
    public static readonly MeasurementColumn CaptureNumber =
        new(nameof(MeasurementResult.CaptureNumber), "Capture No", "Capture No", true, r => r.CaptureNumber);
    public static readonly MeasurementColumn StartTime =
        new(nameof(MeasurementResult.StartTime), "Start Time", "Start Time", true, r => r.StartTime, "yyyy-MM-dd HH:mm:ss");
    public static readonly MeasurementColumn EndTime =
        new(nameof(MeasurementResult.EndTime), "End Time", "End Time", true, r => r.EndTime, "yyyy-MM-dd HH:mm:ss");
    // Saved depth interval of each capture: From and To in metres, Step in centimetres (table only). Read-only: the step
    // is changed with the Step (cm) input. Not exported, so the CSV columns stay as they were (set IncludeInCsv to true
    // to export them; the CSV value of Step stays in metres, step_m).
    public static readonly MeasurementColumn From =
        new(nameof(MeasurementResult.FromMetres), "From (m)", "from_m", false, r => r.FromMetres, "0.00#");
    public static readonly MeasurementColumn To =
        new(nameof(MeasurementResult.ToMetres), "To (m)", "to_m", false, r => r.ToMetres, "0.00#");
    public static readonly MeasurementColumn Step =
        new(nameof(MeasurementResult.StepCentimetres), "Step (cm)", "step_m", false, r => r.StepMetres, "0.#");
    public static readonly MeasurementColumn Sample =
        new(nameof(MeasurementResult.SampleCount), "Sample", "Sample", true, r => r.SampleCount);
    // The table shows DisplayTsMs (device value, or the in-memory demo value); CSV writes only the device value.
    public static readonly MeasurementColumn TsMs =
        new(nameof(MeasurementResult.DisplayTsMs), "ts_ms", "ts_ms", true, r => r.Reading.TsMs);
    public static readonly MeasurementColumn Count0 =
        new(Reading + nameof(DeviceReading.Count0), "count0", "count0", true, r => r.Reading.Count0);
    public static readonly MeasurementColumn Count0Raw =
        new(Reading + nameof(DeviceReading.Count0Raw), "count0_raw", "count0_raw", true, r => r.Reading.Count0Raw);
    public static readonly MeasurementColumn Hdm =
        new(Reading + nameof(DeviceReading.Hdm), "hdm", "hdm", true, r => r.Reading.Hdm);
    public static readonly MeasurementColumn HdmRaw =
        new(Reading + nameof(DeviceReading.HdmRaw), "hdm_raw", "hdm_raw", true, r => r.Reading.HdmRaw);
    public static readonly MeasurementColumn DoseRate =
        new(Reading + nameof(DeviceReading.DoseRate), "dose_rate", "dose_rate", true, r => r.Reading.DoseRate);
    public static readonly MeasurementColumn SessionId =
        new(nameof(MeasurementResult.SessionId), "Session", "session_id", true, r => r.SessionId);

    // SessionId stays last so exported rows remain identifiable.
    public static IReadOnlyList<MeasurementColumn> All { get; } =
        [Number, CaptureNumber, StartTime, EndTime, From, To, Step, Sample, TsMs, Count0, Count0Raw, Hdm, HdmRaw, DoseRate, SessionId];
}
