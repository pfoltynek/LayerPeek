using Avalonia.Media.Imaging;

using PrusaTrayDiagnostic.App.Printers;

namespace PrusaTrayDiagnostic.App.Tray.Menu;

/// <summary>
/// Bitmaps for the printer items of the tray menu (T-039, Windows only; macOS gets none until T-018). Avalonia draws the
/// Windows tray menu itself and fits the item icon into 16 × 16 DIP, so the bitmap has 16 DIP worth of physical pixels at
/// <see cref="Scaling"/>: 16 / 20 / 24 / 32 px at 100 / 125 / 150 / 200 %, the small sizes from the simplified art.
/// Illustrations are shared (<see cref="PrinterBitmaps"/>); a custom picture is fitted into a square once and kept per
/// printer: a new picture of the printer releases the previous bitmap, <see cref="Dispose"/> the rest. UI thread only.
/// </summary>
internal sealed class PrinterMenuIcons : IDisposable
{
    /// <summary>Size of the icon in the menu.</summary>
    public const double SizeInDip = 16;

    // One entry per printer id: the picture bytes and their square bitmap (null when they cannot be decoded).
    private readonly Dictionary<string, (byte[] Png, Bitmap? Bitmap)> _custom = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    /// <param name="scaling">Scaling of the screen the menu opens on (Windows: the system scaling).</param>
    public PrinterMenuIcons(double scaling)
    {
        Scaling = double.IsFinite(scaling) && scaling > 0 ? scaling : 1;
        PixelSize = PrinterIllustrations.SelectPixelSize(SizeInDip, Scaling);
    }

    public double Scaling { get; }

    /// <summary>Pixel size of every icon.</summary>
    public int PixelSize { get; }

    /// <summary>
    /// The bitmap of <paramref name="picture"/> of printer <paramref name="printerId"/>; owned by this instance (do not dispose).
    /// A custom picture that cannot be decoded shows its automatic image.
    /// </summary>
    public Bitmap Get(PrinterPicture picture, string? printerId)
    {
        ArgumentNullException.ThrowIfNull(picture);
        ObjectDisposedException.ThrowIf(_disposed, this);
        string key = printerId ?? string.Empty;
        if (picture.CustomPng is { } png)
        {
            if (!_custom.TryGetValue(key, out (byte[] Png, Bitmap? Bitmap) entry) || !ReferenceEquals(entry.Png, png))
            {
                // The menu item gets the new bitmap right away; the replaced one is not shown any more.
                entry.Bitmap?.Dispose();
                entry = (png, PrinterBitmaps.CustomSquare(png, PixelSize));
                _custom[key] = entry;
            }

            if (entry.Bitmap is { } bitmap)
            {
                return bitmap;
            }
        }
        else if (_custom.Remove(key, out (byte[] Png, Bitmap? Bitmap) previous))
        {
            previous.Bitmap?.Dispose();
        }

        return PrinterBitmaps.Illustration(picture.FallbackIllustrationId, PixelSize);
    }

    /// <summary>How many custom bitmaps are kept (for tests).</summary>
    internal int CustomBitmapCount => _custom.Count;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach ((_, Bitmap? bitmap) in _custom.Values)
        {
            bitmap?.Dispose();
        }

        _custom.Clear();
    }
}
