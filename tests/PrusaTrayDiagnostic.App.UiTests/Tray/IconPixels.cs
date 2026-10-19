using System.Runtime.InteropServices;

using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace PrusaTrayDiagnostic.App.UiTests.Tray;

/// <summary>Straight-alpha pixels of a small rendered bitmap. UI thread only.</summary>
internal sealed class IconPixels
{
    private readonly Color[] _pixels;

    private IconPixels(int size, Color[] pixels)
    {
        Size = size;
        _pixels = pixels;
    }

    public int Size { get; }

    public Color this[int x, int y] => _pixels[(y * Size) + x];

    public IEnumerable<(int X, int Y, Color Color)> All()
    {
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                yield return (x, y, this[x, y]);
            }
        }
    }

    public static IconPixels Read(Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        PixelSize size = bitmap.PixelSize;
        Assert.Equal(size.Width, size.Height);
        using WriteableBitmap copy = new(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        byte[] bytes = new byte[size.Width * size.Height * 4];
        using (ILockedFramebuffer buffer = copy.Lock())
        {
            bitmap.CopyPixels(buffer);
            for (int row = 0; row < size.Height; row++)
            {
                Marshal.Copy(buffer.Address + (row * buffer.RowBytes), bytes, row * size.Width * 4, size.Width * 4);
            }
        }

        Color[] pixels = new Color[size.Width * size.Height];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = Color.FromArgb(bytes[(i * 4) + 3], bytes[(i * 4) + 2], bytes[(i * 4) + 1], bytes[i * 4]);
        }

        return new IconPixels(size.Width, pixels);
    }

    /// <summary>Whether <paramref name="actual"/> is <paramref name="expected"/> (opaque colours; tolerance for rounding).</summary>
    public static bool Same(Color expected, Color actual) => Near(expected, actual, 2);

    /// <summary>
    /// Whether every channel differs by at most <paramref name="tolerance"/>; thin anti-aliased strokes (the badge outline
    /// at 20 px is under 1 px wide) never reach their exact colour.
    /// </summary>
    public static bool Near(Color expected, Color actual, int tolerance) =>
        Math.Abs(expected.A - actual.A) <= tolerance
        && Math.Abs(expected.R - actual.R) <= tolerance
        && Math.Abs(expected.G - actual.G) <= tolerance
        && Math.Abs(expected.B - actual.B) <= tolerance;
}
