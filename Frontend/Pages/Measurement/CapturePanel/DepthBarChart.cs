using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Frontend.Pages.Measurement.CapturePanel;

/// <summary>
/// Bar chart of the rows: x = depth (m), one bar per from–to interval; y = the CPS the table shows. Rows without
/// depths or CPS are not drawn.
/// </summary>
public sealed class DepthBarChart : FrameworkElement
{
    // The left margin grows with the y tick labels; the y title sits YTitleGap left of them.
    private const double MinLeftMargin = 24;
    private const double YTitleGap = 4;
    private const double TickLabelGap = 6;
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

    /// <summary>CPS of the horizontal threshold line; NaN (default) or a negative value draws no line.</summary>
    public static readonly DependencyProperty ThresholdProperty = Register(nameof(Threshold), double.NaN);
    public static readonly DependencyProperty ShowThresholdProperty = Register(nameof(ShowThreshold), true);
    public static readonly DependencyProperty ThresholdBrushProperty = Register<Brush>(nameof(ThresholdBrush), Brushes.Firebrick);
    public static readonly DependencyProperty ThresholdLabelProperty = Register(nameof(ThresholdLabel), "Threshold");

    // The bars of the last render (tests read them).
    private readonly List<(Rect Area, MeasurementResult Row)> bars = [];

    // Drawing resources reused across renders (a resize renders many times); rebuilt when a brush, font or DPI changes.
    private Pen? axisPen;
    private Pen? gridPen;
    private Pen? separatorPen;
    private Pen? thresholdPen;
    private Typeface? typeface;
    private double pixelsPerDip;

    // The rows collection this chart listens to, only while it is on screen: the collection belongs to the long-lived
    // MeasurementViewModel and the view is re-created on every visit, so a lasting subscription would keep every old
    // chart (and its page) alive.
    private INotifyCollectionChanged? observed;

    // The y title is drawn turned a quarter left; one frozen transform serves every render.
    private static readonly RotateTransform QuarterLeft = FreezeAndReturn(new RotateTransform(-90, 0, 0));

    public DepthBarChart()
    {
        Loaded += (_, _) =>
        {
            Observe(ItemsSource as INotifyCollectionChanged);
            InvalidateVisual();
        };
        Unloaded += (_, _) => Observe(null);
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

    public double Threshold { get => (double)GetValue(ThresholdProperty); set => SetValue(ThresholdProperty, value); }

    public bool ShowThreshold { get => (bool)GetValue(ShowThresholdProperty); set => SetValue(ShowThresholdProperty, value); }

    public Brush ThresholdBrush { get => (Brush)GetValue(ThresholdBrushProperty); set => SetValue(ThresholdBrushProperty, value); }

    public string ThresholdLabel { get => (string)GetValue(ThresholdLabelProperty); set => SetValue(ThresholdLabelProperty, value); }

    /// <summary>Y position of the threshold line in the last render, or null when no line was drawn.</summary>
    public double? DrawnThresholdY { get; private set; }

    /// <summary>Right edge (x) of the rotated y title in the last render.</summary>
    public double? DrawnYTitleRight { get; private set; }

    /// <summary>Left edge (x) of the widest y tick label in the last render, or null when there was no y scale.</summary>
    public double? DrawnTickLabelsLeft { get; private set; }

    private bool HasThreshold => ShowThreshold && double.IsFinite(Threshold) && Threshold >= 0;

    private static DependencyProperty Register<T>(string name, T fallback) => DependencyProperty.Register(
        name, typeof(T), typeof(DepthBarChart), new FrameworkPropertyMetadata(fallback, FrameworkPropertyMetadataOptions.AffectsRender));

    // Redraw when rows are added, removed, moved or reloaded (while on screen; see Loaded).
    private static void OnItemsSourceChanged(DependencyObject owner, DependencyPropertyChangedEventArgs e)
    {
        var chart = (DepthBarChart)owner;

        if (chart.IsLoaded)
        {
            chart.Observe(e.NewValue as INotifyCollectionChanged);
        }
    }

    private void Observe(INotifyCollectionChanged? items)
    {
        if (observed is not null)
        {
            observed.CollectionChanged -= OnItemsChanged;
        }

        observed = items;

        if (items is not null)
        {
            items.CollectionChanged += OnItemsChanged;
        }
    }

    private static T FreezeAndReturn<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.Property == AxisBrushProperty || e.Property == GridBrushProperty || e.Property == BarSeparatorBrushProperty)
        {
            axisPen = gridPen = separatorPen = null;
        }
        else if (e.Property == ThresholdBrushProperty)
        {
            thresholdPen = null;
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

    private static Pen DashedPen(Brush brush)
    {
        var pen = new Pen(brush.IsFrozen ? brush : brush.CloneCurrentValue(), 1.5) { DashStyle = new DashStyle([6, 3], 0) };

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
        DrawnThresholdY = null;

        DrawnYTitleRight = null;
        DrawnTickLabelsLeft = null;

        var plotHeight = Math.Max(0, ActualHeight - TopMargin - BottomMargin);

        if (plotHeight < 20 || ActualWidth - MinLeftMargin - RightMargin < 20)
        {
            return;
        }

        var rows = (ItemsSource?.OfType<MeasurementResult>() ?? [])
            .Where(r => r is { FromMm: not null, ToMm: not null, Reading.Count0: >= 0 } && r.ToMm > r.FromMm)
            .ToList();
        var axisPen = this.axisPen ??= Frozen(AxisBrush);
        var gridPen = this.gridPen ??= Frozen(GridBrush);

        // Y: 0 to a round value above the largest CPS and above a visible threshold, so the threshold line is on the
        // graph in every state: before the first capture (no rows), while measuring and after Stop / New Measurement.
        // Without rows and without a threshold there is no y scale (just the axes and the empty text).
        var cpsMax = rows.Count == 0 ? 0d : rows.Max(Cps);

        if (HasThreshold)
        {
            cpsMax = Math.Max(cpsMax, Threshold);
        }

        var hasYScale = rows.Count > 0 || HasThreshold;
        var yStep = NiceStep(Math.Max(0.01, cpsMax), Math.Max(2, (int)(plotHeight / 34)));
        var yMax = Math.Max(yStep, Math.Ceiling(cpsMax / yStep) * yStep);
        var yLabels = new List<(double Value, FormattedText Label)>();

        if (hasYScale)
        {
            for (var value = 0d; value <= yMax + yStep / 2; value += yStep)
            {
                yLabels.Add((value, Text(Compact(value), TickFontSize)));
            }
        }

        // The y title sits right next to the tick labels: the left margin is the title, a small gap and the widest label.
        var yTitle = Text(YAxisLabel, AxisLabelFontSize);
        var tickWidth = yLabels.Count == 0 ? 0 : yLabels.Max(l => l.Label.Width);
        var titleWidth = YAxisLabel.Length == 0 ? 0 : yTitle.Height;
        var leftMargin = Math.Max(MinLeftMargin, titleWidth + YTitleGap + tickWidth + TickLabelGap);
        var plot = new Rect(leftMargin, TopMargin, Math.Max(0, ActualWidth - leftMargin - RightMargin), plotHeight);

        if (plot.Width < 20)
        {
            return;
        }

        double Y(double value) => plot.Bottom - value / yMax * plot.Height;

        // Axis titles.
        var xTitle = Text(XAxisLabel, AxisLabelFontSize);
        drawing.DrawText(xTitle, new Point(plot.Left + (plot.Width - xTitle.Width) / 2, plot.Bottom + 19));
        var titleLeft = plot.Left - TickLabelGap - tickWidth - YTitleGap - titleWidth;
        drawing.PushTransform(QuarterLeft);
        drawing.DrawText(yTitle, new Point(-(plot.Top + (plot.Height + yTitle.Width) / 2), titleLeft));
        drawing.Pop();
        DrawnYTitleRight = titleLeft + titleWidth;

        foreach (var (value, label) in yLabels)
        {
            var y = Math.Round(Y(value)) + 0.5;
            drawing.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            drawing.DrawText(label, new Point(plot.Left - label.Width - TickLabelGap, y - label.Height / 2));
        }

        if (yLabels.Count > 0)
        {
            DrawnTickLabelsLeft = plot.Left - TickLabelGap - tickWidth;
        }

        if (rows.Count == 0)
        {
            DrawThreshold(drawing, plot, Y);
            drawing.DrawLine(axisPen, plot.BottomLeft, plot.BottomRight);
            drawing.DrawLine(axisPen, plot.TopLeft, plot.BottomLeft);
            var empty = Text(EmptyText, AxisLabelFontSize);
            drawing.DrawText(empty, new Point(plot.Left + (plot.Width - empty.Width) / 2, plot.Top + (plot.Height - empty.Height) / 2));
            return;
        }

        // X: depth range of all intervals, on round tick values.
        var fromMin = rows.Min(r => r.FromMm!.Value);
        var toMax = rows.Max(r => r.ToMm!.Value);
        var xStep = NiceStep((toMax - fromMin) / 1000d, Math.Max(2, (int)(plot.Width / 80)));
        var xMin = Math.Floor(fromMin / 1000d / xStep) * xStep;
        var xMax = Math.Max(xMin + xStep, Math.Ceiling(toMax / 1000d / xStep) * xStep);
        double X(double metres) => plot.Left + (metres - xMin) / (xMax - xMin) * plot.Width;

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

        DrawThreshold(drawing, plot, Y);
        drawing.DrawLine(axisPen, plot.BottomLeft, plot.BottomRight);
        drawing.DrawLine(axisPen, plot.TopLeft, plot.BottomLeft);
    }

    // The one place the threshold is drawn: a dashed horizontal line over the bars, labelled at its right end. Every
    // render (new rows, clear, resize, page switch, Show/Hide) goes through it, so the line never drops out.
    private void DrawThreshold(DrawingContext drawing, Rect plot, Func<double, double> y)
    {
        if (!HasThreshold)
        {
            return;
        }

        var lineY = Math.Round(y(Threshold)) + 0.5;
        drawing.DrawLine(thresholdPen ??= DashedPen(ThresholdBrush), new Point(plot.Left, lineY), new Point(plot.Right, lineY));
        var label = new FormattedText($"{ThresholdLabel} {Compact(Threshold)}", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            typeface!, TickFontSize, ThresholdBrush, pixelsPerDip);
        drawing.DrawText(label, new Point(plot.Right - label.Width - 4, Math.Max(plot.Top, lineY - label.Height - 2)));
        DrawnThresholdY = lineY;
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
