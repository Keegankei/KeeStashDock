using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;

namespace KeeStashDock.Desktop.Views;

/// <summary>
/// Event-driven feedback on a visual child of a stationary card hit area. No
/// input is handled, captured, delayed, or synthesized by this behavior.
/// </summary>
public static class FloatingCardMotion
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(FloatingCardMotion), new PropertyMetadata(false, OnEnabledChanged));
    public static readonly DependencyProperty TargetProperty = DependencyProperty.RegisterAttached(
        "Target", typeof(FrameworkElement), typeof(FloatingCardMotion));
    public static readonly DependencyProperty DecorationProperty = DependencyProperty.RegisterAttached(
        "Decoration", typeof(CardDepthDecoration), typeof(FloatingCardMotion));
    public static readonly DependencyProperty HighlightProperty = DependencyProperty.RegisterAttached(
        "Highlight", typeof(CardDepthDecoration), typeof(FloatingCardMotion));
    private static readonly DependencyProperty ControllerProperty = DependencyProperty.RegisterAttached(
        "Controller", typeof(Controller), typeof(FloatingCardMotion));

    public static bool GetEnabled(DependencyObject value) => (bool)value.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject value, bool enabled) => value.SetValue(EnabledProperty, enabled);
    public static FrameworkElement? GetTarget(DependencyObject value) => (FrameworkElement?)value.GetValue(TargetProperty);
    public static void SetTarget(DependencyObject value, FrameworkElement target) => value.SetValue(TargetProperty, target);
    public static CardDepthDecoration? GetDecoration(DependencyObject value) => (CardDepthDecoration?)value.GetValue(DecorationProperty);
    public static void SetDecoration(DependencyObject value, CardDepthDecoration decoration) => value.SetValue(DecorationProperty, decoration);
    public static CardDepthDecoration? GetHighlight(DependencyObject value) => (CardDepthDecoration?)value.GetValue(HighlightProperty);
    public static void SetHighlight(DependencyObject value, CardDepthDecoration highlight) => value.SetValue(HighlightProperty, highlight);
    internal static bool AnimationsAllowed => SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;
    internal const double ProximityReach = 40;

    /// <summary>
    /// Called by the panel's existing sampler. Coordinates belong to the fixed
    /// panel window, never to the moving face. Only realized visuals are visited.
    /// </summary>
    internal static void UpdateProximity(DependencyObject subtree, Point? pointerInPanel, FrameworkElement panel)
    {
        DependencyObject? hit = pointerInPanel is { } point && double.IsFinite(point.X) && double.IsFinite(point.Y)
            ? panel.InputHitTest(point) as DependencyObject : null;
        VisitControllers(subtree, controller => controller.UpdateProximity(pointerInPanel, panel, hit));
    }

    internal static void FreezeProximity(DependencyObject subtree)
        => VisitControllers(subtree, controller => controller.Freeze());

    private static void VisitControllers(DependencyObject subtree, Action<Controller> visit)
    {
        if (subtree.GetValue(ControllerProperty) is Controller controller)
        {
            visit(controller);
            return; // A realized card owns its content, never another card host.
        }
        if (subtree is not Visual and not Visual3D) return;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(subtree); i++)
            VisitControllers(VisualTreeHelper.GetChild(subtree, i), visit);
    }

    /// <summary>Distance to the actual projected, viewport-clipped card quad.</summary>
    internal static double ProximityActivity(Point pointer, IReadOnlyList<Point> polygon)
    {
        if (!double.IsFinite(pointer.X) || !double.IsFinite(pointer.Y) || polygon.Count < 3) return 0;
        bool inside = false;
        double distance = double.PositiveInfinity;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            Point a = polygon[j], b = polygon[i];
            if (!double.IsFinite(a.X) || !double.IsFinite(a.Y) || !double.IsFinite(b.X) || !double.IsFinite(b.Y)) return 0;
            if ((a.Y > pointer.Y) != (b.Y > pointer.Y) &&
                pointer.X < (b.X - a.X) * (pointer.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
            Vector edge = b - a;
            double t = edge.LengthSquared <= 0 ? 0 : Math.Clamp(Vector.Multiply(pointer - a, edge) / edge.LengthSquared, 0, 1);
            distance = Math.Min(distance, (pointer - (a + edge * t)).Length);
        }
        if (inside) return 1;
        double weight = Math.Clamp(1 - distance / ProximityReach, 0, 1);
        return weight * weight * (3 - 2 * weight);
    }

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;
        element.Loaded -= OnLoaded;
        element.Unloaded -= OnUnloaded;
        Stop(element);
        if (!(bool)e.NewValue) return;
        element.Loaded += OnLoaded;
        element.Unloaded += OnUnloaded;
        if (element.IsLoaded) Start(element);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e) => Start((FrameworkElement)sender);
    private static void OnUnloaded(object sender, RoutedEventArgs e) => Stop((FrameworkElement)sender);
    private static void Start(FrameworkElement element)
    {
        if (element.GetValue(ControllerProperty) is null && GetEnabled(element) &&
            GetTarget(element) is { } target)
            element.SetValue(ControllerProperty, new Controller(element, target));
    }
    private static void Stop(FrameworkElement element)
    {
        (element.GetValue(ControllerProperty) as Controller)?.Dispose();
        element.ClearValue(ControllerProperty);
    }

    internal static void Reset(DependencyObject element)
        => (element.GetValue(ControllerProperty) as Controller)?.Reset();

    private sealed class Controller : IDisposable
    {
        private readonly FrameworkElement _host, _target;
        private readonly Transform _previousTransform;
        private readonly Point _previousOrigin;
        private readonly ScaleTransform _scale = new();
        private readonly RotateTransform _rotate = new();
        private readonly TranslateTransform _translate = new();
        private readonly MotionValue _scaleX, _scaleY, _angle, _lift, _shift;
        private readonly MotionValue? _rearActivity, _frontActivity;
        private bool _pressed, _frozen;

        internal Controller(FrameworkElement host, FrameworkElement target)
        {
            _host = host;
            _target = target;
            _previousTransform = target.RenderTransform;
            _previousOrigin = target.RenderTransformOrigin;
            var transforms = new TransformGroup();
            transforms.Children.Add(_scale);
            transforms.Children.Add(_rotate);
            transforms.Children.Add(_translate);
            target.RenderTransform = transforms;
            target.RenderTransformOrigin = new Point(0.5, 0.5);
            _scaleX = new(_scale, ScaleTransform.ScaleXProperty);
            _scaleY = new(_scale, ScaleTransform.ScaleYProperty);
            _angle = new(_rotate, RotateTransform.AngleProperty);
            _lift = new(_translate, TranslateTransform.YProperty);
            _shift = new(_translate, TranslateTransform.XProperty);
            if (GetDecoration(host) is { } rear) _rearActivity = new(rear, CardDepthDecoration.ActivityProperty);
            if (GetHighlight(host) is { } front) _frontActivity = new(front, CardDepthDecoration.ActivityProperty);
            host.MouseEnter += OnMove;
            host.MouseMove += OnMove;
            host.IsVisibleChanged += OnVisibilityChanged;
            host.LostMouseCapture += OnLostCapture;
            host.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(OnDown), true);
            host.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(OnUp), true);
        }

        private void OnMove(object sender, MouseEventArgs e)
        {
            if (!AnimationsAllowed || !_host.IsVisible) { Reset(); return; }
            // Keep pin/delete exactly under the cursor: resetting the whole
            // card here would move a button between pointer-down and pointer-up.
            if (IsButton(e.OriginalSource as DependencyObject)) { Freeze(); return; }
            if (_pressed && e.LeftButton != MouseButtonState.Pressed) _pressed = false;
            if (_frozen && !_pressed) return;
            Point point = e.GetPosition(_host);
            Apply(1, Math.Clamp(point.X / Math.Max(1, _host.ActualWidth) * 2 - 1, -1, 1),
                Math.Clamp(point.Y / Math.Max(1, _host.ActualHeight) * 2 - 1, -1, 1));
        }

        internal void UpdateProximity(Point? pointer, FrameworkElement panel, DependencyObject? hit)
        {
            if (!AnimationsAllowed || !_host.IsVisible) { Reset(); return; }
            if (_pressed && Mouse.LeftButton != MouseButtonState.Pressed) _pressed = false;
            if (_pressed) return;
            if (IsButton(hit) && IsWithinHost(hit)) { Freeze(); return; }
            _frozen = false;
            if (pointer is not { } point || !double.IsFinite(point.X) || !double.IsFinite(point.Y))
            { Rest(animate: true); return; }
            Point[]? quad = VisibleQuad(panel);
            if (quad is null) { Rest(animate: true); return; }
            double activity = ProximityActivity(point, quad);
            if (activity <= 0) { Rest(animate: true); return; }
            var bounds = Rect.Empty;
            foreach (Point corner in quad) bounds.Union(corner);
            double x = Math.Clamp((point.X - bounds.X) / Math.Max(1, bounds.Width) * 2 - 1, -1, 1);
            double y = Math.Clamp((point.Y - bounds.Y) / Math.Max(1, bounds.Height) * 2 - 1, -1, 1);
            Apply(Math.Round(activity, 2), Math.Round(x, 2), Math.Round(y, 2));
        }

        private Point[]? VisibleQuad(FrameworkElement panel)
        {
            try
            {
                // Clip in the unchanged 2D content plane before projecting.
                // Virtualized or scrolled-out cards cannot react through the
                // input row or outside the scrolling viewport.
                FrameworkElement source = _host;
                var bounds = new Rect(_host.RenderSize);
                for (DependencyObject? parent = Parent(_host); parent is not null; parent = Parent(parent))
                {
                    if (parent is not ScrollContentPresenter viewport) continue;
                    bounds = _host.TransformToAncestor(viewport).TransformBounds(bounds);
                    bounds.Intersect(new Rect(viewport.RenderSize));
                    source = viewport;
                    break;
                }
                if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) return null;
                GeneralTransform transform = source.TransformToVisual(panel);
                Point[] points = [bounds.TopLeft, bounds.TopRight, bounds.BottomRight, bounds.BottomLeft];
                for (int i = 0; i < points.Length; i++)
                    if (!transform.TryTransform(points[i], out points[i])) return null;
                return points;
            }
            catch (InvalidOperationException) { return null; }
            catch (ArgumentException) { return null; }
        }

        private bool IsWithinHost(DependencyObject? value)
        {
            while (value is not null)
            {
                if (ReferenceEquals(value, _host)) return true;
                value = Parent(value);
            }
            return false;
        }

        private void Apply(double activity, double x, double y)
        {
            _lift.To(_pressed ? 1 : (-2.8 + y * 0.5) * activity);
            _shift.To(_pressed ? 0 : x * 2.4 * activity);
            _angle.To(_pressed ? 0 : x * Math.Min(0.65, 100 / Math.Max(1, _host.ActualWidth)) * activity);
            double expansion = Math.Min(0.006, 2 / Math.Max(1, _host.ActualWidth)) * activity;
            _scaleX.To(_pressed ? 0.992 : 1 + expansion);
            _scaleY.To(_pressed ? 0.992 : 1 + expansion);
            _rearActivity?.To(_pressed ? 0.3 : activity);
            _frontActivity?.To(_pressed ? 0.3 : activity);
        }

        internal void Freeze()
        {
            _frozen = true;
            if (_pressed) return;
            _scaleX.Freeze(); _scaleY.Freeze(); _angle.Freeze(); _lift.Freeze(); _shift.Freeze();
            _rearActivity?.Freeze(); _frontActivity?.Freeze();
        }

        private void OnDown(object sender, MouseButtonEventArgs e)
        {
            if (IsButton(e.OriginalSource as DependencyObject)) { Freeze(); return; }
            if (!AnimationsAllowed) return;
            _pressed = true;
            OnMove(sender, e);
        }
        private void OnUp(object sender, MouseButtonEventArgs e) { _pressed = false; OnMove(sender, e); }
        // The shared sampler owns pointer departure: moving into the 40 DIP
        // approach area must fade smoothly rather than briefly snap to rest.
        private void OnLostCapture(object sender, MouseEventArgs e) { if (!_frozen) Reset(); }
        private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
        { if (!(bool)e.NewValue) Reset(); }

        internal void Reset() { _pressed = false; _frozen = false; Rest(animate: false); }
        private void Rest(bool animate)
        {
            _scaleX.To(1, animate);
            _scaleY.To(1, animate);
            _angle.To(0, animate);
            _lift.To(0, animate);
            _shift.To(0, animate);
            _rearActivity?.To(0, animate);
            _frontActivity?.To(0, animate);
        }
        public void Dispose()
        {
            Reset();
            _host.MouseEnter -= OnMove;
            _host.MouseMove -= OnMove;
            _host.IsVisibleChanged -= OnVisibilityChanged;
            _host.LostMouseCapture -= OnLostCapture;
            _host.RemoveHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(OnDown));
            _host.RemoveHandler(UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(OnUp));
            _target.RenderTransform = _previousTransform;
            _target.RenderTransformOrigin = _previousOrigin;
        }
    }

    private static bool IsButton(DependencyObject? value)
    {
        while (value is not null)
        {
            if (value is ButtonBase) return true;
            value = Parent(value);
        }
        return false;
    }

    private static DependencyObject? Parent(DependencyObject value)
        => value is Visual or Visual3D ? VisualTreeHelper.GetParent(value) : LogicalTreeHelper.GetParent(value);
}

/// <summary>Finite tween that releases its clock on completion or cancellation.</summary>
internal sealed class MotionValue(DependencyObject target, DependencyProperty property)
{
    private int _generation;
    private double? _destination;

    internal void To(double value, bool animate = true)
    {
        if (animate && _destination == value) return;
        double from = (double)target.GetValue(property);
        int generation = ++_generation;
        _destination = value;
        ((IAnimatable)target).BeginAnimation(property, null);
        target.SetValue(property, value);
        if (!animate || Math.Abs(from - value) < 0.001) return;
        var animation = new DoubleAnimation(from, value, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop,
        };
        animation.Completed += (_, _) =>
        {
            if (_generation == generation) ((IAnimatable)target).BeginAnimation(property, null);
        };
        ((IAnimatable)target).BeginAnimation(property, animation);
    }

    internal void Freeze() => To((double)target.GetValue(property), animate: false);
}
