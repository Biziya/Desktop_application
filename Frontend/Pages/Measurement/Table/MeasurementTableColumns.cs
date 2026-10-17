using System.Windows;
using System.Windows.Controls;

namespace Frontend.Pages.Measurement.Table;

/// <summary>
/// One measurement column: how the table shows it and what the CSV export writes. See <see cref="MeasurementTableColumns"/>.
/// The display settings (header, visibility, widths, format, alignment) never affect storage or CSV; the CSV settings are
/// independent of the table header.
/// </summary>
public sealed record MeasurementTableColumn
{
    /// <summary>Stable property path on <see cref="MeasurementResult"/>: the column's identifier and table binding.</summary>
    public required string Binding { get; init; }

    /// <summary>Table header text. Display only.</summary>
    public required string Header { get; init; }

    /// <summary>Shown in the table. Hiding a column never removes it from SQLite or CSV.</summary>
    public bool Visible { get; init; } = true;

    /// <summary>A star share (<see cref="MeasurementTableColumns.Star"/>), <see cref="DataGridLength.Auto"/> or pixels.</summary>
    public required DataGridLength Width { get; init; }

    public double MinWidth { get; init; }

    public double MaxWidth { get; init; } = double.PositiveInfinity;

    /// <summary>Table text of a value (null: the value as is). Never applied to CSV or storage.</summary>
    public Func<object?, string?>? Format { get; init; }

    /// <summary>Body-cell text alignment; headers stay centred.</summary>
    public TextAlignment Alignment { get; init; } = TextAlignment.Right;

    public required string CsvHeader { get; init; }

    public bool IncludeInCsv { get; init; } = true;

    /// <summary>The stored value CSV export writes (full precision, never the table format).</summary>
    public required Func<MeasurementResult, object?> CsvValue { get; init; }
}

/// <summary>
/// The one definition of the measurement columns. List order is the column order of both the table and the CSV.
/// Visible columns use star widths (shares of the table width, in the proportions of the earlier compact layout), so
/// together they always fit the table: no horizontal scrolling with the sidebar expanded or collapsed. The minimum
/// widths add up to 576 px (+ 64 px Delete), which still fits the narrowest layout (960 px window, sidebar expanded:
/// about 650 px of table). A value wider than its column ends in "…" and shows in full as a tooltip; headers wrap.
/// SQLite column names are in MeasurementRepository.cs.
/// </summary>
public static class MeasurementTableColumns
{
    private const string Reading = nameof(MeasurementResult.Reading) + ".";

    // Row position only: narrow.
    public static readonly MeasurementTableColumn Number = new()
    {
        Binding = nameof(MeasurementResult.Number), Header = "No", Width = Star(0.6), MinWidth = 32,
        CsvHeader = "No", CsvValue = r => r.Number
    };

    public static readonly MeasurementTableColumn CaptureNumber = new()
    {
        Binding = nameof(MeasurementResult.CaptureNumber), Header = "Capture No", Visible = false, Width = DataGridLength.Auto, MinWidth = 90,
        CsvHeader = "Capture No", CsvValue = r => r.CaptureNumber
    };

    public static readonly MeasurementTableColumn StartTime = new()
    {
        Binding = nameof(MeasurementResult.StartTime), Header = "Start Time", Visible = false, Width = DataGridLength.Auto, MinWidth = 160,
        Format = MeasurementCellFormat.Pattern(MeasurementCellFormat.TimestampFormat), CsvHeader = "Start Time", CsvValue = r => r.StartTime
    };

    public static readonly MeasurementTableColumn EndTime = new()
    {
        Binding = nameof(MeasurementResult.EndTime), Header = "End Time", Visible = false, Width = DataGridLength.Auto, MinWidth = 160,
        Format = MeasurementCellFormat.Pattern(MeasurementCellFormat.TimestampFormat), CsvHeader = "End Time", CsvValue = r => r.EndTime
    };

    // Saved depth interval of each capture: From and To in metres, Step in centimetres (table only). Read-only: the step
    // is changed with the Step (cm) input. Not exported, so the CSV columns stay as they were (set IncludeInCsv to true
    // to export them; the CSV value of Step stays in metres, step_m).
    public static readonly MeasurementTableColumn From = new()
    {
        Binding = nameof(MeasurementResult.FromMetres), Header = "From (m)", Width = Star(1), MinWidth = 56,
        Format = MeasurementCellFormat.Pattern("0.00#"), CsvHeader = "from_m", IncludeInCsv = false, CsvValue = r => r.FromMetres
    };

    public static readonly MeasurementTableColumn To = new()
    {
        Binding = nameof(MeasurementResult.ToMetres), Header = "To (m)", Width = Star(1), MinWidth = 56,
        Format = MeasurementCellFormat.Pattern("0.00#"), CsvHeader = "to_m", IncludeInCsv = false, CsvValue = r => r.ToMetres
    };

    public static readonly MeasurementTableColumn Step = new()
    {
        Binding = nameof(MeasurementResult.StepCentimetres), Header = "Step (cm)", Width = Star(1), MinWidth = 52,
        Format = MeasurementCellFormat.Pattern("0.#"), CsvHeader = "step_m", IncludeInCsv = false, CsvValue = r => r.StepMetres
    };

    // Samples averaged: the table shows "3 of 5" for an incomplete capture; CSV keeps the count.
    public static readonly MeasurementTableColumn Sample = new()
    {
        Binding = nameof(MeasurementResult.SampleDisplay), Header = "Sample", Width = Star(0.9), MinWidth = 52,
        CsvHeader = "Sample", CsvValue = r => r.SampleCount
    };

    // The table shows DisplayTsMs (device value, or the in-memory demo value) as local time, so it gets the most room;
    // the model, SQLite and CSV keep the Unix milliseconds (CSV writes only the device value).
    public static readonly MeasurementTableColumn TsMs = new()
    {
        Binding = nameof(MeasurementResult.DisplayTsMs), Header = "ts_ms", Width = Star(2.2), MinWidth = 64,
        Format = MeasurementCellFormat.UnixMillisecondsLocal, CsvHeader = "ts_ms", CsvValue = r => r.Reading.TsMs
    };

    // Device values: at most two decimals in the table; full precision everywhere else.
    public static readonly MeasurementTableColumn Count0 = new()
    {
        Binding = Reading + nameof(DeviceReading.Count0), Header = "count0", Width = Star(1), MinWidth = 52,
        Format = MeasurementCellFormat.Decimal, CsvHeader = "count0", CsvValue = r => r.Reading.Count0
    };

    public static readonly MeasurementTableColumn Count0Raw = new()
    {
        Binding = Reading + nameof(DeviceReading.Count0Raw), Header = "count0_raw", Width = Star(1.1), MinWidth = 56,
        Format = MeasurementCellFormat.Decimal, CsvHeader = "count0_raw", CsvValue = r => r.Reading.Count0Raw
    };

    public static readonly MeasurementTableColumn Hdm = new()
    {
        Binding = Reading + nameof(DeviceReading.Hdm), Header = "hdm", Width = Star(1), MinWidth = 48,
        Format = MeasurementCellFormat.Decimal, CsvHeader = "hdm", CsvValue = r => r.Reading.Hdm
    };

    public static readonly MeasurementTableColumn HdmRaw = new()
    {
        Binding = Reading + nameof(DeviceReading.HdmRaw), Header = "hdm_raw", Width = Star(1), MinWidth = 52,
        Format = MeasurementCellFormat.Decimal, CsvHeader = "hdm_raw", CsvValue = r => r.Reading.HdmRaw
    };

    public static readonly MeasurementTableColumn DoseRate = new()
    {
        Binding = Reading + nameof(DeviceReading.DoseRate), Header = "dose_rate", Width = Star(1), MinWidth = 56,
        Format = MeasurementCellFormat.Decimal, CsvHeader = "dose_rate", CsvValue = r => r.Reading.DoseRate
    };

    public static readonly MeasurementTableColumn SessionId = new()
    {
        Binding = nameof(MeasurementResult.SessionId), Header = "Session", Visible = false, Width = DataGridLength.Auto, MinWidth = 280,
        CsvHeader = "session_id", CsvValue = r => r.SessionId
    };

    // SessionId stays last so exported rows remain identifiable.
    public static IReadOnlyList<MeasurementTableColumn> All { get; } =
        [Number, CaptureNumber, StartTime, EndTime, From, To, Step, Sample, TsMs, Count0, Count0Raw, Hdm, HdmRaw, DoseRate, SessionId];

    public static DataGridLength Star(double share) => new(share, DataGridLengthUnitType.Star);
}

/// <summary>
/// Table sizes. Final size = default × multiplier: edit the multipliers (1.0 = the original compact look, 1.5 = +50 %,
/// 0.8 = −20 %) and leave the defaults. Column widths fit the table, so larger text shortens with "…" sooner.
/// </summary>
public static class MeasurementTableSizes
{
    public const double HeaderFontSizeMultiplier = 1.0;
    public const double RowFontSizeMultiplier = 1.0;
    public const double HeaderHeightMultiplier = 1.0;
    public const double RowHeightMultiplier = 1.0;
    public const double CellPaddingMultiplier = 1.0;     // also the header padding
    public const double DeleteIconSizeMultiplier = 1.0;  // delete glyph, its button and the Delete column
    public const double HeadingFontSizeMultiplier = 1.0;
    public const double ActionButtonFontSizeMultiplier = 1.0;
    public const double ActionButtonWidthMultiplier = 1.0;
    public const double ActionButtonHeightMultiplier = 1.0;
    public const double EmptyTextFontSizeMultiplier = 1.0;

    public const double HeaderFontSize = 12 * HeaderFontSizeMultiplier;
    public const double RowFontSize = 14 * RowFontSizeMultiplier;

    /// <summary>Minimum: a header wrapped onto two lines may be taller.</summary>
    public const double HeaderMinHeight = 34 * HeaderHeightMultiplier;

    public const double RowHeight = 36 * RowHeightMultiplier;
    public static readonly Thickness HeaderPadding = Scaled(new Thickness(8, 4, 8, 4), CellPaddingMultiplier);
    public static readonly Thickness CellPadding = Scaled(new Thickness(8, 0, 8, 0), CellPaddingMultiplier);
    public const double DeleteIconFontSize = 14 * DeleteIconSizeMultiplier;
    public const double DeleteButtonSize = 28 * DeleteIconSizeMultiplier;
    public static readonly DataGridLength DeleteColumnWidth = new(64 * DeleteIconSizeMultiplier);

    /// <summary>"Averaged Measurements".</summary>
    public const double HeadingFontSize = 18 * HeadingFontSizeMultiplier;

    /// <summary>New Measurement and Export CSV. Width and height are minimums; 42.62 is their natural height
    /// (14 px line + 2 × 11 padding + border) and 130 about Export CSV's natural width.</summary>
    public const double ActionButtonFontSize = 14 * ActionButtonFontSizeMultiplier;

    public const double ActionButtonMinWidth = 130 * ActionButtonWidthMultiplier;
    public const double ActionButtonMinHeight = 42.62 * ActionButtonHeightMultiplier;

    /// <summary>"Your averaged measurements will appear here."</summary>
    public const double EmptyTextFontSize = 14 * EmptyTextFontSizeMultiplier;

    private static Thickness Scaled(Thickness t, double multiplier) =>
        new(t.Left * multiplier, t.Top * multiplier, t.Right * multiplier, t.Bottom * multiplier);
}
