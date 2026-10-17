using System.Windows;
using System.Windows.Media;

namespace KeeStashDock.Desktop.Views;

/// <summary>
/// Paints depth around an existing card, without duplicating its content or
/// adding a dark backing behind a translucent face. Static drawing resources
/// are retained; proximity changes only their opacity and small translation.
/// </summary>
public sealed class CardDepthDecoration : FrameworkElement
{
    public static readonly DependencyProperty ActivityProperty = DependencyProperty.Register(
        nameof(Activity), typeof(double), typeof(CardDepthDecoration),
        new FrameworkPropertyMetadata(0.0, OnActivityChanged,
            (_, value) => value is double number && double.IsFinite(number) ? Math.Clamp(number, 0, 1) : 0.0));
    public static readonly DependencyProperty IsHighlightProperty = DependencyProperty.Register(
        nameof(IsHighlight), typeof(bool), typeof(CardDepthDecoration),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, OnAppearanceChanged));
    public static readonly DependencyProperty OutlineBrushProperty = DependencyProperty.Register(
        nameof(OutlineBrush), typeof(Brush), typeof(CardDepthDecoration),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender, OnAppearanceChanged));

    private DrawingGroup? _drawing, _shadows, _farOutline, _nearOutline, _rims;
    private TranslateTransform? _plateOffset;
    private Size _drawingSize = Size.Empty;

    public double Activity { get => (double)GetValue(ActivityProperty); set => SetValue(ActivityProperty, value); }
    public bool IsHighlight { get => (bool)GetValue(IsHighlightProperty); set => SetValue(IsHighlightProperty, value); }
    public Brush OutlineBrush { get => (Brush)GetValue(OutlineBrushProperty); set => SetValue(OutlineBrushProperty, value); }

    public CardDepthDecoration() => IsHitTestVisible = false;

    private static void OnActivityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((CardDepthDecoration)d).UpdateActivity();

    private static void OnAppearanceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((CardDepthDecoration)d)._drawing = null;

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        if (_drawing is null || _drawingSize != RenderSize) BuildDrawing();
        dc.DrawDrawing(_drawing);
    }

    private void BuildDrawing()
    {
        _drawingSize = RenderSize;
        _drawing = new DrawingGroup();
        _shadows = _farOutline = _nearOutline = _rims = null;
        _plateOffset = null;
        var face = new Rect(RenderSize);
        const double radius = 10;
        if (IsHighlight)
        {
            var geometry = new RectangleGeometry(face, radius, radius);
            geometry.Freeze();
            _drawing.Children.Add(FrozenDrawing(White(0.055), White(0.25), 1, geometry));
            UpdateActivity();
            return;
        }

        Rect outsideBounds = face;
        outsideBounds.Inflate(10, 11);
        var outside = new CombinedGeometry(GeometryCombineMode.Exclude,
            new RectangleGeometry(outsideBounds), new RectangleGeometry(face, radius, radius));
        outside.Freeze();
        // Keep the hollow clip stationary while the rear plates move slightly.
        // A translucent foreground must never acquire a dark backing.
        _drawing.ClipGeometry = outside;
        _plateOffset = new TranslateTransform();
        var plates = new DrawingGroup { Transform = _plateOffset };
        _drawing.Children.Add(plates);
        _shadows = new DrawingGroup();
        _farOutline = new DrawingGroup();
        _nearOutline = new DrawingGroup();
        _rims = new DrawingGroup();
        plates.Children.Add(_shadows);
        plates.Children.Add(_farOutline);
        plates.Children.Add(_nearOutline);
        plates.Children.Add(_rims);

        Brush outline = OutlineBrush.IsFrozen ? OutlineBrush : OutlineBrush.CloneCurrentValue();
        if (outline.CanFreeze) outline.Freeze();
        AddPlate(3.0, 5.2, 0.62, _farOutline);
        AddPlate(1.4, 2.6, 0.85, _nearOutline);
        UpdateActivity();

        void AddPlate(double x, double y, double opacity, DrawingGroup line)
        {
            Rect plate = face;
            plate.Offset(x, y);
            var geometry = new RectangleGeometry(plate, radius, radius);
            geometry.Freeze();
            for (int spread = 4; spread >= 1; spread--)
                _shadows.Children.Add(FrozenDrawing(null, Black(0.026 * opacity * (5 - spread)), spread * 1.5, geometry));
            line.Children.Add(FrozenDrawing(null, outline, 1.15, geometry));
            plate.Offset(-0.4, -0.45);
            var rim = new RectangleGeometry(plate, radius, radius);
            rim.Freeze();
            _rims.Children.Add(FrozenDrawing(null, White(0.24), 0.65, rim));
        }
    }

    private void UpdateActivity()
    {
        if (_drawing is null) return;
        double activity = Activity;
        if (IsHighlight)
        {
            _drawing.Opacity = activity;
            return;
        }
        if (_plateOffset is null) return;
        _plateOffset.X = 0.4 * activity;
        _plateOffset.Y = 0.8 * activity;
        _shadows!.Opacity = (0.018 + 0.008 * activity) / 0.026;
        _farOutline!.Opacity = 0.62 + 0.12 * activity;
        _nearOutline!.Opacity = 0.85 + 0.12 * activity;
        _rims!.Opacity = 0.5 + 0.5 * activity;
    }

    private static GeometryDrawing FrozenDrawing(Brush? fill, Brush stroke, double thickness, Geometry geometry)
    {
        var drawing = new GeometryDrawing(fill, new Pen(stroke, thickness), geometry);
        if (drawing.CanFreeze) drawing.Freeze();
        return drawing;
    }

    private static SolidColorBrush White(double alpha) => Brush(alpha, 255);
    private static SolidColorBrush Black(double alpha) => Brush(alpha, 0);
    private static SolidColorBrush Brush(double alpha, byte channel)
    {
        var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(alpha * 255), channel, channel, channel));
        brush.Freeze();
        return brush;
    }
}
