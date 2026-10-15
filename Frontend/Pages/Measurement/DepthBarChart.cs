using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Frontend.Calculation.Measurement;

namespace Frontend.Pages.Measurement;

/// <summary>
/// Bar chart of the measurement rows: x = depth (m), y = ts_ms. Each row is one vertical bar from its saved
/// <c>from</c> to <c>to</c> (so widths follow the actual interval, including step changes) and from 0 up to the same
/// ts_ms the table shows. Bars are placed by depth (rows are also listed by From). Rows without depths or
/// ts_ms are not drawn. Hover a bar for its values. Rendering only — the data stays in the ViewModel.
/// </summary>
public sealed class DepthBarChart : FrameworkElement
{
    private const double LeftMargin = 58;
    private const double BottomMargin = 40;
    private const double TopMargin = 8;
    private const double RightMargin = 10;

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(DepthBarChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnItemsSourceChanged));

    public static readonly DependencyProperty BarBrushProperty = Register(nameof(BarBrush), Brushes.RoyalBlue);
    public static readonly DependencyProperty BarSeparatorBrushProperty = Register(nameof(BarSeparatorBrush), Brushes.White);
    public static readonly DependencyProperty GridBrushProperty = Register(nameof(GridBrush), Brushes.LightGray);
    public static readonly DependencyProperty AxisBrushProperty = Register(nameof(AxisBrush), Brushes.Gray);
    public static readonly DependencyProperty ForegroundProperty = Register(nameof(Foreground), Brushes.Black);

    private readonly List<(Rect Area, MeasurementResult Row)> bars = [];
    private readonly ToolTip tip = new() { Placement = PlacementMode.Relative };

    public DepthBarChart()
    {
        tip.PlacementTarget = this;
        MouseLeave += (_, _) => tip.IsOpen = false;
    }

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public Brush BarBrush { get => (Brush)GetValue(BarBrushProperty); set => SetValue(BarBrushProperty, value); }

    public Brush BarSeparatorBrush { get => (Brush)GetValue(BarSeparatorBrushProperty); set => SetValue(BarSeparatorBrushProperty, value); }

    public Brush GridBrush { get => (Brush)GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }

    public Brush AxisBrush { get => (Brush)GetValue(AxisBrushProperty); set => SetValue(AxisBrushProperty, value); }

    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    private static DependencyProperty Register(string name, Brush fallback) => DependencyProperty.Register(
        name, typeof(Brush), typeof(DepthBarChart), new FrameworkPropertyMetadata(fallback, FrameworkPropertyMetadataOptions.AffectsRender));

    // Redraw when rows are added, removed, moved or reloaded.
    private static void OnItemsSourceChanged(DependencyObject owner, DependencyPropertyChangedEventArgs e)
    {
        var chart = (DepthBarChart)owner;

        if (e.OldValue is INotifyCollectionChanged oldItems)
        {
            oldItems.CollectionChanged -= chart.OnItemsChanged;
        }

        if (e.NewValue is INotifyCollectionChanged newItems)
        {
            newItems.CollectionChanged += chart.OnItemsChanged;
        }
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        bars.Clear();

        var plot = new Rect(LeftMargin, TopMargin,
            Math.Max(0, ActualWidth - LeftMargin - RightMargin), Math.Max(0, ActualHeight - TopMargin - BottomMargin));

        if (plot.Width < 20 || plot.Height < 20)
        {
            return;
        }

        var rows = (ItemsSource?.OfType<MeasurementResult>() ?? [])
            .Where(r => r is { FromMm: not null, ToMm: not null, DisplayTsMs: >= 0 } && r.ToMm > r.FromMm)
            .ToList();
        var axisPen = new Pen(AxisBrush, 1);
        var gridPen = new Pen(GridBrush, 1);

        // Axis titles.
        var xTitle = Text("Depth (m)", 12);
        drawing.DrawText(xTitle, new Point(plot.Left + (plot.Width - xTitle.Width) / 2, plot.Bottom + 22));
        var yTitle = Text("ts_ms", 12);
        drawing.PushTransform(new RotateTransform(-90, 0, 0));
        drawing.DrawText(yTitle, new Point(-(plot.Top + (plot.Height + yTitle.Width) / 2), 0));
        drawing.Pop();

        if (rows.Count == 0)
        {
            drawing.DrawLine(axisPen, plot.BottomLeft, plot.BottomRight);
            drawing.DrawLine(axisPen, plot.TopLeft, plot.BottomLeft);
            var empty = Text("Captured intervals appear here as bars.", 12);
            drawing.DrawText(empty, new Point(plot.Left + (plot.Width - empty.Width) / 2, plot.Top + (plot.Height - empty.Height) / 2));
            return;
        }

        // X: depth range of all intervals, on round tick values. Y: 0 to a round value above the largest ts_ms.
        var xStep = NiceStep((rows.Max(r => r.ToMm!.Value) - rows.Min(r => r.FromMm!.Value)) / 1000d, Math.Max(2, (int)(plot.Width / 80)));
        var xMin = Math.Floor(rows.Min(r => r.FromMm!.Value) / 1000d / xStep) * xStep;
        var xMax = Math.Max(xMin + xStep, Math.Ceiling(rows.Max(r => r.ToMm!.Value) / 1000d / xStep) * xStep);
        var yStep = NiceStep(Math.Max(1, rows.Max(r => r.DisplayTsMs!.Value)), Math.Max(2, (int)(plot.Height / 34)));
        var yMax = Math.Max(yStep, Math.Ceiling(rows.Max(r => r.DisplayTsMs!.Value) / yStep) * yStep);
        double X(double metres) => plot.Left + (metres - xMin) / (xMax - xMin) * plot.Width;
        double Y(double value) => plot.Bottom - value / yMax * plot.Height;

        for (var value = 0d; value <= yMax + yStep / 2; value += yStep)
        {
            var y = Math.Round(Y(value)) + 0.5;
            drawing.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            var label = Text(Compact(value), 11);
            drawing.DrawText(label, new Point(plot.Left - label.Width - 6, y - label.Height / 2));
        }

        for (var metres = xMin; metres <= xMax + xStep / 2; metres += xStep)
        {
            var x = Math.Round(X(metres)) + 0.5;
            drawing.DrawLine(axisPen, new Point(x, plot.Bottom), new Point(x, plot.Bottom + 4));
            var label = Text(metres.ToString("0.0##", CultureInfo.CurrentCulture), 11);
            drawing.DrawText(label, new Point(x - label.Width / 2, plot.Bottom + 5));
        }

        // A thin separator keeps adjacent intervals distinguishable once bars are wide enough.
        var separator = new Pen(BarSeparatorBrush, 1);

        foreach (var row in rows)
        {
            var left = X(row.FromMm!.Value / 1000d);
            var right = X(row.ToMm!.Value / 1000d);
            var area = new Rect(left, Y(row.DisplayTsMs!.Value), Math.Max(1, right - left), plot.Bottom - Y(row.DisplayTsMs.Value));
            drawing.DrawRectangle(BarBrush, area.Width >= 4 ? separator : null, area);
            bars.Add((area, row));
        }

        drawing.DrawLine(axisPen, plot.BottomLeft, plot.BottomRight);
        drawing.DrawLine(axisPen, plot.TopLeft, plot.BottomLeft);
    }

    // Tooltip of the bar under the pointer (the last drawn wins where intervals overlap).
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var point = e.GetPosition(this);
        var hit = bars.LastOrDefault(b => b.Area.Contains(point)).Row;

        if (hit is null)
        {
            tip.IsOpen = false;
            return;
        }

        tip.Content =
            $"From: {Depth.Format(hit.FromMm!.Value)} m\n" +
            $"To: {Depth.Format(hit.ToMm!.Value)} m\n" +
            $"Step: {(hit.StepMm is { } step ? Depth.FormatCentimetres(step) : "—")} cm\n" +
            $"ts_ms: {hit.DisplayTsMs!.Value.ToString("N0", CultureInfo.CurrentCulture)}" + (hit.IsDemoTsMs ? " (demo)" : "");
        tip.HorizontalOffset = point.X + 14;
        tip.VerticalOffset = point.Y + 14;
        tip.IsOpen = true;
    }

    private FormattedText Text(string text, double size) => new(
        text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
        new Typeface(TextElement.GetFontFamily(this), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
        size, Foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    // 1, 2 or 5 × a power of ten giving about <paramref name="count"/> ticks over <paramref name="range"/>.
    private static double NiceStep(double range, int count)
    {
        var raw = Math.Max(range, 1e-9) / count;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var normalized = raw / magnitude;
        return (normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10) * magnitude;
    }

    // Short tick labels for large ts_ms values: 2500 → 2.5k, 1 760 152 570 851 → 1.76T.
    private static string Compact(double value)
    {
        string[] suffixes = ["", "k", "M", "G", "T"];
        var index = 0;

        while (Math.Abs(value) >= 1000 && index < suffixes.Length - 1)
        {
            value /= 1000;
            index++;
        }

        return value.ToString("0.##", CultureInfo.CurrentCulture) + suffixes[index];
    }
}
