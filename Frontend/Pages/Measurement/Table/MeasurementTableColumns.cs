namespace Frontend.Pages.Measurement.Table;

/// <summary>
/// One known measurement column: the stable identifier MeasurementTable.yaml refers to, the model property the table
/// binds to, and how CSV export writes it. Fixed in code, so editing the YAML can never change a binding, a stored
/// field or the CSV format.
/// </summary>
/// <param name="Id">Identifier used in MeasurementTable.yaml.</param>
/// <param name="Binding">Property path on <see cref="MeasurementResult"/> the table cell shows.</param>
/// <param name="CsvValue">The stored value CSV export writes (full precision, never the table format).</param>
public sealed record MeasurementColumn(string Id, string Binding, string CsvHeader, bool IncludeInCsv, Func<MeasurementResult, object?> CsvValue);

/// <summary>
/// Every measurement column, in CSV order. How the table shows them (order, header, visibility, width, format,
/// alignment) is set in MeasurementTable.yaml; hiding a column there never removes it from SQLite or CSV.
/// SQLite column names are in MeasurementRepository.cs.
/// </summary>
public static class MeasurementTableColumns
{
    private const string Reading = nameof(MeasurementResult.Reading) + ".";

    public static readonly MeasurementColumn Number = new("number", nameof(MeasurementResult.Number), "No", true, r => r.Number);
    public static readonly MeasurementColumn CaptureNumber =
        new("captureNumber", nameof(MeasurementResult.CaptureNumber), "Capture No", true, r => r.CaptureNumber);
    public static readonly MeasurementColumn StartTime = new("startTime", nameof(MeasurementResult.StartTime), "Start Time", true, r => r.StartTime);
    public static readonly MeasurementColumn EndTime = new("endTime", nameof(MeasurementResult.EndTime), "End Time", true, r => r.EndTime);

    // Saved depth interval of each capture: From and To in metres, Step in centimetres in the table. Exported in metres
    // (step_m too) so an exported file can be imported on the Upload Data page with its depths.
    public static readonly MeasurementColumn From = new("from", nameof(MeasurementResult.FromMetres), "from_m", true, r => r.FromMetres);
    public static readonly MeasurementColumn To = new("to", nameof(MeasurementResult.ToMetres), "to_m", true, r => r.ToMetres);
    public static readonly MeasurementColumn Step = new("step", nameof(MeasurementResult.StepCentimetres), "step_m", true, r => r.StepMetres);

    // The table shows "3 of 5" for an incomplete capture; CSV keeps the count.
    public static readonly MeasurementColumn Sample = new("sample", nameof(MeasurementResult.SampleDisplay), "Sample", true, r => r.SampleCount);

    // The table shows DisplayTsMs (device value, or the in-memory demo value); CSV writes only the device value.
    public static readonly MeasurementColumn TsMs = new("tsMs", nameof(MeasurementResult.DisplayTsMs), "ts_ms", true, r => r.Reading.TsMs);
    public static readonly MeasurementColumn Count0 = new("count0", Reading + nameof(DeviceReading.Count0), "count0", true, r => r.Reading.Count0);
    public static readonly MeasurementColumn Count0Raw =
        new("count0Raw", Reading + nameof(DeviceReading.Count0Raw), "count0_raw", true, r => r.Reading.Count0Raw);
    public static readonly MeasurementColumn Hdm = new("hdm", Reading + nameof(DeviceReading.Hdm), "hdm", true, r => r.Reading.Hdm);
    public static readonly MeasurementColumn HdmRaw = new("hdmRaw", Reading + nameof(DeviceReading.HdmRaw), "hdm_raw", true, r => r.Reading.HdmRaw);
    public static readonly MeasurementColumn DoseRate =
        new("doseRate", Reading + nameof(DeviceReading.DoseRate), "dose_rate", true, r => r.Reading.DoseRate);
    public static readonly MeasurementColumn SessionId = new("sessionId", nameof(MeasurementResult.SessionId), "session_id", true, r => r.SessionId);

    // SessionId stays last so exported rows remain identifiable.
    public static IReadOnlyList<MeasurementColumn> All { get; } =
        [Number, CaptureNumber, StartTime, EndTime, From, To, Step, Sample, TsMs, Count0, Count0Raw, Hdm, HdmRaw, DoseRate, SessionId];
}
