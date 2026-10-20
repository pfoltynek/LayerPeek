using System.Buffers.Binary;

using PrusaTrayDiagnostic.Core.Images;

namespace PrusaTrayDiagnostic.App.Printers;

/// <summary>
/// Reads what the custom image import needs before decoding (T-039): the pixel size from the PNG / JPEG header (to refuse
/// images whose pixels would not fit in memory and to decode a large JPEG already scaled down) and the EXIF orientation
/// of a JPEG (photos from phones store the pixels rotated and say so in the metadata, which the decoder ignores).
/// Pure parsing with bounds checks; nothing here allocates or throws on malformed data.
/// </summary>
internal static class ImageHeaders
{
    /// <summary>EXIF orientation: the pixels are stored as they should be shown.</summary>
    public const int NormalOrientation = 1;

    private const byte MarkerPrefix = 0xFF;
    private const byte StartOfScan = 0xDA;
    private const byte EndOfImage = 0xD9;
    private const byte App1 = 0xE1;
    private const ushort OrientationTag = 0x0112;
    private const ushort ShortType = 3;

    private static ReadOnlySpan<byte> ExifHeader => "Exif\0\0"u8;

    /// <summary>Width and height from the PNG <c>IHDR</c> chunk or the JPEG start-of-frame marker.</summary>
    public static bool TryReadSize(ReadOnlySpan<byte> data, PrinterImageFormat format, out int width, out int height) =>
        format == PrinterImageFormat.Png ? TryReadPngSize(data, out width, out height) : TryReadJpegSize(data, out width, out height);

    /// <summary>The EXIF orientation of a JPEG (1–8), or <see cref="NormalOrientation"/> when it has none or it is invalid.</summary>
    public static int ReadJpegOrientation(ReadOnlySpan<byte> data)
    {
        foreach ((byte marker, int start, int length) in JpegSegments(data))
        {
            if (marker != App1)
            {
                continue;
            }

            ReadOnlySpan<byte> payload = data.Slice(start, length);
            if (payload.StartsWith(ExifHeader) && ReadTiffOrientation(payload[ExifHeader.Length..]) is { } orientation)
            {
                return orientation;
            }
        }

        return NormalOrientation;
    }

    private static bool TryReadPngSize(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = height = 0;
        // Signature (8), chunk length (4), "IHDR" (4), width (4), height (4).
        if (data.Length < 24 || !data.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            return false;
        }

        uint w = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(16, 4));
        uint h = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(20, 4));
        if (w is 0 or > int.MaxValue || h is 0 or > int.MaxValue)
        {
            return false;
        }

        width = (int)w;
        height = (int)h;
        return true;
    }

    private static bool TryReadJpegSize(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = height = 0;
        foreach ((byte marker, int start, int length) in JpegSegments(data))
        {
            // SOF0–SOF15 except DHT (C4), JPG (C8) and DAC (CC): precision (1), height (2), width (2).
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                if (length < 5)
                {
                    return false;
                }

                height = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(start + 1, 2));
                width = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(start + 3, 2));
                return width > 0 && height > 0;
            }
        }

        return false;
    }

    /// <summary>The marker segments before the image data: (marker, payload start, payload length).</summary>
    private static List<(byte Marker, int Start, int Length)> JpegSegments(ReadOnlySpan<byte> data)
    {
        List<(byte, int, int)> segments = [];
        int position = 2; // After SOI (FF D8).
        while (position + 4 <= data.Length)
        {
            if (data[position] != MarkerPrefix)
            {
                break;
            }

            byte marker = data[position + 1];
            if (marker == MarkerPrefix)
            {
                // Fill byte.
                position++;
                continue;
            }

            if (marker is StartOfScan or EndOfImage)
            {
                break;
            }

            if (marker is >= 0xD0 and <= 0xD7 or 0x01)
            {
                // Markers without a length.
                position += 2;
                continue;
            }

            int length = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(position + 2, 2));
            if (length < 2 || position + 2 + length > data.Length)
            {
                break;
            }

            segments.Add((marker, position + 4, length - 2));
            position += 2 + length;
        }

        return segments;
    }

    /// <summary>Tag 0x0112 (SHORT) of IFD0 in a TIFF structure, little- or big-endian.</summary>
    private static int? ReadTiffOrientation(ReadOnlySpan<byte> tiff)
    {
        if (tiff.Length < 8)
        {
            return null;
        }

        bool little = tiff[0] == (byte)'I' && tiff[1] == (byte)'I';
        bool big = tiff[0] == (byte)'M' && tiff[1] == (byte)'M';
        if ((!little && !big) || Read16(tiff, 2, little) != 42)
        {
            return null;
        }

        uint ifd = Read32(tiff, 4, little);
        if (ifd > (uint)(tiff.Length - 2))
        {
            return null;
        }

        int count = Read16(tiff, (int)ifd, little);
        for (int i = 0; i < count; i++)
        {
            int entry = (int)ifd + 2 + (i * 12);
            if (entry + 12 > tiff.Length)
            {
                return null;
            }

            if (Read16(tiff, entry, little) == OrientationTag && Read16(tiff, entry + 2, little) == ShortType)
            {
                int value = Read16(tiff, entry + 8, little);
                return value is >= 1 and <= 8 ? value : null;
            }
        }

        return null;
    }

    private static ushort Read16(ReadOnlySpan<byte> data, int offset, bool little) =>
        little ? BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2)) : BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));

    private static uint Read32(ReadOnlySpan<byte> data, int offset, bool little) =>
        little ? BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4)) : BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));
}
