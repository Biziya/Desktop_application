namespace Frontend.Pages.Measurement;

/// <summary>Table layout of one measurement column.</summary>
/// <param name="Visible">Shown in the frontend table. Storage and CSV are not affected.</param>
/// <param name="Width">WPF DataGrid width: pixels ("52"), a star share ("*", "1.5*") or "Auto" (fits header and values).</param>
/// <param name="MinWidth">Smallest width in pixels.</param>
/// <param name="MaxWidth">Largest width in pixels; infinity means no limit.</param>
public sealed record ColumnLayout(bool Visible, string Width = "*", double MinWidth = 0, double MaxWidth = double.PositiveInfinity);

/// <summary>
/// Frontend table layout per measurement column — the only place table visibility and column widths are set.
/// Columns are identified by the shared definitions in MeasurementColumns.cs (which also hold labels and order).
/// The drag-handle and Delete columns, fonts, row heights and paddings stay in MeasurementUiConstants.xaml.
/// </summary>
public static class MeasurementColumnLayout
{
    private static readonly Dictionary<string, ColumnLayout> Layouts = new()
    {
        // Row position only: narrow, fixed.
        [MeasurementColumns.Number.Binding] = new(Visible: true, Width: "52", MinWidth: 44),
        [MeasurementColumns.CaptureNumber.Binding] = new(Visible: false, Width: "Auto", MinWidth: 90),
        [MeasurementColumns.StartTime.Binding] = new(Visible: false, Width: "Auto", MinWidth: 160),
        [MeasurementColumns.EndTime.Binding] = new(Visible: false, Width: "Auto", MinWidth: 160),
        [MeasurementColumns.From.Binding] = new(Visible: true, Width: "Auto", MinWidth: 80),
        [MeasurementColumns.To.Binding] = new(Visible: true, Width: "Auto", MinWidth: 80),
        [MeasurementColumns.Step.Binding] = new(Visible: true, Width: "Auto", MinWidth: 80),
        // Takes the remaining width so the table fills the card.
        [MeasurementColumns.Sample.Binding] = new(Visible: true, Width: "*", MinWidth: 72),
        // Device values size to their content so full-precision numbers are never clipped.
        [MeasurementColumns.TsMs.Binding] = new(Visible: true, Width: "Auto", MinWidth: 120),
        [MeasurementColumns.Count0.Binding] = new(Visible: true, Width: "Auto", MinWidth: 96),
        [MeasurementColumns.Count0Raw.Binding] = new(Visible: true, Width: "Auto", MinWidth: 104),
        [MeasurementColumns.Hdm.Binding] = new(Visible: true, Width: "Auto", MinWidth: 96),
        [MeasurementColumns.HdmRaw.Binding] = new(Visible: true, Width: "Auto", MinWidth: 96),
        [MeasurementColumns.DoseRate.Binding] = new(Visible: true, Width: "Auto", MinWidth: 96),
        [MeasurementColumns.SessionId.Binding] = new(Visible: false, Width: "Auto", MinWidth: 280),
    };

    public static ColumnLayout For(MeasurementColumn column) =>
        Layouts.TryGetValue(column.Binding, out var layout)
            ? layout
            : throw new InvalidOperationException($"MeasurementColumnLayout.cs has no entry for column '{column.Binding}'.");
}
