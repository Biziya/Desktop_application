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
using Frontend.Pages.Measurement.Table;

namespace Frontend.Pages.Measurement.CapturePanel;

/// <summary>
/// Bar chart of the rows: x = depth (m), one bar per from–to interval; y = the CPS the table shows. Rows without
/// depths or CPS are not drawn.
/// </summary>
public sealed class DepthBarChart : FrameworkElement
{
    private const double LeftMargin = 58;
    private const double BottomMargin = 34;
    private const double TopMargin = 8;
    private const double RightMargin = 10;

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(DepthBarChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnItemsSourceChanged));

    public static readonly DependencyProperty BarBrushProperty = Register<Brush>(nameof(BarBrush), Brushes.RoyalBlue);
    public static readonly DependencyProperty BarSeparatorBrushProperty = Register<Brush>(nameof(BarSeparatorBrush), Brushes.White);
    public static readonly DependencyProperty GridBrushProperty = Register<Brush>(nameof(GridBrush), Brushes.LightGray);
    public static readonly DependencyProperty AxisBrushProperty = Register<Brush>(nameof(AxisBrush), Brushes.Gray);
    public static readonly DependencyProperty ForegroundProperty = Register<Brush>(nameof(Foreground), Brushes.Black);
    public static readonly DependencyProperty XAxisLabelProperty = Register(nameof(XAxisLabel), "");
    public static readonly DependencyProperty YAxisLabelProperty = Register(nameof(YAxisLabel), "");
    public static readonly DependencyProperty EmptyTextProperty = Register(nameof(EmptyText), "");
    public static readonly DependencyProperty AxisLabelFontSizeProperty = Register(nameof(AxisLabelFontSize), 12d);
    public static readonly DependencyProperty TickFontSizeProperty = Register(nameof(TickFontSize), 11d);

    private readonly List<(Rect Area, MeasurementResult Row)> bars = [];
    private readonly ToolTip tip = new() { Placement = PlacementMode.Relative };

    // Drawing resources reused across renders (a resize renders many times); rebuilt when a brush, font or DPI changes.
    private Pen? axisPen;
    private Pen? gridPen;
    private Pen? separatorPen;
    private Typeface? typeface;
    private double pixelsPerDip;
    private MeasurementResult? hovered;

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

    public string XAxisLabel { get => (string)GetValue(XAxisLabelProperty); set => SetValue(XAxisLabelProperty, value); }

    public string YAxisLabel { get => (string)GetValue(YAxisLabelProperty); set => SetValue(YAxisLabelProperty, value); }

    public string EmptyText { get => (string)GetValue(EmptyTextProperty); set => SetValue(EmptyTextProperty, value); }

    public double AxisLabelFontSize { get => (double)GetValue(AxisLabelFontSizeProperty); set => SetValue(AxisLabelFontSizeProperty, value); }

    public double TickFontSize { get => (double)GetValue(TickFontSizeProperty); set => SetValue(TickFontSizeProperty, value); }

    private static DependencyProperty Register<T>(string name, T fallback) => DependencyProperty.Register(
        name, typeof(T), typeof(DepthBarChart), new FrameworkPropertyMetadata(fallback, FrameworkPropertyMetadataOptions.AffectsRender));

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

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.Property == AxisBrushProperty || e.Property == GridBrushProperty || e.Property == BarSeparatorBrushProperty)
        {
            axisPen = gridPen = separatorPen = null;
        }
        else if (e.Property == TextElement.FontFamilyProperty)
        {
            typeface = null;
        }
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        pixelsPerDip = newDpi.PixelsPerDip;
    }

    // Freezes a copy, never the brush itself (Theme brushes are shared).
    private static Pen Frozen(Brush brush)
    {
        var pen = new Pen(brush.IsFrozen ? brush : brush.CloneCurrentValue(), 1);

        if (pen.CanFreeze)
        {
            pen.Freeze();
        }

        return pen;
    }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        bars.Clear();
        hovered = null; // the row values may have changed: the next hover rebuilds the tooltip text

        var plot = new Rect(LeftMargin, TopMargin,
            Math.Max(0, ActualWidth - LeftMargin - RightMargin), Math.Max(0, ActualHeight - TopMargin - BottomMargin));

        if (plot.Width < 20 || plot.Height < 20)
        {
            return;
        }

        var rows = (ItemsSource?.OfType<MeasurementResult>() ?? [])
            .Where(r => r is { FromMm: not null, ToMm: not null, Reading.Count0: >= 0 } && r.ToMm > r.FromMm)
            .ToList();
        var axisPen = this.axisPen ??= Frozen(AxisBrush);
        var gridPen = this.gridPen ??= Frozen(GridBrush);

        // Axis titles.
        var xTitle = Text(XAxisLabel, AxisLabelFontSize);
        drawing.DrawText(xTitle, new Point(plot.Left + (plot.Width - xTitle.Width) / 2, plot.Bottom + 19));
        var yTitle = Text(YAxisLabel, AxisLabelFontSize);
        drawing.PushTransform(new RotateTransform(-90, 0, 0));
        drawing.DrawText(yTitle, new Point(-(plot.Top + (plot.Height + yTitle.Width) / 2), 0));
        drawing.Pop();

        if (rows.Count == 0)
        {
            drawing.DrawLine(axisPen, plot.BottomLeft, plot.BottomRight);
            drawing.DrawLine(axisPen, plot.TopLeft, plot.BottomLeft);
            var empty = Text(EmptyText, AxisLabelFontSize);
            drawing.DrawText(empty, new Point(plot.Left + (plot.Width - empty.Width) / 2, plot.Top + (plot.Height - empty.Height) / 2));
            return;
        }

        // X: depth range of all intervals, on round tick values. Y: 0 to a round value above the largest CPS.
        // One pass for the depth range and the largest CPS.
        long fromMin = long.MaxValue, toMax = long.MinValue;
        var cpsMax = 0d;

        foreach (var row in rows)
        {
            fromMin = Math.Min(fromMin, row.FromMm!.Value);
            toMax = Math.Max(toMax, row.ToMm!.Value);
            cpsMax = Math.Max(cpsMax, Cps(row));
        }

        var xStep = NiceStep((toMax - fromMin) / 1000d, Math.Max(2, (int)(plot.Width / 80)));
        var xMin = Math.Floor(fromMin / 1000d / xStep) * xStep;
        var xMax = Math.Max(xMin + xStep, Math.Ceiling(toMax / 1000d / xStep) * xStep);
        var yStep = NiceStep(Math.Max(0.01, cpsMax), Math.Max(2, (int)(plot.Height / 34)));
        var yMax = Math.Max(yStep, Math.Ceiling(cpsMax / yStep) * yStep);
        double X(double metres) => plot.Left + (metres - xMin) / (xMax - xMin) * plot.Width;
        double Y(double value) => plot.Bottom - value / yMax * plot.Height;

        for (var value = 0d; value <= yMax + yStep / 2; value += yStep)
        {
            var y = Math.Round(Y(value)) + 0.5;
            drawing.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            var label = Text(Compact(value), TickFontSize);
            drawing.DrawText(label, new Point(plot.Left - label.Width - 6, y - label.Height / 2));
        }

        for (var metres = xMin; metres <= xMax + xStep / 2; metres += xStep)
        {
            var x = Math.Round(X(metres)) + 0.5;
            drawing.DrawLine(axisPen, new Point(x, plot.Bottom), new Point(x, plot.Bottom + 4));
            var label = Text(metres.ToString("0.0##", CultureInfo.CurrentCulture), TickFontSize);
            drawing.DrawText(label, new Point(x - label.Width / 2, plot.Bottom + 5));
        }

        // A thin separator keeps adjacent intervals distinguishable once bars are wide enough.
        var separator = separatorPen ??= Frozen(BarSeparatorBrush);

        foreach (var row in rows)
        {
            var left = X(row.FromMm!.Value / 1000d);
            var right = X(row.ToMm!.Value / 1000d);
            var area = new Rect(left, Y(Cps(row)), Math.Max(1, right - left), plot.Bottom - Y(Cps(row)));
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
            hovered = null;
            return;
        }

        // The text is rebuilt only when the pointer moves to another bar; within a bar only the position follows.
        if (!ReferenceEquals(hit, hovered) || !tip.IsOpen)
        {
            hovered = hit;
            tip.Content =
                $"From: {Depth.Format(hit.FromMm!.Value)} m\n" +
                $"To: {Depth.Format(hit.ToMm!.Value)} m\n" +
                $"Step: {(hit.StepMm is { } step ? Depth.FormatCentimetres(step) : "—")} cm\n" +
                $"CPS: {MeasurementCellFormat.Decimal(Cps(hit))}";
        }
        tip.HorizontalOffset = point.X + 14;
        tip.VerticalOffset = point.Y + 14;
        tip.IsOpen = true;
    }

    private FormattedText Text(string text, double size)
    {
        typeface ??= new Typeface(TextElement.GetFontFamily(this), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        if (pixelsPerDip <= 0)
        {
            pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        }

        return new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, size, Foreground, pixelsPerDip);
    }

    // 1, 2 or 5 × a power of ten giving about <paramref name="count"/> ticks over <paramref name="range"/>.
    private static double NiceStep(double range, int count)
    {
        var raw = Math.Max(range, 1e-9) / count;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var normalized = raw / magnitude;
        return (normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10) * magnitude;
    }

    // The bar height: the finalized CPS of the row (drawn rows always have one).
    private static double Cps(MeasurementResult row) => row.Reading.Count0!.Value;

    // Short tick labels for large values: 2500 → 2.5k, 1 760 152 570 851 → 1.76T.
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
