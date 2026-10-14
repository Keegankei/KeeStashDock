using System.Windows;
using System.Windows.Media;

namespace KeeStashDock.Desktop.Views;

/// <summary>
/// Original vector chrome for floating surfaces. Decoration never participates
/// in input, text layout or animation; the content retains a stable hit area.
/// </summary>
public sealed class FloatingFrame : FrameworkElement
{
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(FloatingFrame), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(FloatingFrame), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty BackplateProperty = DependencyProperty.Register(
        nameof(Backplate), typeof(bool), typeof(FloatingFrame), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush? Fill { get => (Brush?)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush? Stroke { get => (Brush?)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public bool Backplate { get => (bool)GetValue(BackplateProperty); set => SetValue(BackplateProperty, value); }

    private static readonly Brush Signal = FrozenBrush(Color.FromRgb(224, 195, 57));
    private static readonly Brush SignalInk = FrozenBrush(Color.FromRgb(47, 46, 38));
    private Size _geometrySize;
    private Geometry? _outline;

    public FloatingFrame() { IsHitTestVisible = false; SnapsToDevicePixels = true; }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        double width = ActualWidth, height = ActualHeight;
        if (width < 24 || height < 24) return;
        if (_outline is null || _geometrySize != RenderSize)
        {
            _geometrySize = RenderSize;
            _outline = Outline(width, height);
        }
        drawing.DrawGeometry(Fill, Stroke is null ? null : new Pen(Stroke, 1), _outline);
        if (Backplate) return;

        // A short signal tab and a corner bracket establish a common visual
        // axis without decorative labels or game assets in the product.
        drawing.DrawRectangle(Signal, null, new Rect(0.5, 14, 3, Math.Min(24, height - 22)));
        drawing.DrawLine(new Pen(Signal, 2), new Point(width - 26, 1), new Point(width - 13, 1));
        drawing.DrawLine(new Pen(Signal, 2), new Point(width - 13, 1), new Point(width - 1, 13));
        drawing.DrawLine(new Pen(Stroke ?? SignalInk, 1), new Point(width - 23, height - 5), new Point(width - 7, height - 5));
    }

    private static Geometry Outline(double width, double height)
    {
        var geometry = new StreamGeometry();
        using (StreamGeometryContext path = geometry.Open())
        {
            path.BeginFigure(new Point(0.5, 0.5), true, true);
            path.LineTo(new Point(width - 13, 0.5), true, false);
            path.LineTo(new Point(width - 0.5, 13), true, false);
            path.LineTo(new Point(width - 0.5, height - 5), true, false);
            path.LineTo(new Point(width - 5, height - 0.5), true, false);
            path.LineTo(new Point(0.5, height - 0.5), true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    private static Brush FrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
