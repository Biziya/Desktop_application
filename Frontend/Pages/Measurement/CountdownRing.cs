using System.Windows;
using System.Windows.Media;
namespace Frontend.Pages.Measurement;

/// <summary>Elapsed progress only. Timing and capture completion are owned by the ViewModel/service.</summary>
public sealed class CountdownRing : FrameworkElement
{
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(CountdownRing),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(CountdownRing),
        new FrameworkPropertyMetadata(Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ProgressBrushProperty = DependencyProperty.Register(
        nameof(ProgressBrush), typeof(Brush), typeof(CountdownRing),
        new FrameworkPropertyMetadata(Brushes.RoyalBlue, FrameworkPropertyMetadataOptions.AffectsRender));
    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty,value); }
    public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty,value); }
    public Brush ProgressBrush { get => (Brush)GetValue(ProgressBrushProperty); set => SetValue(ProgressBrushProperty,value); }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        const double thickness = 8;
        var radius = Math.Min(ActualWidth,ActualHeight) / 2 - thickness / 2 - 1;
        if (radius <= 0) return;
        var center = new Point(ActualWidth/2,ActualHeight/2);
        drawing.DrawEllipse(null,new Pen(TrackBrush,thickness),center,radius,radius);
        var progress = double.IsFinite(Progress) ? Math.Clamp(Progress,0,1) : 0;
        if (progress <= 0) return;
        var pen = new Pen(ProgressBrush,thickness) { StartLineCap=PenLineCap.Round, EndLineCap=PenLineCap.Round };
        if (progress >= 1) { drawing.DrawEllipse(null,pen,center,radius,radius); return; }
        var angle = progress * Math.PI * 2;
        var start = new Point(center.X,center.Y-radius);
        var end = new Point(center.X+radius*Math.Sin(angle),center.Y-radius*Math.Cos(angle));
        var arc = new StreamGeometry();
        using (var context = arc.Open())
        {
            context.BeginFigure(start,false,false);
            context.ArcTo(end,new Size(radius,radius),0,progress>0.5,SweepDirection.Clockwise,true,false);
        }
        arc.Freeze();
        drawing.DrawGeometry(null,pen,arc);
    }
}
