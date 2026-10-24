namespace LayerPeek.App.Printers;

/// <summary>
/// Turns 32-bit pixels stored with an EXIF orientation (1–8) into the upright image. Orientations 5–8 swap width and
/// height. The pixel format does not matter (whole pixels are moved).
/// </summary>
internal static class PixelOrientation
{
    /// <summary>Whether <paramref name="orientation"/> swaps width and height (5–8).</summary>
    public static bool SwapsAxes(int orientation) => orientation is >= 5 and <= 8;

    /// <param name="pixels">Row by row, <paramref name="width"/> × <paramref name="height"/>.</param>
    /// <returns>The upright pixels, row by row; their width is <paramref name="height"/> when <see cref="SwapsAxes"/>.</returns>
    public static uint[] Apply(ReadOnlySpan<uint> pixels, int width, int height, int orientation)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (pixels.Length != width * height)
        {
            throw new ArgumentException("The pixel count does not match the size.", nameof(pixels));
        }

        bool swap = SwapsAxes(orientation);
        int outWidth = swap ? height : width;
        int outHeight = swap ? width : height;
        uint[] result = new uint[pixels.Length];
        for (int y = 0; y < outHeight; y++)
        {
            for (int x = 0; x < outWidth; x++)
            {
                (int sx, int sy) = orientation switch
                {
                    2 => (width - 1 - x, y),
                    3 => (width - 1 - x, height - 1 - y),
                    4 => (x, height - 1 - y),
                    5 => (y, x),
                    6 => (y, height - 1 - x),
                    7 => (width - 1 - y, height - 1 - x),
                    8 => (width - 1 - y, x),
                    _ => (x, y),
                };
                result[(y * outWidth) + x] = pixels[(sy * width) + sx];
            }
        }

        return result;
    }
}
