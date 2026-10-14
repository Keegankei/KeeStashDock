using System.Windows;
using System.Windows.Media;

namespace KeeStashDock.Desktop.Views;

/// <summary>
/// A hollow shadow around a surface. The surface itself is excluded from every
/// layer, so translucent backgrounds never acquire a black backing and text is
/// never rasterized through an effect. Drawing outside the element does not add
/// layout padding or a hit-test area.
/// </summary>
public sealed class SurfaceShadow : FrameworkElement
{
    public static readonly DependencyProperty StrengthProperty = DependencyProperty.Register(
        nameof(Strength), typeof(double), typeof(SurfaceShadow),
        new FrameworkPropertyMetadata(0.16, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SpreadProperty = DependencyProperty.Register(
        nameof(Spread), typeof(double), typeof(SurfaceShadow),
        new FrameworkPropertyMetadata(7.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(double), typeof(SurfaceShadow),
        new FrameworkPropertyMetadata(10.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ShadowOffsetProperty = DependencyProperty.Register(
        nameof(ShadowOffset), typeof(double), typeof(SurfaceShadow),
        new FrameworkPropertyMetadata(2.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty OutlineProperty = DependencyProperty.Register(
        nameof(Outline), typeof(Geometry), typeof(SurfaceShadow),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Strength { get => (double)GetValue(StrengthProperty); set => SetValue(StrengthProperty, value); }
    public double Spread { get => (double)GetValue(SpreadProperty); set => SetValue(SpreadProperty, value); }
    public double CornerRadius { get => (double)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }
    public double ShadowOffset { get => (double)GetValue(ShadowOffsetProperty); set => SetValue(ShadowOffsetProperty, value); }
    /// <summary>Optional projected surface outline, expressed in this element's DIPs.</summary>
    public Geometry? Outline { get => (Geometry?)GetValue(OutlineProperty); set => SetValue(OutlineProperty, value); }

    public SurfaceShadow() => IsHitTestVisible = false;

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        double spread = Math.Clamp(Spread, 1, 96);
        double strength = Math.Clamp(Strength, 0, 0.5);
        double radius = Math.Clamp(CornerRadius, 0, Math.Min(ActualWidth, ActualHeight) / 2);
        double offset = Math.Clamp(ShadowOffset, 0, 16);
        if (Outline is { } outline)
        {
            DrawProjectedShadow(dc, outline, spread, strength, offset);
            return;
        }
        var surface = new RectangleGeometry(bounds, radius, radius);
        for (int layer = (int)Math.Ceiling(spread); layer >= 0; layer--)
        {
            // Adjacent rings have no overlapping alpha. The small step is softer
            // than a shader shadow on small cards and costs nothing while idle.
            double alpha = strength * Math.Exp(-4.0 * Math.Pow(layer / spread, 2));
            var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * alpha), 0, 0, 0));
            brush.Freeze();
            Geometry ring = Ring(bounds, radius, offset, layer);
            var hollow = new CombinedGeometry(GeometryCombineMode.Exclude, ring, surface);
            hollow.Freeze();
            dc.DrawGeometry(brush, null, hollow);
        }
    }

    private static void DrawProjectedShadow(DrawingContext dc, Geometry outline,
        double spread, double strength, double offset)
    {
        Rect bounds = outline.Bounds;
        if (bounds.IsEmpty || strength <= 0) return;
        bounds.Inflate(spread + 1, spread + offset + 1);
        // Exclude the actual projected face, including its rounded corners.
        // A rectangular exclusion would leave dark wedges inside a tilted,
        // translucent panel and detach the shadow from the visible edge.
        var outside = new CombinedGeometry(GeometryCombineMode.Exclude,
            new RectangleGeometry(bounds), outline);
        outside.Freeze();
        dc.PushClip(outside);

        Geometry shifted = outline.CloneCurrentValue();
        var transform = new TransformGroup();
        transform.Children.Add(shifted.Transform);
        transform.Children.Add(new TranslateTransform(0, offset));
        shifted.Transform = transform;
        shifted.Freeze();

        // Concentric strokes avoid rebuilding widened path geometries on every
        // pointer frame. Each stroke adds only the missing opacity, so their
        // overlap follows the same soft falloff as the rectangular shadow.
        double previous = 0;
        for (int layer = (int)Math.Ceiling(spread); layer >= 0; layer--)
        {
            double target = strength * Math.Exp(-4.0 * Math.Pow(layer / spread, 2));
            double alpha = Math.Clamp((target - previous) / (1 - previous), 0, 1);
            previous = target;
            var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * alpha), 0, 0, 0));
            brush.Freeze();
            Pen? pen = layer == 0 ? null : new Pen(brush, layer * 2) { LineJoin = PenLineJoin.Round };
            pen?.Freeze();
            dc.DrawGeometry(brush, pen, shifted);
        }
        dc.Pop();
    }

    internal static Geometry Ring(Rect bounds, double radius, double offset, int layer)
    {
        Rect outer = bounds;
        outer.Offset(0, offset);
        outer.Inflate(layer, layer);
        var geometry = new RectangleGeometry(outer, radius + layer, radius + layer);
        if (layer == 0) return geometry;
        Rect inner = bounds;
        inner.Offset(0, offset);
        inner.Inflate(layer - 1, layer - 1);
        return new CombinedGeometry(GeometryCombineMode.Exclude, geometry,
            new RectangleGeometry(inner, radius + layer - 1, radius + layer - 1));
    }
}
