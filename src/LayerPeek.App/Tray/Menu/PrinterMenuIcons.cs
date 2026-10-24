using Avalonia.Media.Imaging;

using LayerPeek.App.Printers;

namespace LayerPeek.App.Tray.Menu;

/// <summary>
/// Bitmaps for the printer items of the tray menu (T-039, Windows only; macOS gets none until T-018). Avalonia draws the
/// Windows tray menu itself and fits the item icon into 16 × 16 DIP, so the bitmap has 16 DIP worth of physical pixels at
/// <see cref="Scaling"/>: 16 / 20 / 24 / 32 px at 100 / 125 / 150 / 200 %, the small sizes from the simplified art.
/// Illustrations are shared (<see cref="PrinterBitmaps"/>). A custom picture is fitted into a square once and kept per
/// printer while menu items use it (<see cref="Acquire"/> / <see cref="Release"/>; while the menu is rebuilt two items of
/// one printer may share it): a new picture of the printer replaces the previous bitmap, the last release or
/// <see cref="Dispose"/> frees it. UI thread only.
/// </summary>
internal sealed class PrinterMenuIcons : IDisposable
{
    /// <summary>Size of the icon in the menu.</summary>
    public const double SizeInDip = 16;

    // One entry per printer id.
    private readonly Dictionary<string, Entry> _custom = new(StringComparer.OrdinalIgnoreCase);
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

    /// <summary>How many custom bitmaps are kept (for tests).</summary>
    internal int CustomBitmapCount => _custom.Count;

    /// <summary>
    /// The bitmap of <paramref name="picture"/> of printer <paramref name="printerId"/>; owned by this instance (do not dispose).
    /// A custom picture that cannot be decoded shows its automatic image.
    /// </summary>
    public Bitmap Get(PrinterPicture picture, string? printerId)
    {
        ArgumentNullException.ThrowIfNull(picture);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (picture.CustomPng is { } png && EntryFor(printerId, png).Bitmap is { } bitmap)
        {
            return bitmap;
        }

        return PrinterBitmaps.Illustration(picture.FallbackIllustrationId, PixelSize);
    }

    /// <summary>A menu item shows the custom picture <paramref name="png"/> of <paramref name="printerId"/>.</summary>
    public void Acquire(string? printerId, byte[] png)
    {
        ArgumentNullException.ThrowIfNull(png);
        ObjectDisposedException.ThrowIf(_disposed, this);
        EntryFor(printerId, png).Users++;
    }

    /// <summary>
    /// A menu item no longer shows the custom picture <paramref name="png"/> of <paramref name="printerId"/> (removed, another
    /// picture, no icon). The bitmap is freed when no item uses it any more; a newer picture of the printer is not touched.
    /// </summary>
    public void Release(string? printerId, byte[] png)
    {
        ArgumentNullException.ThrowIfNull(png);
        string key = printerId ?? string.Empty;
        if (_custom.TryGetValue(key, out Entry? entry) && ReferenceEquals(entry.Png, png) && --entry.Users <= 0)
        {
            _custom.Remove(key);
            entry.Bitmap?.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (Entry entry in _custom.Values)
        {
            entry.Bitmap?.Dispose();
        }

        _custom.Clear();
    }

    /// <summary>The entry of the printer for <paramref name="png"/>; a new picture replaces (and frees) the previous one.</summary>
    private Entry EntryFor(string? printerId, byte[] png)
    {
        string key = printerId ?? string.Empty;
        if (!_custom.TryGetValue(key, out Entry? entry) || !ReferenceEquals(entry.Png, png))
        {
            // The items showing the replaced picture get the new one right away (the menu is updated synchronously).
            entry?.Bitmap?.Dispose();
            entry = new Entry(png, PrinterBitmaps.CustomSquare(png, PixelSize));
            _custom[key] = entry;
        }

        return entry;
    }

    /// <param name="Bitmap"><see langword="null"/> when the picture cannot be decoded.</param>
    private sealed record Entry(byte[] Png, Bitmap? Bitmap)
    {
        public int Users { get; set; }
    }
}
