using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using KeeStashDock.Desktop.Views;

namespace KeeStashDock.Desktop.Tests;

/// <summary>Records only the supplied synthetic WPF fixture, never desktop pixels or OS input.</summary>
internal static class PerspectivePreviewRecorder
{
    internal static void Record(QuickPanelWindow panel, Action<Point> movePointer, string directory)
    {
        if (!panel.FloatingDepthEnabled) return;
        const int count = 56, delayMilliseconds = 80, padding = 44;
        int width = (int)Math.Ceiling(panel.ActualWidth) + 2 * padding;
        int height = (int)Math.Ceiling(panel.ActualHeight) + 2 * padding;
        var encoder = new GifBitmapEncoder();
        var pointerShape = Geometry.Parse("M 0,0 L 0,18 L 4.5,13.5 L 8,21 L 11,19.5 L 7.5,12 L 14,12 Z");
        pointerShape.Freeze();
        var backdrop = new SolidColorBrush(Color.FromRgb(230, 235, 243));
        backdrop.Freeze();
        var pointerOutline = new Pen(new SolidColorBrush(Color.FromRgb(36, 43, 55)), 1.4);
        pointerOutline.Freeze();
        BitmapPalette? palette = null;

        // Visit nearby desktop edges and each card, so the same recording shows
        // the panel's inward tilt and the separate card proximity response.
        Point[] route =
        [
            new(panel.ActualWidth + 16, 100), new(panel.ActualWidth * 0.7, 175),
            new(panel.ActualWidth * 0.65, 275), new(panel.ActualWidth * 0.3, 380),
            new(-16, 380), new(panel.ActualWidth * 0.4, panel.ActualHeight + 16),
            new(panel.ActualWidth * 0.5, -16),
        ];
        Point Position(int index)
        {
            double segment = index * route.Length / (double)count;
            int start = (int)segment;
            double t = segment - start;
            t = t * t * (3 - 2 * t);
            Point a = route[start], b = route[(start + 1) % route.Length];
            return new Point(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
        }

        movePointer(Position(0));
        panel.UpdatePerspectivePointer();
        Pump(TimeSpan.FromMilliseconds(240));
        for (int index = 0; index < count; index++)
        {
            Point pointer = Position(index);
            movePointer(pointer);
            panel.UpdatePerspectivePointer();
            Pump(TimeSpan.FromMilliseconds(delayMilliseconds));
            panel.UpdateLayout();

            // Direct snapshots avoid nesting the projected live VisualBrush
            // inside a second VisualBrush, which can lose its 3D texture.
            RenderTargetBitmap face = Snapshot(panel);
            var shadow = panel.OwnedWindows.OfType<PanelShadowWindow>().Single();
            RenderTargetBitmap? shade = shadow.IsVisible ? Snapshot(shadow) : null;
            var frame = new DrawingVisual();
            using (DrawingContext drawing = frame.RenderOpen())
            {
                drawing.DrawRectangle(backdrop, null, new Rect(0, 0, width, height));
                if (shade is not null)
                    drawing.DrawImage(shade, new Rect(padding - 32, padding - 32,
                        shadow.ActualWidth, shadow.ActualHeight));
                drawing.DrawImage(face, new Rect(padding, padding, panel.ActualWidth, panel.ActualHeight));
                drawing.PushTransform(new TranslateTransform(pointer.X + padding, pointer.Y + padding));
                drawing.DrawGeometry(Brushes.White, pointerOutline, pointerShape);
                drawing.Pop();
            }
            var rendered = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rendered.Render(frame);
            palette ??= new BitmapPalette(rendered, 256);
            var indexed = new FormatConvertedBitmap(rendered, PixelFormats.Indexed8, palette, 0);
            encoder.Frames.Add(BitmapFrame.Create(indexed));
        }

        using var encoded = new MemoryStream();
        encoder.Save(encoded);
        Directory.CreateDirectory(directory);
        using var output = File.Create(Path.Combine(directory, "floating-panel-motion.gif"));
        WriteAnimationMetadata(encoded.ToArray(), output, delayMilliseconds / 10);
    }

    internal static RenderTargetBitmap Snapshot(FrameworkElement element)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),
            (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        return bitmap;
    }

    private static void Pump(TimeSpan duration)
    {
        var elapsed = Stopwatch.StartNew();
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(8),
        };
        timer.Tick += (_, _) => { if (elapsed.Elapsed >= duration) frame.Continue = false; };
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
    }

    // GifBitmapEncoder writes the image frames but exposes neither global nor
    // frame metadata. Add standard animation blocks around the unchanged WIC
    // image data, rather than relying on ignored BitmapMetadata properties.
    private static void WriteAnimationMetadata(byte[] input, Stream output, int delayCentiseconds)
    {
        if (input.Length < 14 || Encoding.ASCII.GetString(input, 0, 3) != "GIF")
            throw new InvalidDataException("The fixture encoder did not produce a GIF.");
        int position = 13 + ((input[10] & 0x80) == 0 ? 0 : 3 * (1 << ((input[10] & 7) + 1)));
        if (position > input.Length) throw new InvalidDataException("Truncated GIF palette.");
        output.Write(Encoding.ASCII.GetBytes("GIF89a"));
        output.Write(input, 6, position - 6);
        output.Write(new byte[] { 0x21, 0xff, 0x0b });
        output.Write(Encoding.ASCII.GetBytes("NETSCAPE2.0"));
        output.Write(new byte[] { 3, 1, 0, 0, 0 });
        while (position < input.Length)
        {
            int start = position;
            byte marker = input[position++];
            if (marker == 0x3b)
            {
                output.WriteByte(marker);
                return;
            }
            if (marker == 0x21)
            {
                RequireBytes(1);
                byte label = input[position++];
                SkipBlocks();
                if (label != 0xf9) output.Write(input, start, position - start);
            }
            else if (marker == 0x2c)
            {
                RequireBytes(9);
                byte packed = input[position + 8];
                position += 9;
                if ((packed & 0x80) != 0)
                {
                    int paletteLength = 3 * (1 << ((packed & 7) + 1));
                    RequireBytes(paletteLength);
                    position += paletteLength;
                }
                RequireBytes(1);
                position++; // LZW minimum code size.
                SkipBlocks();
                output.Write(new byte[] { 0x21, 0xf9, 4, 8,
                    (byte)delayCentiseconds, (byte)(delayCentiseconds >> 8), 0, 0 });
                output.Write(input, start, position - start);
            }
            else throw new InvalidDataException("Unexpected GIF block in the fixture recording.");
        }
        throw new InvalidDataException("The fixture GIF has no trailer.");

        void RequireBytes(int length)
        {
            if (length > input.Length - position) throw new InvalidDataException("Truncated fixture GIF.");
        }
        void SkipBlocks()
        {
            while (true)
            {
                RequireBytes(1);
                int length = input[position++];
                if (length == 0) return;
                RequireBytes(length);
                position += length;
            }
        }
    }
}
