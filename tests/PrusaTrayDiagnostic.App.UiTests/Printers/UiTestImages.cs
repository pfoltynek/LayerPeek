using System.Runtime.InteropServices;

using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

using PrusaTrayDiagnostic.App.Tests.Printers;

using SkiaSharp;

namespace PrusaTrayDiagnostic.App.UiTests.Printers;

/// <summary>Real PNG and JPEG data for the image tests (T-039), and reading pixels back. Needs the Avalonia platform.</summary>
internal static class UiTestImages
{
    /// <summary>A PNG whose pixel (x, y) is <paramref name="color"/>(x, y) as 0xAARRGGBB.</summary>
    public static byte[] Png(int width, int height, Func<int, int, uint> color)
    {
        using WriteableBitmap bitmap = new(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using (ILockedFramebuffer frame = bitmap.Lock())
        {
            uint[] row = new uint[width];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    row[x] = color(x, y);
                }

                Marshal.Copy(MemoryMarshal.AsBytes(row.AsSpan()).ToArray(), 0, frame.Address + (y * frame.RowBytes), width * 4);
            }
        }

        using MemoryStream png = new();
        bitmap.Save(png, new PngBitmapEncoderOptions());
        return png.ToArray();
    }

    public static byte[] Png(int width, int height, uint color) => Png(width, height, (_, _) => color);

    /// <summary>A JPEG (quality 95) with an EXIF orientation segment when <paramref name="orientation"/> is set.</summary>
    public static byte[] Jpeg(int width, int height, Func<int, int, uint> color, int? orientation = null)
    {
        using SKBitmap bitmap = new(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bitmap.SetPixel(x, y, new SKColor(color(x, y)));
            }
        }

        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Jpeg, 95);
        byte[] jpeg = data.ToArray();
        if (orientation is not { } value)
        {
            return jpeg;
        }

        // Right after SOI, like a camera writes it.
        return [.. jpeg.AsSpan(0, 2), .. TestImages.ExifSegment(value, littleEndian: true), .. jpeg.AsSpan(2)];
    }

    /// <summary>Decodes <paramref name="png"/> and returns its size and pixels as 0xAARRGGBB (unpremultiplied).</summary>
    public static (int Width, int Height, uint[] Pixels) Read(byte[] png)
    {
        using MemoryStream stream = new(png);
        using Bitmap decoded = new(stream);
        PixelSize size = decoded.PixelSize;
        uint[] pixels = new uint[size.Width * size.Height];
        GCHandle pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            decoded.CopyPixels(new PixelRect(size), pinned.AddrOfPinnedObject(), pixels.Length * 4, size.Width * 4);
        }
        finally
        {
            pinned.Free();
        }

        bool rgba = decoded.Format == PixelFormat.Rgba8888;
        bool premultiplied = decoded.AlphaFormat is null or AlphaFormat.Premul;
        for (int i = 0; i < pixels.Length; i++)
        {
            uint p = pixels[i];
            uint a = p >> 24;
            uint c0 = (p >> 16) & 0xFF;
            uint c1 = (p >> 8) & 0xFF;
            uint c2 = p & 0xFF;
            if (rgba)
            {
                // Stored R, G, B, A: as a little-endian uint that is 0xAABBGGRR.
                (c0, c2) = (c2, c0);
            }

            if (premultiplied && a is > 0 and < 255)
            {
                c0 = Math.Min(255, c0 * 255 / a);
                c1 = Math.Min(255, c1 * 255 / a);
                c2 = Math.Min(255, c2 * 255 / a);
            }

            pixels[i] = (a << 24) | (c0 << 16) | (c1 << 8) | c2;
        }

        return (size.Width, size.Height, pixels);
    }

    /// <summary>Whether <paramref name="argb"/> is close to <paramref name="expected"/> (JPEG and scaling shift colours a little).</summary>
    public static bool IsNear(uint argb, uint expected, int tolerance = 40)
    {
        for (int shift = 0; shift < 32; shift += 8)
        {
            if (Math.Abs((int)((argb >> shift) & 0xFF) - (int)((expected >> shift) & 0xFF)) > tolerance)
            {
                return false;
            }
        }

        return true;
    }
}
