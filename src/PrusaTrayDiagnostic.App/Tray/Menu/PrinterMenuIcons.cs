using Avalonia.Media.Imaging;

using PrusaTrayDiagnostic.App.Printers;

namespace PrusaTrayDiagnostic.App.Tray.Menu;

/// <summary>
/// Bitmaps for the printer items of the tray menu (T-039, Windows only; macOS gets none until T-018). Avalonia draws the
/// Windows tray menu itself and fits the item icon into 16 × 16 DIP, so the bitmap has 16 DIP worth of physical pixels at
/// <see cref="Scaling"/>: 16 / 20 / 24 / 32 px at 100 / 125 / 150 / 200 %, the small sizes from the simplified art.
/// Illustrations are shared (<see cref="PrinterBitmaps"/>); custom pictures are fitted into a square once per picture and
/// released by <see cref="Dispose"/>. UI thread only.
/// </summary>
internal sealed class PrinterMenuIcons : IDisposable
{
    /// <summary>Size of the icon in the menu.</summary>
    public const double SizeInDip = 16;

    private readonly Dictionary<byte[], Bitmap?> _custom = new(ReferenceEqualityComparer.Instance);
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

    /// <summary>The bitmap of <paramref name="picture"/>; owned by this instance (do not dispose).</summary>
    public Bitmap Get(PrinterPicture picture)
    {
        ArgumentNullException.ThrowIfNull(picture);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (picture.CustomPng is { } png)
        {
            if (!_custom.TryGetValue(png, out Bitmap? bitmap))
            {
                bitmap = PrinterBitmaps.CustomSquare(png, PixelSize);
                _custom[png] = bitmap;
            }

            if (bitmap is not null)
            {
                return bitmap;
            }
        }

        return PrinterBitmaps.Illustration(picture.CustomPng is null ? picture.IllustrationId : PrinterIllustrations.Generic, PixelSize);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (Bitmap? bitmap in _custom.Values)
        {
            bitmap?.Dispose();
        }

        _custom.Clear();
    }
}
