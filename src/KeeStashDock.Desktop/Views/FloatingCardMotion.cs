using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

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
    private static readonly DependencyProperty ControllerProperty = DependencyProperty.RegisterAttached(
        "Controller", typeof(Controller), typeof(FloatingCardMotion));

    public static bool GetEnabled(DependencyObject value) => (bool)value.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject value, bool enabled) => value.SetValue(EnabledProperty, enabled);
    public static FrameworkElement? GetTarget(DependencyObject value) => (FrameworkElement?)value.GetValue(TargetProperty);
    public static void SetTarget(DependencyObject value, FrameworkElement target) => value.SetValue(TargetProperty, target);
    internal static bool AnimationsAllowed => SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;

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
        private readonly MotionValue _scaleX, _scaleY, _angle, _lift;
        private bool _pressed;

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
            host.MouseEnter += OnMove;
            host.MouseMove += OnMove;
            host.MouseLeave += OnLeave;
            host.IsVisibleChanged += OnVisibilityChanged;
            host.LostMouseCapture += OnLostCapture;
            host.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(OnDown), true);
            host.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(OnUp), true);
        }

        private void OnMove(object sender, MouseEventArgs e)
        {
            if (!AnimationsAllowed || !_host.IsVisible) { Reset(); return; }
            // Pin/delete remain independent buttons, with their normal feedback.
            if (IsButton(e.OriginalSource as DependencyObject)) { Rest(animate: true); return; }
            if (_pressed && e.LeftButton != MouseButtonState.Pressed) _pressed = false;
            Point point = e.GetPosition(_host);
            double normalizedX = Math.Clamp(point.X / Math.Max(1, _host.ActualWidth), 0, 1) - 0.5;
            _lift.To(_pressed ? 0 : -2);
            // Bound the corner displacement as well as the angle, including a
            // very wide resized panel, so the reserved shadow margin suffices.
            _angle.To(_pressed ? 0 : normalizedX * Math.Min(0.9, 120 / Math.Max(1, _host.ActualWidth)));
            _scaleX.To(_pressed ? 0.985 : 1);
            _scaleY.To(_pressed ? 0.985 : 1);
        }

        private void OnDown(object sender, MouseButtonEventArgs e)
        {
            if (IsButton(e.OriginalSource as DependencyObject) || !AnimationsAllowed) return;
            _pressed = true;
            OnMove(sender, e);
        }
        private void OnUp(object sender, MouseButtonEventArgs e) { _pressed = false; OnMove(sender, e); }
        private void OnLeave(object sender, MouseEventArgs e) { _pressed = false; Rest(AnimationsAllowed); }
        private void OnLostCapture(object sender, MouseEventArgs e) => Reset();
        private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
        { if (!(bool)e.NewValue) Reset(); }

        internal void Reset() { _pressed = false; Rest(animate: false); }
        private void Rest(bool animate)
        {
            _scaleX.To(1, animate);
            _scaleY.To(1, animate);
            _angle.To(0, animate);
            _lift.To(0, animate);
        }
        public void Dispose()
        {
            Reset();
            _host.MouseEnter -= OnMove;
            _host.MouseMove -= OnMove;
            _host.MouseLeave -= OnLeave;
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
            value = value is Visual ? VisualTreeHelper.GetParent(value) : LogicalTreeHelper.GetParent(value);
        }
        return false;
    }
}

/// <summary>Finite tween that releases its clock on completion or cancellation.</summary>
internal sealed class MotionValue(Animatable target, DependencyProperty property)
{
    private int _generation;
    private double? _destination;

    internal void To(double value, bool animate = true)
    {
        if (animate && _destination == value) return;
        double from = (double)target.GetValue(property);
        int generation = ++_generation;
        _destination = value;
        target.BeginAnimation(property, null);
        target.SetValue(property, value);
        if (!animate || Math.Abs(from - value) < 0.001) return;
        var animation = new DoubleAnimation(from, value, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop,
        };
        animation.Completed += (_, _) =>
        {
            if (_generation == generation) target.BeginAnimation(property, null);
        };
        target.BeginAnimation(property, animation);
    }
}
