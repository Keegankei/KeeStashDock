using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using KeeStashDock.Desktop.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeeStashDock.Desktop.Tests;

[TestClass]
public sealed class PerspectiveSurfaceTests
{
    [STATestMethod]
    public void DisabledProjectionPreservesOrdinaryLayoutAndPixels()
    {
        var original = new Border { Background = new SolidColorBrush(Color.FromArgb(93, 120, 180, 240)), CornerRadius = new CornerRadius(12) };
        var wrapped = new Border { Background = original.Background, CornerRadius = original.CornerRadius };
        var surface = new PerspectiveSurface { Child = wrapped, Yaw = 12, Pitch = 9, OffsetX = 4 };
        var size = new Size(320, 240);
        Layout(original, size);
        Layout(surface, size);
        Assert.AreEqual(size, surface.ContentSize);
        Assert.AreEqual(size, wrapped.RenderSize);
        CollectionAssert.AreEqual(RenderPixels(original, size), RenderPixels(surface, size),
            "Classic mode must remain ordinary WPF drawing, including translucent colors and rounded corners.");
        Assert.AreEqual(new Point(73, 92), surface.ProjectPoint(new Point(73, 92)));
        Assert.IsFalse(ContainsViewport(surface));
    }

    [STATestMethod]
    public void ProjectedContentPreservesTextColorsAndCoverageAlpha()
    {
        var content = new Grid { Background = new SolidColorBrush(Color.FromArgb(153, 40, 90, 150)) };
        var text = new TextBlock
        {
            Text = "Projected live text", Foreground = Brushes.Black, Background = Brushes.Yellow,
            FontSize = 24, Width = 260, Height = 45, HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(30, 40, 0, 0)
        };
        content.Children.Add(text);
        var surface = new PerspectiveSurface { Child = content, IsPerspectiveEnabled = true, Yaw = 10, Pitch = -6 };
        var size = new Size(400, 300);
        Layout(surface, size);
        VerifyPixels(RenderPixels(surface, size));
        var snapshot = new DrawingVisual();
        using (DrawingContext drawing = snapshot.RenderOpen())
            drawing.DrawRectangle(new VisualBrush(surface), null, new Rect(size));
        VerifyPixels(RenderPixels(snapshot, size));

        void VerifyPixels(byte[] pixels)
        {
            Point background = surface.ProjectPoint(new Point(300, 220));
            int index = ((int)background.Y * 400 + (int)background.X) * 4;
            // WPF rounds 3D texture color/alpha by one level. Premultiplied
            // coverage is essential: emissive rendering previously returned
            // nonzero RGB with alpha zero, washing out the composed panel.
            foreach (var (channel, expected) in new[] { (0, 90), (1, 54), (2, 24), (3, 153) })
                Assert.IsTrue(Math.Abs(pixels[index + channel] - expected) <= 2,
                    $"Projected background channel {channel} must retain premultiplied color and alpha.");
            int blackText = 0, yellowBackground = 0;
            for (int pixel = 0; pixel < pixels.Length; pixel += 4)
            {
                if (pixels[pixel + 3] < 240) continue;
                if (pixels[pixel] < 32 && pixels[pixel + 1] < 32 && pixels[pixel + 2] < 32) blackText++;
                if (pixels[pixel] < 16 && pixels[pixel + 1] > 240 && pixels[pixel + 2] > 240) yellowBackground++;
            }
            Assert.IsTrue(blackText > 100, "The projected snapshot must contain readable dark glyphs, not an empty plane.");
            Assert.IsTrue(yellowBackground > 1000, "The projected snapshot must retain the actual content colors.");
        }
    }

    [STATestMethod]
    public void PerspectiveCornersStayWithinFixedWindowAndHaveRealDepth()
    {
        var content = new Border();
        var surface = new PerspectiveSurface { Child = content, IsPerspectiveEnabled = true };
        foreach (Size size in new[] { new Size(400, 600), new Size(700, 260), new Size(300, 1000), new Size(400, 44) })
        {
            Layout(surface, size);
            Assert.AreEqual(size, content.RenderSize, "Projection must not reflow the original full-size panel.");
            foreach (double yaw in new[] { -12.0, 0.0, 12.0 })
            foreach (double pitch in new[] { -9.0, 0.0, 9.0 })
            foreach (double offset in new[] { -4.0, 4.0 })
            {
                surface.Yaw = yaw;
                surface.Pitch = pitch;
                surface.OffsetX = offset;
                surface.OffsetY = -offset;
                foreach (Point corner in Corners(size))
                {
                    Point projected = surface.ProjectPoint(corner);
                    Assert.IsTrue(projected.X >= 0 && projected.X <= size.Width && projected.Y >= 0 && projected.Y <= size.Height,
                        $"The projected corner {projected} must stay in {size} at {yaw}/{pitch} degrees.");
                }
            }
        }
        Layout(surface, new Size(400, 600));
        surface.Yaw = 12;
        surface.Pitch = 0;
        Point topLeft = surface.ProjectPoint(new Point(0, 0));
        Point topRight = surface.ProjectPoint(new Point(400, 0));
        Point bottomLeft = surface.ProjectPoint(new Point(0, 600));
        Point bottomRight = surface.ProjectPoint(new Point(400, 600));
        Assert.IsTrue(Math.Abs((bottomLeft.Y - topLeft.Y) - (bottomRight.Y - topRight.Y)) > 10,
            "Yaw must change the near/far edge sizes; a 2D skew or rotation is insufficient.");
        Assert.IsTrue(surface.ProjectedOutline().IsFrozen);
        Assert.IsTrue(surface.ProjectedOutline().FillContains(surface.ProjectPoint(new Point(200, 300))));
    }

    [STATestMethod]
    public void ModeSwitchKeepsLiveChildrenBindingsAndResources()
    {
        var text = new TextBox { Text = "retained editor state" };
        var label = new TextBlock();
        label.SetBinding(TextBlock.TextProperty, new Binding());
        label.SetResourceReference(TextBlock.ForegroundProperty, "LocalColor");
        var content = new StackPanel();
        content.Children.Add(label);
        content.Children.Add(text);
        var surface = new PerspectiveSurface { Child = content, DataContext = "inherited context" };
        surface.Resources["LocalColor"] = Brushes.Orange;
        text.Select(3, 7);
        for (int iteration = 0; iteration < 8; iteration++)
        {
            surface.IsPerspectiveEnabled = iteration % 2 == 0;
            Layout(surface, new Size(400, 600));
            Assert.AreSame(content, surface.Child);
            Assert.AreSame(surface, LogicalTreeHelper.GetParent(LogicalTreeHelper.GetParent(content)));
            Assert.AreEqual("inherited context", label.Text);
            Assert.AreSame(Brushes.Orange, label.Foreground);
            Assert.AreEqual("retained editor state", text.Text);
            Assert.AreEqual(3, text.SelectionStart);
            Assert.AreEqual(7, text.SelectionLength);
            Assert.AreEqual(1, VisualTreeHelper.GetChildrenCount(surface));
            Assert.AreEqual(surface.IsPerspectiveEnabled, ContainsViewport(surface));
        }
        surface.Yaw = double.NaN;
        surface.Pitch = double.PositiveInfinity;
        surface.OffsetX = -double.MaxValue;
        Assert.AreEqual(0.0, surface.Yaw);
        Assert.AreEqual(0.0, surface.Pitch);
        Assert.AreEqual(-PerspectiveSurface.MaximumOffset, surface.OffsetX);
    }

    internal static void VerifyLiveControls()
    {
        var button = new Button { Content = "Synthetic action", Width = 180, Height = 44, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(32, 40, 0, 0) };
        var editor = new TextBox { Text = "Synthetic editable text", Width = 240, Height = 60, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(32, 120, 0, 0) };
        var combo = new ComboBox { Width = 220, Height = 32, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(32, 220, 0, 0) };
        combo.Items.Add("Synthetic first option");
        combo.Items.Add("Synthetic second option");
        combo.SelectedIndex = 0;
        var content = new Grid { Background = Brushes.White };
        content.Children.Add(button);
        content.Children.Add(editor);
        content.Children.Add(combo);
        var surface = new PerspectiveSurface { Child = content, IsPerspectiveEnabled = true, Yaw = 10, Pitch = -6 };
        var window = new Window { Width = 400, Height = 360, Content = surface, ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Left = 80, Top = 80 };
        try
        {
            window.Show();
            window.UpdateLayout();
            PumpRender();
            Point buttonPoint = surface.ProjectPoint(new Point(122, 62));
            Point editorPoint = surface.ProjectPoint(new Point(152, 150));
            Assert.IsTrue(IsWithin(surface.InputHitTest(buttonPoint) as DependencyObject, button),
                "WPF must direct projected pointer coordinates to the real Button.");
            Assert.IsTrue(IsWithin(surface.InputHitTest(editorPoint) as DependencyObject, editor),
                "WPF must direct projected pointer coordinates to the real TextBox.");
            Assert.AreSame(window, Window.GetWindow(editor));
            Point actual = button.PointToScreen(new Point(90, 22));
            Point expected = surface.PointToScreen(buttonPoint);
            Assert.IsTrue((actual - expected).Length < 1,
                "Child screen positions must use the same projection as visuals and hit testing.");
            Rect caret = editor.GetRectFromCharacterIndex(4);
            Assert.IsFalse(caret.IsEmpty);
            Point caretScreen = editor.PointToScreen(caret.BottomLeft);
            Assert.IsTrue(double.IsFinite(caretScreen.X) && double.IsFinite(caretScreen.Y));
            combo.IsDropDownOpen = true;
            PumpRender();
            var popup = (Popup)combo.Template.FindName("PART_Popup", combo);
            Assert.IsTrue(popup.IsOpen, "The existing ComboBox popup must open from the live projected target.");
            Point popupScreen = popup.Child.PointToScreen(new Point());
            Assert.IsTrue(double.IsFinite(popupScreen.X) && double.IsFinite(popupScreen.Y));
            combo.SelectedIndex = 1;
            combo.IsDropDownOpen = false;
            for (int index = 0; index < 4; index++)
            {
                surface.IsPerspectiveEnabled = !surface.IsPerspectiveEnabled;
                window.UpdateLayout();
                PumpRender();
                Assert.AreSame(window, Window.GetWindow(editor));
                Assert.AreEqual("Synthetic editable text", editor.Text);
                Assert.AreEqual(1, combo.SelectedIndex);
            }
        }
        finally { combo.IsDropDownOpen = false; window.Close(); }
    }

    private static bool IsWithin(DependencyObject? current, DependencyObject expected)
    {
        while (current is not null)
        {
            if (ReferenceEquals(current, expected)) return true;
            current = current is Visual or Visual3D ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
        }
        return false;
    }

    private static void PumpRender()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static bool ContainsViewport(PerspectiveSurface surface) => VisualTreeHelper.GetChild(surface, 0) is Viewport3D;
    private static Point[] Corners(Size size) => [new(0, 0), new(size.Width, 0), new(size.Width, size.Height), new(0, size.Height)];
    private static void Layout(FrameworkElement element, Size size)
    {
        element.Measure(size);
        element.Arrange(new Rect(size));
        element.UpdateLayout();
    }

    private static byte[] RenderPixels(Visual element, Size size)
    {
        int width = (int)size.Width, height = (int)size.Height;
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var bytes = new byte[width * height * 4];
        bitmap.CopyPixels(bytes, width * 4, 0);
        return bytes;
    }
}
