using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace Frontend.Shared.Infrastructure;

/// <summary>
/// Smooth scrolling for a ScrollViewer (its offsets are not animatable): <see cref="ScrollTo"/> animates the attached
/// <c>VerticalOffset</c>, which scrolls the viewer as it changes. Jumps instead when Windows animations are off.
/// </summary>
public static class ScrollAnimation
{
    public static readonly DependencyProperty VerticalOffsetProperty = DependencyProperty.RegisterAttached(
        "VerticalOffset", typeof(double), typeof(ScrollAnimation),
        new PropertyMetadata(0.0, (d, e) => (d as ScrollViewer)?.ScrollToVerticalOffset((double)e.NewValue)));

    public static double GetVerticalOffset(DependencyObject element) => (double)element.GetValue(VerticalOffsetProperty);

    public static void SetVerticalOffset(DependencyObject element, double value) => element.SetValue(VerticalOffsetProperty, value);

    /// <summary>Scrolls <paramref name="viewer"/> to <paramref name="offset"/>: about 250 ms ease-out, or at once.</summary>
    public static void ScrollTo(ScrollViewer viewer, double offset, bool animate)
    {
        offset = Math.Clamp(offset, 0, Math.Max(0, viewer.ExtentHeight - viewer.ViewportHeight));

        // Stop any running animation, starting from where the viewer is now.
        viewer.BeginAnimation(VerticalOffsetProperty, null);
        SetVerticalOffset(viewer, viewer.VerticalOffset);

        if (!animate || Math.Abs(offset - viewer.VerticalOffset) < 1)
        {
            SetVerticalOffset(viewer, offset);
            viewer.ScrollToVerticalOffset(offset);
            return;
        }

        var animation = new DoubleAnimation(viewer.VerticalOffset, offset, TimeSpan.FromMilliseconds(250))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        animation.Completed += (_, _) => SetVerticalOffset(viewer, offset);
        viewer.BeginAnimation(VerticalOffsetProperty, animation);
    }
}

/// <summary>Places each child centred at a fraction of its own size (attached X / Y, 0..1). Takes no space itself.</summary>
public sealed class FractionPanel : Panel
{
    public static readonly DependencyProperty XProperty = DependencyProperty.RegisterAttached(
        "X", typeof(double), typeof(FractionPanel), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static readonly DependencyProperty YProperty = DependencyProperty.RegisterAttached(
        "Y", typeof(double), typeof(FractionPanel), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static double GetX(DependencyObject element) => (double)element.GetValue(XProperty);

    public static void SetX(DependencyObject element, double value) => element.SetValue(XProperty, value);

    public static double GetY(DependencyObject element) => (double)element.GetValue(YProperty);

    public static void SetY(DependencyObject element, double value) => element.SetValue(YProperty, value);

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        }

        return default;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (UIElement child in InternalChildren)
        {
            var size = child.DesiredSize;
            var x = GetX(child) * finalSize.Width - size.Width / 2;
            var y = GetY(child) * finalSize.Height - size.Height / 2;
            child.Arrange(new Rect(new Point(x, y), size));
        }

        return finalSize;
    }
}
