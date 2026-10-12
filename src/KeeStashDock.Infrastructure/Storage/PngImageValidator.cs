using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;

namespace KeeStashDock.Infrastructure.Storage;

/// <summary>Validates bounded PNG assets before persistence, display, or database import.</summary>
public static class PngImageValidator
{
    public const int MaxImageBytes = 48 * 1024 * 1024;
    public const int MaxThumbnailBytes = 1024 * 1024;
    public const int MaxDimension = 16384;
    public const long MaxPixels = 40_000_000;
    public const int MaxThumbnailDimension = 512;
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly uint[] CrcTable = CreateCrcTable();

    public static string ImageHash(byte[] png)
        => "image:" + Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant();

    public static (int Width, int Height) Validate(byte[] png, bool thumbnail = false)
    {
        ArgumentNullException.ThrowIfNull(png);
        int maxBytes = thumbnail ? MaxThumbnailBytes : MaxImageBytes;
        if (png.Length < 45 || png.Length > maxBytes || !png.AsSpan(0, 8).SequenceEqual(Signature))
            throw new InvalidDataException("The image must be a bounded PNG file.");
        int width = 0, height = 0, channels = 0, offset = 8;
        bool imageDataSeen = false, imageDataEnded = false, ended = false;
        using var compressed = new MemoryStream();
        while (offset <= png.Length - 12)
        {
            uint length = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset));
            if (length > int.MaxValue || (long)offset + length + 12 > png.Length)
                throw new InvalidDataException("The PNG contains a truncated chunk.");
            int count = (int)length;
            ReadOnlySpan<byte> type = png.AsSpan(offset + 4, 4);
            ReadOnlySpan<byte> data = png.AsSpan(offset + 8, count);
            uint crc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset + 8 + count));
            if (Crc(png.AsSpan(offset + 4, count + 4)) != crc)
                throw new InvalidDataException("The PNG checksum is invalid.");
            if (offset == 8 && !type.SequenceEqual("IHDR"u8))
                throw new InvalidDataException("The PNG image header is missing.");
            if (type.SequenceEqual("IHDR"u8))
            {
                if (offset != 8 || count != 13)
                    throw new InvalidDataException("The PNG image header is invalid.");
                uint w = BinaryPrimitives.ReadUInt32BigEndian(data);
                uint h = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
                int maxDimension = thumbnail ? MaxThumbnailDimension : MaxDimension;
                if (w == 0 || h == 0 || w > maxDimension || h > maxDimension || (long)w * h > MaxPixels)
                    throw new InvalidDataException("The image dimensions exceed the supported limit.");
                if (data[8] != 8 || (data[9] != 2 && data[9] != 6) || data[10] != 0 || data[11] != 0 || data[12] != 0)
                    throw new InvalidDataException("Only non-interlaced 8-bit RGB or RGBA PNG images are supported.");
                width = (int)w; height = (int)h; channels = data[9] == 2 ? 3 : 4;
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                if (imageDataEnded) throw new InvalidDataException("The PNG image data is out of order.");
                imageDataSeen = true;
                compressed.Write(data);
            }
            else
            {
                if (imageDataSeen) imageDataEnded = true;
                if (type.SequenceEqual("IEND"u8))
                {
                    if (count != 0 || !imageDataSeen || offset + 12 != png.Length)
                        throw new InvalidDataException("The PNG end marker is invalid.");
                    ended = true;
                    break;
                }
                // Reject unknown critical chunks, and animated PNGs: each entry is one static screenshot.
                if ((type[0] & 32) == 0 && !type.SequenceEqual("PLTE"u8) || type.SequenceEqual("acTL"u8))
                    throw new InvalidDataException("Unsupported PNG chunk.");
            }
            offset += count + 12;
        }
        if (!ended) throw new InvalidDataException("The PNG is incomplete.");
        compressed.Position = 0;
        using var decoded = new ZLibStream(compressed, CompressionMode.Decompress);
        byte[] buffer = new byte[Math.Min(width * channels, 65536)];
        for (int y = 0; y < height; y++)
        {
            int filter = decoded.ReadByte();
            if (filter is < 0 or > 4) throw new InvalidDataException("The PNG scanline is invalid.");
            int remaining = width * channels;
            while (remaining > 0)
            {
                int read = decoded.Read(buffer, 0, Math.Min(buffer.Length, remaining));
                if (read == 0) throw new InvalidDataException("The PNG pixels are truncated.");
                remaining -= read;
            }
        }
        if (decoded.ReadByte() != -1) throw new InvalidDataException("The PNG contains excess pixel data.");
        return (width, height);
    }

    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in bytes) crc = CrcTable[(crc ^ value) & 255] ^ (crc >> 8);
        return ~crc;
    }

    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint value = n;
            for (int k = 0; k < 8; k++) value = (value & 1) != 0 ? 0xedb88320 ^ (value >> 1) : value >> 1;
            table[n] = value;
        }
        return table;
    }
}
