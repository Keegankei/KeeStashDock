using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace KeeStashDock.Desktop.Views;

/// <summary>
/// Presents the same live controls either normally or on a perspective plane.
/// WPF performs the projected hit testing, focus and input-coordinate mapping;
/// the surface never captures pixels, duplicates controls or synthesizes input.
/// </summary>
[ContentProperty(nameof(Child))]
public sealed class PerspectiveSurface : FrameworkElement
{
    internal const double MaximumYaw = 12;
    internal const double MaximumPitch = 9;
    internal const double MaximumOffset = 4;

    public static readonly DependencyProperty IsPerspectiveEnabledProperty = DependencyProperty.Register(
        nameof(IsPerspectiveEnabled), typeof(bool), typeof(PerspectiveSurface),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure, OnModeChanged));
    public static readonly DependencyProperty YawProperty = DependencyProperty.Register(
        nameof(Yaw), typeof(double), typeof(PerspectiveSurface),
        new PropertyMetadata(0.0, OnPoseChanged, (_, value) => ClampFinite(value, MaximumYaw)));
    public static readonly DependencyProperty PitchProperty = DependencyProperty.Register(
        nameof(Pitch), typeof(double), typeof(PerspectiveSurface),
        new PropertyMetadata(0.0, OnPoseChanged, (_, value) => ClampFinite(value, MaximumPitch)));
    public static readonly DependencyProperty OffsetXProperty = DependencyProperty.Register(
        nameof(OffsetX), typeof(double), typeof(PerspectiveSurface),
        new PropertyMetadata(0.0, OnPoseChanged, (_, value) => ClampFinite(value, MaximumOffset)));
    public static readonly DependencyProperty OffsetYProperty = DependencyProperty.Register(
        nameof(OffsetY), typeof(double), typeof(PerspectiveSurface),
        new PropertyMetadata(0.0, OnPoseChanged, (_, value) => ClampFinite(value, MaximumOffset)));

    // This logical parent is stable across mode changes. In particular a local
    // DataContext, inherited resources, TextBox selection and ListBox state do
    // not belong to a newly created copy of the panel.
    private readonly Decorator _content = new();
    private readonly Viewport3D _viewport = new() { ClipToBounds = false };
    private readonly Viewport2DVisual3D _plane = new();
    private readonly PerspectiveCamera _camera = new() { LookDirection = new Vector3D(0, 0, -1), UpDirection = new Vector3D(0, 1, 0) };
    private readonly MatrixTransform3D _rotation = new();
    private readonly TranslateTransform _offset = new();
    private bool _projected;
    private bool _batchPose;
    private readonly BitmapCache _contentCache = new();
    private Size _cameraSize = Size.Empty;
    private double _focalLength;
    private double _cameraDistance;

    public PerspectiveSurface()
    {
        // EmissiveMaterial adds RGB without coverage alpha on a transparent
        // viewport. That makes ordinary text wash out when the layered window
        // is composited. White ambient light preserves the UI's colors while a
        // diffuse host writes the matching premultiplied alpha.
        var material = new DiffuseMaterial(Brushes.White);
        Viewport2DVisual3D.SetIsVisualHostMaterial(material, true);
        material.Freeze();
        _plane.Material = material;
        _plane.Transform = _rotation;
        _viewport.Camera = _camera;
        var light = new AmbientLight(Colors.White);
        light.Freeze();
        _viewport.Children.Add(new ModelVisual3D { Content = light });
        _viewport.Children.Add(_plane);
        _viewport.RenderTransform = _offset;
        AddLogicalChild(_content);
        AddVisualChild(_content);
    }

    public UIElement? Child
    {
        get => _content.Child;
        set
        {
            if (ReferenceEquals(value, _content.Child)) return;
            _content.Child = value;
            InvalidateMeasure();
        }
    }

    public bool IsPerspectiveEnabled { get => (bool)GetValue(IsPerspectiveEnabledProperty); set => SetValue(IsPerspectiveEnabledProperty, value); }
    public double Yaw { get => (double)GetValue(YawProperty); set => SetValue(YawProperty, value); }
    public double Pitch { get => (double)GetValue(PitchProperty); set => SetValue(PitchProperty, value); }
    public double OffsetX { get => (double)GetValue(OffsetXProperty); set => SetValue(OffsetXProperty, value); }
    public double OffsetY { get => (double)GetValue(OffsetYProperty); set => SetValue(OffsetYProperty, value); }

    internal Size ContentSize => _content.RenderSize;
    internal FrameworkElement ContentRoot => _content;
    internal event EventHandler? ProjectionChanged;

    // Inspection captures input at the window. Its children need no 3D hit
    // tests until the drag ends; ordinary input always restores this subtree.
    internal void SetInspectionActive(bool active) => _viewport.IsHitTestVisible = !active;

    internal void SetPose(PerspectivePose pose)
    {
        if (Yaw == pose.Yaw && Pitch == pose.Pitch && OffsetX == pose.OffsetX && OffsetY == pose.OffsetY) return;
        _batchPose = true;
        try
        {
            SetCurrentValue(YawProperty, pose.Yaw);
            SetCurrentValue(PitchProperty, pose.Pitch);
            SetCurrentValue(OffsetXProperty, pose.OffsetX);
            SetCurrentValue(OffsetYProperty, pose.OffsetY);
        }
        finally { _batchPose = false; }
        UpdatePose();
    }

    protected override IEnumerator LogicalChildren => new object[] { _content }.GetEnumerator();
    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index)
        => index == 0 ? (_projected ? _viewport : _content) : throw new ArgumentOutOfRangeException(nameof(index));

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_projected)
        {
            // A Viewport2DVisual3D uses a VisualBrush internally. Explicit finite
            // dimensions stop that brush measuring the editor/list at infinity.
            SetContentDimensions(availableSize);
            _viewport.Measure(availableSize);
        }
        _content.Measure(availableSize);
        return _content.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_projected) SetContentDimensions(finalSize);
        _content.Arrange(new Rect(finalSize));
        if (_projected)
        {
            _viewport.Arrange(new Rect(finalSize));
            UpdateCamera(finalSize);
            // Parent rotation can reuse the live UI texture. Stay within the
            // software renderer's cache limit as well as hardware limits.
            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            _plane.CacheMode = finalSize.Width * dpi.DpiScaleX <= 2048 &&
                finalSize.Height * dpi.DpiScaleY <= 2048 ? _contentCache : null;
        }
        ProjectionChanged?.Invoke(this, EventArgs.Empty);
        return finalSize;
    }

    private static object ClampFinite(object value, double limit)
        => value is double number && double.IsFinite(number) ? Math.Clamp(number, -limit, limit) : 0.0;

    private static void OnModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((PerspectiveSurface)d).SwitchHosting((bool)e.NewValue);

    private static void OnPoseChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var surface = (PerspectiveSurface)d;
        if (!surface._batchPose) surface.UpdatePose();
    }

    private void SwitchHosting(bool projected)
    {
        if (_projected == projected) return;
        if (projected)
        {
            RemoveVisualChild(_content);
            _projected = true;
            AddVisualChild(_viewport);
            _plane.Visual = _content;
            UpdatePose();
        }
        else
        {
            _plane.Visual = null;
            RemoveVisualChild(_viewport);
            _projected = false;
            _content.ClearValue(WidthProperty);
            _content.ClearValue(HeightProperty);
            AddVisualChild(_content);
        }
        InvalidateMeasure();
        ProjectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetContentDimensions(Size size)
    {
        if (double.IsFinite(size.Width)) _content.Width = Math.Max(0, size.Width);
        if (double.IsFinite(size.Height)) _content.Height = Math.Max(0, size.Height);
    }

    private void UpdateCamera(Size size)
    {
        if (size.Width <= 0 || size.Height <= 0 || size == _cameraSize) return;
        _cameraSize = size;
        double halfWidth = size.Width / 2;
        double halfHeight = size.Height / 2;
        _focalLength = Math.Max(size.Width, size.Height) * 2.5;

        // Fit the complete permitted pose envelope once per size, rather than
        // zooming at every angle. This leaves a fixed margin for the small
        // lateral motion and prevents edges clipping or breathing as it tilts.
        double yaw = MaximumYaw * Math.PI / 180;
        double pitch = MaximumPitch * Math.PI / 180;
        double depth = halfWidth * Math.Sin(yaw) + halfHeight * Math.Sin(pitch);
        double extentY = halfHeight + halfWidth * Math.Sin(yaw) * Math.Sin(pitch);
        double inset = Math.Min(MaximumOffset + 1, Math.Min(halfWidth, halfHeight) / 4);
        double fit = Math.Max(halfWidth / (halfWidth - inset), extentY / (halfHeight - inset));
        _cameraDistance = _focalLength * fit + depth;
        _camera.FieldOfView = 2 * Math.Atan(halfWidth / _focalLength) * 180 / Math.PI;
        _camera.Position = new Point3D(0, 0, _cameraDistance);
        _camera.NearPlaneDistance = Math.Max(0.01, _cameraDistance - depth - 1);
        _camera.FarPlaneDistance = _cameraDistance + depth + 1;

        var mesh = new MeshGeometry3D
        {
            Positions = new Point3DCollection
            {
                new(-halfWidth, halfHeight, 0), new(-halfWidth, -halfHeight, 0),
                new(halfWidth, -halfHeight, 0), new(halfWidth, halfHeight, 0)
            },
            TextureCoordinates = new PointCollection { new(0, 0), new(0, 1), new(1, 1), new(1, 0) },
            TriangleIndices = new Int32Collection { 0, 1, 2, 0, 2, 3 }
        };
        mesh.Freeze();
        _plane.Geometry = mesh;
    }

    private void UpdatePose()
    {
        var matrix = Matrix3D.Identity;
        matrix.Rotate(new Quaternion(new Vector3D(0, 1, 0), Yaw));
        matrix.Rotate(new Quaternion(new Vector3D(1, 0, 0), Pitch));
        _rotation.Matrix = matrix;
        _offset.X = OffsetX;
        _offset.Y = OffsetY;
        ProjectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Maps an original content point to this host, including perspective.</summary>
    internal Point ProjectPoint(Point point)
    {
        if (!_projected || _focalLength <= 0) return point;
        double halfWidth = ContentSize.Width / 2;
        double halfHeight = ContentSize.Height / 2;
        Point3D rotated = _rotation.Transform(new Point3D(point.X - halfWidth, halfHeight - point.Y, 0));
        double scale = _focalLength / (_cameraDistance - rotated.Z);
        return new Point(halfWidth + rotated.X * scale + OffsetX, halfHeight - rotated.Y * scale + OffsetY);
    }

    /// <summary>Inverse plane mapping without WPF's expensive 3D hit-test traversal.</summary>
    internal bool TryUnprojectPoint(Point projected, out Point content)
    {
        content = projected;
        if (!double.IsFinite(projected.X) || !double.IsFinite(projected.Y)) return false;
        if (!_projected || _focalLength <= 0) return true;
        double halfWidth = ContentSize.Width / 2, halfHeight = ContentSize.Height / 2;
        double x = (projected.X - halfWidth - OffsetX) / _focalLength;
        double y = (halfHeight - projected.Y + OffsetY) / _focalLength;
        Matrix3D m = _rotation.Matrix;
        double a = m.M11 + x * m.M13, b = m.M21 + x * m.M23;
        double c = m.M12 + y * m.M13, d = m.M22 + y * m.M23;
        double determinant = a * d - b * c;
        if (Math.Abs(determinant) < 1e-10) return false;
        double u = _cameraDistance * (x * d - b * y) / determinant;
        double v = _cameraDistance * (a * y - x * c) / determinant;
        content = new Point(halfWidth + u, halfHeight - v);
        return double.IsFinite(content.X) && double.IsFinite(content.Y);
    }

    /// <summary>The matching rounded outline for an independent hollow shadow.</summary>
    internal Geometry ProjectedOutline(double cornerRadius = 12)
    {
        Size size = ContentSize;
        if (size.Width <= 0 || size.Height <= 0) return Geometry.Empty;
        double radius = Math.Clamp(cornerRadius, 0, Math.Min(size.Width, size.Height) / 2);
        if (!_projected) return new RectangleGeometry(new Rect(size), radius, radius);
        var outline = new StreamGeometry();
        using (StreamGeometryContext context = outline.Open())
        {
            bool first = true;
            AddCorner(radius, radius, 180);
            AddCorner(size.Width - radius, radius, 270);
            AddCorner(size.Width - radius, size.Height - radius, 0);
            AddCorner(radius, size.Height - radius, 90);

            void AddCorner(double centerX, double centerY, double startAngle)
            {
                for (int step = 0; step <= 8; step++)
                {
                    double angle = (startAngle + step * 90.0 / 8) * Math.PI / 180;
                    Point point = ProjectPoint(new Point(centerX + radius * Math.Cos(angle), centerY + radius * Math.Sin(angle)));
                    if (first) { context.BeginFigure(point, true, true); first = false; }
                    else context.LineTo(point, true, false);
                }
            }
        }
        outline.Freeze();
        return outline;
    }
}
