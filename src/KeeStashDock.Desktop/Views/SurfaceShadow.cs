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
    public static readonly DependencyProperty DepthProperty = DependencyProperty.Register(
        nameof(Depth), typeof(double), typeof(SurfaceShadow),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty DepthBrushProperty = DependencyProperty.Register(
        nameof(DepthBrush), typeof(Brush), typeof(SurfaceShadow),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    private (Brush Brush, Pen? Pen)[]? _projectedLayers;
    private double _layerSpread, _layerStrength;
    private TranslateTransform? _shadowTranslation, _depthTranslation, _rimTranslation;
    private Brush? _depthBrush;
    private Pen? _depthPen;
    private static readonly Pen DepthRim = CreateDepthRim();

    public double Strength { get => (double)GetValue(StrengthProperty); set => SetValue(StrengthProperty, value); }
    public double Spread { get => (double)GetValue(SpreadProperty); set => SetValue(SpreadProperty, value); }
    public double CornerRadius { get => (double)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }
    public double ShadowOffset { get => (double)GetValue(ShadowOffsetProperty); set => SetValue(ShadowOffsetProperty, value); }
    /// <summary>Optional projected surface outline, expressed in this element's DIPs.</summary>
    public Geometry? Outline { get => (Geometry?)GetValue(OutlineProperty); set => SetValue(OutlineProperty, value); }
    /// <summary>Thin side wall outside the projected face; unused for ordinary shadows.</summary>
    public double Depth { get => (double)GetValue(DepthProperty); set => SetValue(DepthProperty, value); }
    public Brush DepthBrush { get => (Brush)GetValue(DepthBrushProperty); set => SetValue(DepthBrushProperty, value); }

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

    private void DrawProjectedShadow(DrawingContext dc, Geometry outline,
        double spread, double strength, double offset)
    {
        if (!outline.IsFrozen) outline = (Geometry)outline.GetCurrentValueAsFrozen();
        Rect bounds = outline.Bounds;
        if (bounds.IsEmpty) return;
        bounds.Inflate(spread + 1, spread + offset + 1);
        // Exclude the actual projected face, including its rounded corners.
        // A rectangular exclusion would leave dark wedges inside a tilted,
        // translucent panel and detach the shadow from the visible edge.
        var outside = new CombinedGeometry(GeometryCombineMode.Exclude,
            new RectangleGeometry(bounds), outline);
        outside.Freeze();
        dc.PushClip(outside);

        // Pose changes only the frozen outline. Retain the falloff brushes and
        // pens, and shift the drawing context instead of cloning that outline
        // for every frame of the pointer-follow animation.
        EnsureProjectedLayers(spread, strength);
        dc.PushTransform(Translation(ref _shadowTranslation, 0, offset));
        foreach (var layer in _projectedLayers!) dc.DrawGeometry(layer.Brush, layer.Pen, outline);
        dc.Pop();

        double depth = Math.Clamp(double.IsFinite(Depth) ? Depth : 0, 0, 16);
        if (depth > 0)
        {
            Brush brush = DepthBrush;
            if (!ReferenceEquals(_depthBrush, brush) || !brush.IsFrozen)
            {
                _depthBrush = brush;
                var snapshot = brush.IsFrozen ? brush : (Brush)brush.GetCurrentValueAsFrozen();
                _depthPen = new Pen(snapshot, 0.7) { LineJoin = PenLineJoin.Round };
                _depthPen.Freeze();
            }
            // The clip stays at the front face while this narrow backing moves
            // down/right. Its center is hollow even over a translucent panel.
            dc.PushTransform(Translation(ref _depthTranslation, depth * 0.5, depth));
            dc.PushOpacity(0.65);
            dc.DrawGeometry(brush, _depthPen, outline);
            dc.Pop();
            dc.Pop();
            dc.PushTransform(Translation(ref _rimTranslation, depth * 0.5 - 0.2, depth - 0.3));
            dc.DrawGeometry(null, DepthRim, outline);
            dc.Pop();
        }
        dc.Pop();
    }

    private void EnsureProjectedLayers(double spread, double strength)
    {
        if (_projectedLayers is not null && _layerSpread == spread && _layerStrength == strength) return;
        _layerSpread = spread;
        _layerStrength = strength;
        _projectedLayers = new (Brush, Pen?)[(int)Math.Ceiling(spread) + 1];
        // Each overlapping stroke adds only the missing opacity; caching does
        // not reduce the original soft shadow's layers or change its falloff.
        double previous = 0;
        for (int layer = _projectedLayers.Length - 1; layer >= 0; layer--)
        {
            double target = strength * Math.Exp(-4.0 * Math.Pow(layer / spread, 2));
            double alpha = Math.Clamp((target - previous) / (1 - previous), 0, 1);
            previous = target;
            var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * alpha), 0, 0, 0));
            brush.Freeze();
            Pen? pen = layer == 0 ? null : new Pen(brush, layer * 2) { LineJoin = PenLineJoin.Round };
            pen?.Freeze();
            _projectedLayers[_projectedLayers.Length - layer - 1] = (brush, pen);
        }
    }

    private static TranslateTransform Translation(ref TranslateTransform? cached, double x, double y)
    {
        if (cached is null || cached.X != x || cached.Y != y)
        {
            cached = new TranslateTransform(x, y);
            cached.Freeze();
        }
        return cached;
    }

    private static Pen CreateDepthRim()
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(42, 255, 255, 255)), 0.6)
            { LineJoin = PenLineJoin.Round };
        pen.Freeze();
        return pen;
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
