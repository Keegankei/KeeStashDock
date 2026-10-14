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

    public double Strength { get => (double)GetValue(StrengthProperty); set => SetValue(StrengthProperty, value); }
    public double Spread { get => (double)GetValue(SpreadProperty); set => SetValue(SpreadProperty, value); }
    public double CornerRadius { get => (double)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }
    public double ShadowOffset { get => (double)GetValue(ShadowOffsetProperty); set => SetValue(ShadowOffsetProperty, value); }

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
