namespace Frontend.Pages.Measurement;

/// <summary>Table layout of one measurement column.</summary>
/// <param name="Visible">Shown in the frontend table. Storage and CSV are not affected.</param>
/// <param name="Width">WPF DataGrid width: pixels ("52"), a star share ("*", "1.5*") or "Auto" (fits header and values).</param>
/// <param name="MinWidth">Smallest width in pixels.</param>
/// <param name="MaxWidth">Largest width in pixels; infinity means no limit.</param>
public sealed record ColumnLayout(bool Visible, string Width = "*", double MinWidth = 0, double MaxWidth = double.PositiveInfinity);

/// <summary>
/// Frontend table layout per measurement column — the only place table visibility and column widths are set.
/// Visible columns use star widths (shares of the table width, in the proportions of the earlier compact layout), so
/// together they always fit the table: no horizontal scrolling with the sidebar expanded or collapsed. The minimum
/// widths add up to 576 px (+ 64 px Delete), which still fits the narrowest layout (minimum window 960 px with
/// the sidebar expanded: about 650 px of table inside the vertical scrollbar). A value wider than its column ends in "…" and shows in full as a
/// tooltip; headers wrap onto two lines. A horizontal scrollbar can only appear if the window gets narrower than that.
/// Columns are identified by the shared definitions in MeasurementColumns.cs (which also hold labels and order).
/// The Delete column, fonts, row heights and paddings stay in MeasurementUiConstants.xaml.
/// </summary>
public static class MeasurementColumnLayout
{
    private static readonly Dictionary<string, ColumnLayout> Layouts = new()
    {
        // Row position only: narrow.
        [MeasurementColumns.Number.Binding] = new(Visible: true, Width: "0.6*", MinWidth: 32),
        [MeasurementColumns.CaptureNumber.Binding] = new(Visible: false, Width: "Auto", MinWidth: 90),
        [MeasurementColumns.StartTime.Binding] = new(Visible: false, Width: "Auto", MinWidth: 160),
        [MeasurementColumns.EndTime.Binding] = new(Visible: false, Width: "Auto", MinWidth: 160),
        [MeasurementColumns.From.Binding] = new(Visible: true, Width: "1*", MinWidth: 56),
        [MeasurementColumns.To.Binding] = new(Visible: true, Width: "1*", MinWidth: 56),
        [MeasurementColumns.Step.Binding] = new(Visible: true, Width: "1*", MinWidth: 52),
        [MeasurementColumns.Sample.Binding] = new(Visible: true, Width: "0.9*", MinWidth: 52),
        // ts_ms shows a local timestamp ("2026-10-16 09:56:09"), so it gets the most room; device values show at most two
        // decimals and need less. A value that still does not fit shows "…" + tooltip.
        [MeasurementColumns.TsMs.Binding] = new(Visible: true, Width: "2.2*", MinWidth: 64),
        [MeasurementColumns.Count0.Binding] = new(Visible: true, Width: "1*", MinWidth: 52),
        [MeasurementColumns.Count0Raw.Binding] = new(Visible: true, Width: "1.1*", MinWidth: 56),
        [MeasurementColumns.Hdm.Binding] = new(Visible: true, Width: "1*", MinWidth: 48),
        [MeasurementColumns.HdmRaw.Binding] = new(Visible: true, Width: "1*", MinWidth: 52),
        [MeasurementColumns.DoseRate.Binding] = new(Visible: true, Width: "1*", MinWidth: 56),
        [MeasurementColumns.SessionId.Binding] = new(Visible: false, Width: "Auto", MinWidth: 280),
    };

    public static ColumnLayout For(MeasurementColumn column) =>
        Layouts.TryGetValue(column.Binding, out var layout)
            ? layout
            : throw new InvalidOperationException($"MeasurementColumnLayout.cs has no entry for column '{column.Binding}'.");
}
