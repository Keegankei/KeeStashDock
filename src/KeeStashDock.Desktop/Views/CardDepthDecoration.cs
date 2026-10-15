using System.Windows;
using System.Windows.Media;

namespace KeeStashDock.Desktop.Views;

/// <summary>
/// Paints depth around an existing card, without duplicating its content or
/// adding a dark backing behind a translucent face. The front instance adds
/// a restrained light wash; the rear instance draws two offset hollow plates.
/// </summary>
public sealed class CardDepthDecoration : FrameworkElement
{
    public static readonly DependencyProperty ActivityProperty = DependencyProperty.Register(
        nameof(Activity), typeof(double), typeof(CardDepthDecoration),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender,
            null, (_, value) => value is double number && double.IsFinite(number) ? Math.Clamp(number, 0, 1) : 0.0));
    public static readonly DependencyProperty IsHighlightProperty = DependencyProperty.Register(
        nameof(IsHighlight), typeof(bool), typeof(CardDepthDecoration),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty OutlineBrushProperty = DependencyProperty.Register(
        nameof(OutlineBrush), typeof(Brush), typeof(CardDepthDecoration),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Activity { get => (double)GetValue(ActivityProperty); set => SetValue(ActivityProperty, value); }
    public bool IsHighlight { get => (bool)GetValue(IsHighlightProperty); set => SetValue(IsHighlightProperty, value); }
    public Brush OutlineBrush { get => (Brush)GetValue(OutlineBrushProperty); set => SetValue(OutlineBrushProperty, value); }

    public CardDepthDecoration() => IsHitTestVisible = false;

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        var face = new Rect(0, 0, ActualWidth, ActualHeight);
        const double radius = 10;
        if (IsHighlight)
        {
            if (Activity <= 0) return;
            // A small wash makes dark and image-backed cards respond without
            // recoloring their theme or applying any effect to readable text.
            dc.DrawRoundedRectangle(White(0.055 * Activity),
                new Pen(White(0.25 * Activity), 1), face, radius, radius);
            return;
        }

        var silhouette = new RectangleGeometry(face, radius, radius);
        Rect outsideBounds = face;
        outsideBounds.Inflate(10, 11);
        var outside = new CombinedGeometry(GeometryCombineMode.Exclude,
            new RectangleGeometry(outsideBounds), silhouette);
        outside.Freeze();
        dc.PushClip(outside);

        // The plate interiors are excluded above, including under a partially
        // transparent foreground card. Narrow strokes keep each layer legible.
        DrawPlate(3.0, 5.2, 0.62);
        DrawPlate(1.4, 2.6, 0.85);
        dc.Pop();

        void DrawPlate(double x, double y, double opacity)
        {
            Rect plate = face;
            plate.Offset(x + 0.4 * Activity, y + 0.8 * Activity);
            for (int spread = 4; spread >= 1; spread--)
            {
                double alpha = (0.018 + Activity * 0.008) * opacity * (5 - spread);
                dc.DrawRoundedRectangle(null, new Pen(Black(alpha), spread * 1.5),
                    plate, radius, radius);
            }
            dc.PushOpacity(Math.Min(1, opacity + 0.12 * Activity));
            dc.DrawRoundedRectangle(null, new Pen(OutlineBrush, 1.15), plate, radius, radius);
            dc.Pop();
            plate.Offset(-0.4, -0.45);
            dc.DrawRoundedRectangle(null, new Pen(White(0.12 + 0.12 * Activity), 0.65),
                plate, radius, radius);
        }
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
