using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KeeStashDock.Desktop.Views;

/// <summary>
/// QR transfer: renders a snippet's text as a QR code so a phone
/// camera can carry it away without any pairing or network setup.
/// The module matrix from the built-in <see cref="QrEncoder"/> (zero
/// external dependencies - builds offline) is rasterized straight into a
/// WriteableBitmap - no System.Drawing round trip, DPI-crisp at any scale.
/// Practical payload ceiling is enforced at 1800 characters (QR version 40
/// tops out near 2.9 KB in byte mode; dense codes also scan poorly).
/// </summary>
public sealed class QrWindow : Window
{
    public const int MaxChars = 1800;

    public QrWindow(string text)
    {
        Localization.LocalizationService.Instance.ApplyFlowDirection(this);
        var loc = Localization.LocalizationService.Instance;
        Title = loc["Qr.Title"];
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "B.WindowBg");
        SourceInitialized += (_, _) => ThemeManager.ApplyWindowChrome(this);

        bool[,] modules = QrEncoder.Encode(text);
        var qrImage = new Image
        {
            Source = Render(modules, scale: 6),
            Width = 320, Height = 320,
            SnapsToDevicePixels = true,
        };
        RenderOptions.SetBitmapScalingMode(qrImage, BitmapScalingMode.NearestNeighbor);

        var hint = new TextBlock
        {
            Text = loc["Qr.Hint"],
            Margin = new Thickness(0, 10, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 320,
        };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "B.TextMuted");

        // QR modules must stay dark-on-light regardless of app theme, so the
        // code sits on its own white plate.
        var plate = new Border
        {
            Background = Brushes.White,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(24),   // >= 4-module quiet zone at scale 6
            Child = qrImage,
        };

        var stack = new StackPanel { Margin = new Thickness(20) };
        stack.Children.Add(plate);
        stack.Children.Add(hint);
        Content = stack;
    }

    private static WriteableBitmap Render(bool[,] modules, int scale)
    {
        int n = modules.GetLength(0), size = n * scale;
        var wb = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
        var pixels = new int[size * size];
        const int black = unchecked((int)0xFF000000), white = unchecked((int)0xFFFFFFFF);

        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                int color = modules[y, x] ? black : white;
                for (int dy = 0; dy < scale; dy++)
                {
                    int baseIdx = (y * scale + dy) * size + x * scale;
                    for (int dx = 0; dx < scale; dx++)
                        pixels[baseIdx + dx] = color;
                }
            }
        }
        wb.WritePixels(new Int32Rect(0, 0, size, size), pixels, size * 4, 0);
        wb.Freeze();
        return wb;
    }
}
