using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using PrusaTrayDiagnostic.Tests.Shared;

namespace PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;

/// <summary>Where the app icon was found in a capture and how well it matched.</summary>
/// <param name="Score">Mean colour difference (0..1) of the best position from the icon drawn over the background.</param>
/// <param name="BackgroundScore">
/// Mean difference of that position from plain background: high means something is drawn there (no false match on an
/// empty area).
/// </param>
internal sealed record IconMatch(Rectangle Location, int Size, double Score, double BackgroundScore, Color Background)
{
    /// <summary>Most a toast icon may differ from the reference (acrylic background, Windows' own scaling).</summary>
    public const double MaxScore = 0.15;

    /// <summary>The area must differ from the background clearly more than from the icon.</summary>
    public const double MinContrast = 2;

    public bool IsMatch => Score <= MaxScore && BackgroundScore >= Math.Max(0.05, Score * MinContrast);

    public override string ToString() =>
        $"{Size}×{Size} px at ({Location.X},{Location.Y}), difference {Score:P1} (max {MaxScore:P0}), from plain background {BackgroundScore:P1}, background {Background.R},{Background.G},{Background.B}";
}

/// <summary>
/// Finds the app icon (<c>app-icon.ico</c>, T-025) in a capture of a toast header: the entry of the expected size is
/// drawn over the background colour of the capture and slid over it; the best position wins (T-068).
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ToastIcon
{
    /// <summary>Icon sizes (DIP) of toast headers: 16 is the header icon of Windows 11; the others cover other layouts.</summary>
    private static readonly int[] HeaderSizes = [16, 20, 24, 32];

    /// <summary>The icon file of the app.</summary>
    public static string IconFile { get; } = Path.Combine(TestWorktree.Root, "src", "PrusaTrayDiagnostic.App", "Assets", "AppIcon", "app-icon.ico");

    /// <summary>
    /// Icon sizes (px) a toast header may use at <paramref name="scale"/> (DPI / 96): 16 DIP is the header icon of
    /// Windows 11; 20, 24 and 32 DIP cover other layouts.
    /// </summary>
    public static int[] CandidateSizes(double scale) =>
        [.. HeaderSizes.Select(dip => (int)Math.Round(dip * scale)).Distinct()];

    /// <summary>The icon entry closest to <paramref name="size"/>, scaled to exactly that size.</summary>
    public static Bitmap Reference(int size)
    {
        using Icon icon = new(IconFile, size, size);
        using Bitmap entry = icon.ToBitmap();
        Bitmap scaled = new(size, size, PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(scaled);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.DrawImage(entry, new Rectangle(0, 0, size, size));
        return scaled;
    }

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

            using Bitmap reference = Reference(size);
            Pixels icon = Pixels.Of(reference).Over(background);
            for (int y = 0; y + size <= area.Height; y++)
            {
                for (int x = 0; x + size <= area.Width; x++)
                {
                    double score = pixels.Difference(icon, x, y, best?.Score ?? double.MaxValue);
                    if (best is null || score < best.Score)
                    {
                        best = new IconMatch(new Rectangle(x, y, size, size), size, score, pixels.DifferenceFrom(background, x, y, size), background);
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

        /// <summary>Mean RGB difference (0..1) of <paramref name="other"/> placed at (<paramref name="left"/>, <paramref name="top"/>); stops early above <paramref name="limit"/>.</summary>
        public double Difference(Pixels other, int left, int top, double limit)
        {
            long sum = 0;
            long count = (long)other._width * other._height * 3;
            long stop = double.IsFinite(limit) && limit < 1 ? (long)(limit * 255 * count) + 1 : long.MaxValue;
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

                if (sum > stop)
                {
                    return 1;
                }
            }

            return sum / (255.0 * count);
        }

        /// <summary>Mean RGB difference (0..1) of a square from a plain colour.</summary>
        public double DifferenceFrom(Color color, int left, int top, int size)
        {
            long sum = 0;
            for (int y = top; y < top + size; y++)
            {
                for (int x = left; x < left + size; x++)
                {
                    (byte b, byte g, byte r) = At(x, y);
                    sum += Math.Abs(r - color.R) + Math.Abs(g - color.G) + Math.Abs(b - color.B);
                }
            }

            return sum / (255.0 * size * size * 3);
        }

        private (byte B, byte G, byte R) At(int x, int y)
        {
            int i = ((y * _width) + x) * 4;
            return (_rgba[i], _rgba[i + 1], _rgba[i + 2]);
        }
    }
}
