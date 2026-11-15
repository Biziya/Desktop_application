using System.Windows;
using System.Windows.Media;

namespace Frontend.Pages.Measurement.CapturePanel;

/// <summary>Elapsed progress only. Timing and capture completion are owned by the ViewModel/service.</summary>
public sealed class CountdownRing : FrameworkElement
{
    private const double DefaultThickness = 8;

    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress),
        typeof(double),
        typeof(CountdownRing),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush),
        typeof(Brush),
        typeof(CountdownRing),
        new FrameworkPropertyMetadata(Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ProgressBrushProperty = DependencyProperty.Register(
        nameof(ProgressBrush),
        typeof(Brush),
        typeof(CountdownRing),
        new FrameworkPropertyMetadata(Brushes.RoyalBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RingThicknessProperty = DependencyProperty.Register(
        nameof(RingThickness),
        typeof(double),
        typeof(CountdownRing),
        new FrameworkPropertyMetadata(DefaultThickness, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public Brush TrackBrush
    {
        get => (Brush)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public Brush ProgressBrush
    {
        get => (Brush)GetValue(ProgressBrushProperty);
        set => SetValue(ProgressBrushProperty, value);
    }

    public double RingThickness
    {
        get => (double)GetValue(RingThicknessProperty);
        set => SetValue(RingThicknessProperty, value);
    }

    // Pens reused across progress updates; rebuilt when a brush or the thickness changes.
    private Pen? trackPen;
    private Pen? progressPen;

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.Property == TrackBrushProperty || e.Property == ProgressBrushProperty || e.Property == RingThicknessProperty)
        {
            trackPen = progressPen = null;
        }
    }

    // Freezes a copy, never the brush itself (Theme brushes are shared).
    private static Pen Frozen(Brush brush, double thickness, bool round)
    {
        var pen = new Pen(brush.IsFrozen ? brush : brush.CloneCurrentValue(), thickness);

        if (round)
        {
            pen.StartLineCap = PenLineCap.Round;
            pen.EndLineCap = PenLineCap.Round;
        }

        if (pen.CanFreeze)
        {
            pen.Freeze();
        }

        return pen;
    }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);

        var thickness = double.IsFinite(RingThickness) && RingThickness > 0 ? RingThickness : DefaultThickness;
        var radius = Math.Min(ActualWidth, ActualHeight) / 2 - thickness / 2 - 1;

        if (radius <= 0)
        {
            return;
        }

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        drawing.DrawEllipse(null, trackPen ??= Frozen(TrackBrush, thickness, round: false), center, radius, radius);

        var progress = double.IsFinite(Progress) ? Math.Clamp(Progress, 0, 1) : 0;

        if (progress <= 0)
        {
            return;
        }

        var pen = progressPen ??= Frozen(ProgressBrush, thickness, round: true);

        if (progress >= 1)
        {
            drawing.DrawEllipse(null, pen, center, radius, radius);
            return;
        }

        var angle = progress * Math.PI * 2;
        var start = new Point(center.X, center.Y - radius);
        var end = new Point(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle));
        var arc = new StreamGeometry();

        using (var context = arc.Open())
        {
            context.BeginFigure(start, false, false);
            context.ArcTo(end, new Size(radius, radius), 0, progress > 0.5, SweepDirection.Clockwise, true, false);
        }

        arc.Freeze();
        drawing.DrawGeometry(null, pen, arc);
    }
}
