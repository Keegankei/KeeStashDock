using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KeeStashDock.Desktop.Views;
using Microsoft.Data.Sqlite;

namespace KeeStashDock.Desktop;

/// <summary>
/// Explicit release-tool entry point. Exercises the actual packaged process
/// before single-instance IPC, hooks, settings, user data or tray initialization.
/// </summary>
internal static class PackageVerification
{
    internal const string Argument = "--verify-package";

    internal static async Task<int> RunAsync(string[] args)
    {
        if (args.Length is < 2 or > 3 || (args.Length == 3 && args[2] != "--installed-ocr")) return 2;
        bool installedOcr = args.Length == 3;
        var report = new Dictionary<string, object?>
        {
            ["Version"] = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3),
            ["BaseDirectory"] = AppContext.BaseDirectory,
            ["ProcessPath"] = Environment.ProcessPath,
            ["RenderTier"] = RenderCapability.Tier >> 16,
            ["OcrMode"] = installedOcr ? "Installed" : "Optional",
            ["Success"] = false
        };
        int exitCode = 1;
        try
        {
            using (var connection = new SqliteConnection("Data Source=:memory:"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT sqlite_version()";
                report["SqliteVersion"] = command.ExecuteScalar();
            }
            string[] locales = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Locales"), "*.json");
            foreach (string locale in locales)
                using (JsonDocument.Parse(await File.ReadAllTextAsync(locale))) { }
            if (locales.Length < 20) throw new InvalidDataException("Missing packaged locales.");
            report["LocaleCount"] = locales.Length;

            var surface = new PerspectiveSurface
            {
                IsPerspectiveEnabled = true, Yaw = 8, Pitch = -5,
                Child = new Border { Background = Brushes.White, Child = new TextBlock
                    { Text = "Package rendering", Foreground = Brushes.Black, FontSize = 28, Margin = new Thickness(20) } }
            };
            var size = new Size(400, 160);
            surface.Measure(size);
            surface.Arrange(new Rect(size));
            surface.UpdateLayout();
            var bitmap = new RenderTargetBitmap(400, 160, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(surface);
            byte[] pixels = new byte[400 * 160 * 4];
            bitmap.CopyPixels(pixels, 400 * 4, 0);
            int dark = 0, light = 0;
            for (int i = 0; i < pixels.Length; i += 4)
            {
                if (pixels[i + 3] < 240) continue;
                if (pixels[i] < 32 && pixels[i + 1] < 32 && pixels[i + 2] < 32) dark++;
                if (pixels[i] > 240 && pixels[i + 1] > 240 && pixels[i + 2] > 240) light++;
            }
            if (dark < 50 || light < 1000) throw new InvalidDataException("Packaged WPF rendering failed.");
            report["ProjectedTextRendered"] = true;

            var drawing = new DrawingVisual();
            using (DrawingContext context = drawing.RenderOpen())
            {
                context.DrawRectangle(Brushes.White, null, new Rect(0, 0, 640, 120));
                context.DrawText(new FormattedText("KeeStashDock 122", CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Arial"), 48, Brushes.Black, 1), new Point(24, 24));
            }
            var ocrImage = new RenderTargetBitmap(640, 120, 96, 96, PixelFormats.Pbgra32);
            ocrImage.Render(drawing);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(ocrImage));
            using var stream = new MemoryStream();
            png.Save(stream);
            var ocr = new LocalOcrService(AppContext.BaseDirectory, TimeSpan.FromSeconds(30));
            OcrRecognitionResult recognized = await ocr.RecognizeAsync(stream.ToArray());
            if (installedOcr)
            {
                if (!recognized.IsSuccess || !recognized.Text.Contains("122", StringComparison.Ordinal))
                    throw new InvalidDataException("Installed OCR recognition failed: " + recognized.Failure);
                report["SyntheticOcrRecognized"] = true;
            }
            else
            {
                // The verifier intentionally ignores any per-user installation.
                // A fresh package must fail clearly without starting a download.
                if (recognized.Failure != OcrFailure.MissingModels ||
                    Directory.Exists(Path.Combine(AppContext.BaseDirectory, "x64")) ||
                    Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "OcrModels"), "*.traineddata").Any() ||
                    Directory.Exists(Path.Combine(AppContext.BaseDirectory, "OfflineTranslation", "runtime")))
                    throw new InvalidDataException("A fresh package must not preinstall optional OCR or translation components.");
                report["MissingOcrHandled"] = true;
                report["OptionalComponentsAbsent"] = true;
            }
            report["Success"] = true;
            exitCode = 0;
        }
        catch (Exception error) { report["Error"] = error.ToString(); }
        try { await File.WriteAllTextAsync(Path.GetFullPath(args[1]), JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = true })); }
        catch { return 2; }
        return exitCode;
    }
}
