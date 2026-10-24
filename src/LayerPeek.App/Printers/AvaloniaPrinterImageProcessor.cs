using System.Runtime.InteropServices;

using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

using LayerPeek.Core.Images;

namespace LayerPeek.App.Printers;

/// <summary>
/// <see cref="IPrinterImageProcessor"/> with the Avalonia imaging API (Skia underneath; no extra package, no reflection,
/// Native AOT safe). Runs on the thread pool. The pixel size is read from the header first: an image whose pixels would
/// not fit in memory is refused before decoding, and a large JPEG is decoded already scaled down (Skia decodes JPEG at
/// 1/2, 1/4 or 1/8 directly). The longer side ends at most at the limit, smaller images keep their size (never enlarged),
/// the EXIF orientation (JPEG APP1, PNG eXIf) is applied to the pixels, and the result is a new PNG: no metadata of the source (EXIF, GPS,
/// ICC profile) is copied. Needs the Avalonia platform (the app, or a headless session in tests).
/// </summary>
internal sealed class AvaloniaPrinterImageProcessor : IPrinterImageProcessor
{
    /// <summary>A PNG is decoded at full size: 40 megapixels need 160 MB, more is refused.</summary>
    public const long MaxPngPixels = 40_000_000;

    /// <summary>A JPEG is decoded scaled down to at least 1/8 per side; up to 400 megapixels (≤ 25 MB of pixels at 1/8).</summary>
    public const long MaxJpegPixels = 400_000_000;

    public static AvaloniaPrinterImageProcessor Instance { get; } = new();

    public Task<byte[]> ConvertToPngAsync(ReadOnlyMemory<byte> source, PrinterImageFormat format, int maxPixelSize, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPixelSize);
        return Task.Run(() => Convert(source, format, maxPixelSize, cancellationToken), cancellationToken);
    }

    private static byte[] Convert(ReadOnlyMemory<byte> source, PrinterImageFormat format, int maxPixelSize, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!ImageHeaders.TryReadSize(source.Span, format, out int width, out int height))
        {
            throw new PrinterImageDecodeException("The image header cannot be read.");
        }

        if ((long)width * height > (format == PrinterImageFormat.Png ? MaxPngPixels : MaxJpegPixels))
        {
            throw new PrinterImageDecodeException("The image has too many pixels.");
        }

        int orientation = ImageHeaders.ReadOrientation(source.Span, format);
        try
        {
            using Bitmap decoded = Decode(source, format, width, height, maxPixelSize);
            cancellationToken.ThrowIfCancellationRequested();
            using MemoryStream png = new();
            if (orientation == ImageHeaders.NormalOrientation)
            {
                decoded.Save(png, new PngBitmapEncoderOptions());
            }
            else
            {
                using WriteableBitmap upright = Orient(decoded, orientation);
                upright.Save(png, new PngBitmapEncoderOptions());
            }

            return png.ToArray();
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not PrinterImageDecodeException)
        {
            // Corrupt or truncated data, an unsupported variant (e.g. CMYK), out of memory: the image cannot be used.
            throw new PrinterImageDecodeException("The image cannot be decoded.", ex);
        }
    }

    /// <summary>
    /// Decodes with the longer side at most <paramref name="maxPixelSize"/>, keeping the aspect ratio (the shorter side is
    /// rounded, at least 1 px). A large JPEG is first decoded scaled down to about twice the target (Skia decodes JPEG at
    /// 1/2, 1/4 or 1/8 directly), so that a photo never needs its full pixels in memory; the last step scales smoothly.
    /// </summary>
    private static Bitmap Decode(ReadOnlyMemory<byte> source, PrinterImageFormat format, int width, int height, int maxPixelSize)
    {
        using Stream stream = Open(source);
        int longer = Math.Max(width, height);
        if (longer <= maxPixelSize)
        {
            return new Bitmap(stream);
        }

        double scale = (double)maxPixelSize / longer;
        PixelSize target = new(Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
        int intermediate = 2 * maxPixelSize;
        // The shorter side of the intermediate must stay at least a few pixels (Avalonia truncates it; 0 would fail).
        bool scaledDecode = format == PrinterImageFormat.Jpeg && longer > intermediate && (double)Math.Min(width, height) * intermediate / longer >= 4;
        using Bitmap decoded = !scaledDecode ? new Bitmap(stream)
            : width >= height ? Bitmap.DecodeToWidth(stream, intermediate, BitmapInterpolationMode.HighQuality)
            : Bitmap.DecodeToHeight(stream, intermediate, BitmapInterpolationMode.HighQuality);
        return decoded.CreateScaledBitmap(target, BitmapInterpolationMode.HighQuality);
    }

    private static MemoryStream Open(ReadOnlyMemory<byte> source) =>
        MemoryMarshal.TryGetArray(source, out ArraySegment<byte> segment)
            ? new MemoryStream(segment.Array!, segment.Offset, segment.Count, writable: false)
            : new MemoryStream(source.ToArray(), writable: false);

    /// <summary>The upright copy of <paramref name="bitmap"/> for an EXIF orientation other than 1.</summary>
    private static WriteableBitmap Orient(Bitmap bitmap, int orientation)
    {
        PixelSize size = bitmap.PixelSize;
        PixelFormat format = bitmap.Format ?? PixelFormat.Bgra8888;
        AlphaFormat alpha = bitmap.AlphaFormat ?? AlphaFormat.Premul;
        if (format.BitsPerPixel != 32)
        {
            throw new NotSupportedException("Only 32-bit pixels can be oriented.");
        }

        uint[] pixels = new uint[size.Width * size.Height];
        GCHandle pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(size), pinned.AddrOfPinnedObject(), pixels.Length * sizeof(uint), size.Width * sizeof(uint));
        }
        finally
        {
            pinned.Free();
        }

        uint[] upright = PixelOrientation.Apply(pixels, size.Width, size.Height, orientation);
        PixelSize uprightSize = PixelOrientation.SwapsAxes(orientation) ? new PixelSize(size.Height, size.Width) : size;
        WriteableBitmap result = new(uprightSize, new Vector(96, 96), format, alpha);
        try
        {
            using ILockedFramebuffer frame = result.Lock();
            byte[] row = new byte[uprightSize.Width * sizeof(uint)];
            for (int y = 0; y < uprightSize.Height; y++)
            {
                Buffer.BlockCopy(upright, y * row.Length, row, 0, row.Length);
                Marshal.Copy(row, 0, frame.Address + (y * frame.RowBytes), row.Length);
            }

            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }
}
