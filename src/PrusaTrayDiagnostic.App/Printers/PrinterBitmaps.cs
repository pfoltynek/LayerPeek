using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace PrusaTrayDiagnostic.App.Printers;

/// <summary>
/// Bitmaps of the printer illustrations (T-039), loaded from the bundled assets on first use and kept for the lifetime of
/// the app (a few kB each), plus decoding of custom pictures. UI thread only (the cache is not locked).
/// </summary>
internal static class PrinterBitmaps
{
    private static readonly Dictionary<(string Id, int Size), Bitmap> Illustrations = [];

    /// <summary>The illustration <paramref name="id"/> pre-rendered at <paramref name="pixelSize"/> (one of <see cref="PrinterIllustrations.PixelSizes"/>).</summary>
    public static Bitmap Illustration(string id, int pixelSize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (!Illustrations.TryGetValue((id, pixelSize), out Bitmap? bitmap))
        {
            using Stream stream = AssetLoader.Open(PrinterIllustrations.AssetUri(id, pixelSize));
            bitmap = new Bitmap(stream);
            Illustrations[(id, pixelSize)] = bitmap;
        }

        return bitmap;
    }

    /// <summary>The illustration of <paramref name="picture"/> for <paramref name="sizeInDip"/> at <paramref name="scaling"/>.</summary>
    public static Bitmap Illustration(PrinterPicture picture, double sizeInDip, double scaling)
    {
        ArgumentNullException.ThrowIfNull(picture);
        return Illustration(picture.IllustrationId, PrinterIllustrations.SelectPixelSize(sizeInDip, scaling));
    }

    /// <summary>A custom picture (stored PNG); the caller disposes it. <see langword="null"/> when it cannot be decoded.</summary>
    public static Bitmap? DecodeCustom(byte[] png)
    {
        ArgumentNullException.ThrowIfNull(png);
        try
        {
            using MemoryStream stream = new(png, writable: false);
            return new Bitmap(stream);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The store hands out only PNGs it checked; a damaged one shows the generic illustration instead.
            return null;
        }
    }

    /// <summary>
    /// A custom picture fitted into a square of <paramref name="pixelSize"/> (aspect ratio kept, centred, transparent
    /// bands), e.g. for a menu icon whose bitmap the platform scales as a whole. The caller disposes it.
    /// </summary>
    public static Bitmap? CustomSquare(byte[] png, int pixelSize)
    {
        using Bitmap? decoded = DecodeCustom(png);
        if (decoded is null)
        {
            return null;
        }

        RenderTargetBitmap square = new(new PixelSize(pixelSize, pixelSize), new Vector(96, 96));
        using (Avalonia.Media.DrawingContext context = square.CreateDrawingContext())
        {
            Rect destination = Fit(decoded.Size, new Rect(0, 0, pixelSize, pixelSize));
            using (context.PushRenderOptions(new Avalonia.Media.RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
            {
                context.DrawImage(decoded, new Rect(decoded.Size), destination);
            }
        }

        return square;
    }

    /// <summary>The largest rectangle with the aspect ratio of <paramref name="content"/>, centred in <paramref name="bounds"/>.</summary>
    public static Rect Fit(Size content, Rect bounds)
    {
        if (content.Width <= 0 || content.Height <= 0 || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return default;
        }

        double scale = Math.Min(bounds.Width / content.Width, bounds.Height / content.Height);
        Size size = new(content.Width * scale, content.Height * scale);
        return new Rect(bounds.X + ((bounds.Width - size.Width) / 2), bounds.Y + ((bounds.Height - size.Height) / 2), size.Width, size.Height);
    }
}
