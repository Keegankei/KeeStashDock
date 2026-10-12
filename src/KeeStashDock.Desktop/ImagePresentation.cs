using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KeeStashDock.Desktop;

/// <summary>WPF image decoding and bounded card previews; originals stay in storage.</summary>
internal static class ImagePresentation
{
    internal static BitmapSource Decode(byte[] png, int decodeWidth = 0)
    {
        using var stream = new MemoryStream(png, writable: false);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        if (decodeWidth > 0) image.DecodePixelWidth = decodeWidth;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    internal static byte[] Thumbnail(byte[] png)
    {
        var source = Decode(png);
        double scale = Math.Min(1, 320d / Math.Max(source.PixelWidth, source.PixelHeight));
        BitmapSource thumbnail = scale < 1
            ? new TransformedBitmap(source, new ScaleTransform(scale, scale)) : source;
        thumbnail = new FormatConvertedBitmap(thumbnail, PixelFormats.Bgr24, null, 0);
        var encoder = new PngBitmapEncoder { Interlace = PngInterlaceOption.Off };
        encoder.Frames.Add(BitmapFrame.Create(thumbnail));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    internal static void Copy(byte[] png)
    {
        var data = new DataObject();
        data.SetImage(Decode(png));
        using var stream = new MemoryStream(png, writable: false);
        data.SetData("PNG", stream);
        Clipboard.SetDataObject(data, copy: true);
    }
}
