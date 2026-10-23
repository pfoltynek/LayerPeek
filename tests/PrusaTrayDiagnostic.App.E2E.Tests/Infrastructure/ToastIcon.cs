using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using PrusaTrayDiagnostic.Tests.Shared;

namespace PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;

/// <summary>Where the app icon was found in a capture and how well it matched.</summary>
/// <param name="Score">Mean colour difference (0..1) of the position from the icon drawn over the background.</param>
/// <param name="Correlation">
/// Normalized cross-correlation (-1..1) of the position with the icon: its shape, whatever the brightness. A plain area
/// has 0, so an empty header never matches, and a dark icon on a dark toast is still told apart from the background.
/// </param>
/// <param name="EntrySize">The icon entry the reference was drawn from (<see cref="ToastIcon.EntrySizes"/>).</param>
internal sealed record IconMatch(Rectangle Location, int Size, double Score, double Correlation, Color Background, int EntrySize)
{
    /// <summary>Most a toast icon may differ in colour from the reference (acrylic background, Windows' own scaling).</summary>
    public const double MaxScore = 0.15;

    /// <summary>Least correlation of the shape with the icon.</summary>
    public const double MinCorrelation = 0.8;

    public bool IsMatch => Score <= MaxScore && Correlation >= MinCorrelation;

    public override string ToString() =>
        $"{Size}×{Size} px (from the {EntrySize} px entry) at ({Location.X},{Location.Y}), correlation {Correlation:0.000} (min {MinCorrelation:0.0}), "
        + $"difference {Score:P1} (max {MaxScore:P0}), background {Background.R},{Background.G},{Background.B}";
}

/// <summary>
/// Finds the app icon (<c>app-icon.ico</c>, T-025) in a capture of a toast header: the entry of the expected size is
/// drawn over the background colour of the capture and slid over it; the position with the best correlation wins (T-068).
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ToastIcon
{
    /// <summary>Icon sizes (DIP) of toast headers: 12 is the header icon of Windows 11 26300 (measured); the others cover other layouts.</summary>
    private static readonly int[] HeaderSizes = [12, 16, 20, 24, 32];

    /// <summary>The icon file of the app.</summary>
    public static string IconFile { get; } = Path.Combine(TestWorktree.Root, "src", "PrusaTrayDiagnostic.App", "Assets", "AppIcon", "app-icon.ico");

    /// <summary>
    /// Icon sizes (px) a toast header may use at <paramref name="scale"/> (DPI / 96): 12 DIP is the header icon of
    /// Windows 11 26300; 16, 20, 24 and 32 DIP cover other builds and layouts.
    /// </summary>
    public static int[] CandidateSizes(double scale) =>
        [.. HeaderSizes.Select(dip => (int)Math.Round(dip * scale)).Distinct()];

    /// <summary>The icon entry closest to <paramref name="size"/>, scaled to exactly that size.</summary>
    public static Bitmap Reference(int size) => Reference(size, size);

    /// <summary>
    /// The icon entry closest to <paramref name="entrySize"/>, scaled to <paramref name="size"/>: Windows 11 draws the toast
    /// header icon by downscaling a larger entry, which is softer than the entry of the exact size.
    /// </summary>
    public static Bitmap Reference(int size, int entrySize)
    {
        using Icon icon = new(IconFile, entrySize, entrySize);
        using Bitmap entry = icon.ToBitmap();
        Bitmap scaled = new(size, size, PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(scaled);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.DrawImage(entry, new Rectangle(0, 0, size, size));
        return scaled;
    }

    /// <summary>The entries a header icon of <paramref name="size"/> px may be drawn from: its own size, twice it, the largest.</summary>
    internal static int[] EntrySizes(int size) => [.. new[] { size, size * 2, 256 }.Distinct()];

    /// <summary>The best position of the icon at any of <paramref name="sizes"/> in <paramref name="area"/>; null when the area is too small.</summary>
    public static IconMatch? Find(Bitmap area, IReadOnlyList<int> sizes)
    {
        Pixels pixels = Pixels.Of(area);
        Color background = pixels.BorderMedian();
        IconMatch? best = null;
        foreach (int size in sizes)
        {
            if (size > area.Width || size > area.Height)
            {
                continue;
            }

            foreach (int entrySize in EntrySizes(size))
            {
                using Bitmap reference = Reference(size, entrySize);
                Pixels icon = Pixels.Of(reference).Over(background);
                for (int y = 0; y + size <= area.Height; y++)
                {
                    for (int x = 0; x + size <= area.Width; x++)
                    {
                        double correlation = pixels.Correlation(icon, x, y);
                        if (best is null || correlation > best.Correlation)
                        {
                            best = new IconMatch(new Rectangle(x, y, size, size), size, pixels.Difference(icon, x, y), correlation, background, entrySize);
                        }
                    }
                }
            }
        }

        return best;
    }

    /// <summary>The RGB pixels of a bitmap (alpha separately), for fast comparisons.</summary>
    private sealed class Pixels
    {
        private readonly int _width;
        private readonly int _height;
        private readonly byte[] _rgba;

        private Pixels(int width, int height, byte[] rgba)
        {
            _width = width;
            _height = height;
            _rgba = rgba;
        }

        public static Pixels Of(Bitmap bitmap)
        {
            Rectangle all = new(0, 0, bitmap.Width, bitmap.Height);
            BitmapData data = bitmap.LockBits(all, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                byte[] rgba = new byte[bitmap.Width * bitmap.Height * 4];
                for (int y = 0; y < bitmap.Height; y++)
                {
                    // BGRA rows.
                    Marshal.Copy(data.Scan0 + (y * data.Stride), rgba, y * bitmap.Width * 4, bitmap.Width * 4);
                }

                return new Pixels(bitmap.Width, bitmap.Height, rgba);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        /// <summary>This (with alpha) drawn over an opaque <paramref name="background"/>.</summary>
        public Pixels Over(Color background)
        {
            byte[] result = new byte[_rgba.Length];
            for (int i = 0; i < _rgba.Length; i += 4)
            {
                double alpha = _rgba[i + 3] / 255.0;
                result[i] = (byte)Math.Round((_rgba[i] * alpha) + (background.B * (1 - alpha)));
                result[i + 1] = (byte)Math.Round((_rgba[i + 1] * alpha) + (background.G * (1 - alpha)));
                result[i + 2] = (byte)Math.Round((_rgba[i + 2] * alpha) + (background.R * (1 - alpha)));
                result[i + 3] = 255;
            }

            return new Pixels(_width, _height, result);
        }

        /// <summary>The median colour of the outermost pixels (the background around the icon).</summary>
        public Color BorderMedian()
        {
            List<(byte B, byte G, byte R)> border = [];
            for (int x = 0; x < _width; x++)
            {
                border.Add(At(x, 0));
                border.Add(At(x, _height - 1));
            }

            for (int y = 1; y < _height - 1; y++)
            {
                border.Add(At(0, y));
                border.Add(At(_width - 1, y));
            }

            static byte Median(IEnumerable<byte> values)
            {
                byte[] sorted = [.. values.Order()];
                return sorted[sorted.Length / 2];
            }

            return Color.FromArgb(Median(border.Select(static p => p.R)), Median(border.Select(static p => p.G)), Median(border.Select(static p => p.B)));
        }

        /// <summary>Mean RGB difference (0..1) of <paramref name="other"/> placed at (<paramref name="left"/>, <paramref name="top"/>).</summary>
        public double Difference(Pixels other, int left, int top)
        {
            long sum = 0;
            for (int y = 0; y < other._height; y++)
            {
                int row = (((top + y) * _width) + left) * 4;
                int otherRow = y * other._width * 4;
                for (int x = 0; x < other._width * 4; x += 4)
                {
                    sum += Math.Abs(_rgba[row + x] - other._rgba[otherRow + x])
                        + Math.Abs(_rgba[row + x + 1] - other._rgba[otherRow + x + 1])
                        + Math.Abs(_rgba[row + x + 2] - other._rgba[otherRow + x + 2]);
                }
            }

            return sum / (255.0 * other._width * other._height * 3);
        }

        /// <summary>
        /// Normalized cross-correlation (-1..1) of the RGB values of <paramref name="other"/> and of the square at
        /// (<paramref name="left"/>, <paramref name="top"/>); 0 when either is plain.
        /// </summary>
        public double Correlation(Pixels other, int left, int top)
        {
            double sumA = 0, sumB = 0, sumAA = 0, sumBB = 0, sumAB = 0;
            int count = 0;
            for (int y = 0; y < other._height; y++)
            {
                int row = (((top + y) * _width) + left) * 4;
                int otherRow = y * other._width * 4;
                for (int x = 0; x < other._width * 4; x++)
                {
                    if ((x & 3) == 3)
                    {
                        // Alpha.
                        continue;
                    }

                    double a = _rgba[row + x];
                    double b = other._rgba[otherRow + x];
                    sumA += a;
                    sumB += b;
                    sumAA += a * a;
                    sumBB += b * b;
                    sumAB += a * b;
                    count++;
                }
            }

            double varianceA = sumAA - (sumA * sumA / count);
            double varianceB = sumBB - (sumB * sumB / count);
            return varianceA <= 1e-9 || varianceB <= 1e-9 ? 0 : (sumAB - (sumA * sumB / count)) / Math.Sqrt(varianceA * varianceB);
        }

        private (byte B, byte G, byte R) At(int x, int y)
        {
            int i = ((y * _width) + x) * 4;
            return (_rgba[i], _rgba[i + 1], _rgba[i + 2]);
        }
    }
}
