using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;

namespace PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;

/// <summary>
/// Screenshots of single windows into <see cref="E2EFixture.ResultsDirectory"/> (for review by a person or Claude),
/// captured with <c>PrintWindow(PW_RENDERFULLCONTENT)</c>, so other windows in front of it do not matter.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class Screenshots
{
    private static int _counter;

    /// <summary>The window as it is drawn now, including its frame.</summary>
    public static Bitmap Capture(nint hwnd)
    {
        if (!Native.GetWindowRect(hwnd, out Native.Rect rect) || rect.Width <= 0 || rect.Height <= 0)
        {
            // Which case it is tells whether the app closed the window (T-073: seen once, cause not found).
            throw new InvalidOperationException(
                $"The window 0x{hwnd:X} has no area to capture (exists: {Native.IsWindow(hwnd)}, visible: {Native.IsWindowVisible(hwnd)}, "
                + $"minimized: {Native.IsIconic(hwnd)}, rect: {rect}).");
        }

        Bitmap bitmap = new(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
        try
        {
            using Graphics graphics = Graphics.FromImage(bitmap);
            nint hdc = graphics.GetHdc();
            try
            {
                if (!Native.PrintWindow(hwnd, hdc, Native.PwRenderFullContent))
                {
                    throw new InvalidOperationException("PrintWindow failed.");
                }
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }

            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    /// <summary>Captures the window and saves it as <c>NNN-name.png</c>; returns the file.</summary>
    public static string Save(nint hwnd, string name)
    {
        using Bitmap bitmap = Capture(hwnd);
        return Save(bitmap, name);
    }

    public static string Save(Bitmap bitmap, string name)
    {
        int number = Interlocked.Increment(ref _counter);
        string file = Path.Combine(E2EFixture.ResultsDirectory, $"{number:000}-{Sanitize(name)}.png");
        bitmap.Save(file, ImageFormat.Png);
        return file;
    }

    /// <summary>A text report next to the screenshots (same numbering), saved as <c>NNN-name.txt</c>.</summary>
    public static string SaveText(string text, string name)
    {
        int number = Interlocked.Increment(ref _counter);
        string file = Path.Combine(E2EFixture.ResultsDirectory, $"{number:000}-{Sanitize(name)}.txt");
        File.WriteAllText(file, text);
        return file;
    }

    /// <summary>Fraction (0..1) of sampled pixels that differ noticeably between two captures of the same size.</summary>
    public static double Difference(Bitmap first, Bitmap second)
    {
        if (first.Size != second.Size)
        {
            return 1;
        }

        int differing = 0;
        int sampled = 0;
        for (int y = 0; y < first.Height; y += 4)
        {
            for (int x = 0; x < first.Width; x += 4)
            {
                Color a = first.GetPixel(x, y);
                Color b = second.GetPixel(x, y);
                if (Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) > 24)
                {
                    differing++;
                }

                sampled++;
            }
        }

        return sampled == 0 ? 0 : (double)differing / sampled;
    }

    /// <summary>Fraction (0..1) of sampled pixels that are not (nearly) black: the camera window shows a picture.</summary>
    public static double NonBlack(Bitmap bitmap, Rectangle area)
    {
        int lit = 0;
        int sampled = 0;
        for (int y = Math.Max(0, area.Top); y < Math.Min(bitmap.Height, area.Bottom); y += 4)
        {
            for (int x = Math.Max(0, area.Left); x < Math.Min(bitmap.Width, area.Right); x += 4)
            {
                Color c = bitmap.GetPixel(x, y);
                if (c.R + c.G + c.B > 60)
                {
                    lit++;
                }

                sampled++;
            }
        }

        return sampled == 0 ? 0 : (double)lit / sampled;
    }

    /// <summary>Fraction (0..1) of all pixels within <paramref name="tolerance"/> (sum of channel differences) of <paramref name="color"/>.</summary>
    public static double FractionNear(Bitmap bitmap, Color color, int tolerance = 60)
    {
        int near = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                Color c = bitmap.GetPixel(x, y);
                if (Math.Abs(c.R - color.R) + Math.Abs(c.G - color.G) + Math.Abs(c.B - color.B) <= tolerance)
                {
                    near++;
                }
            }
        }

        return bitmap.Width * bitmap.Height == 0 ? 0 : (double)near / (bitmap.Width * bitmap.Height);
    }

    private static string Sanitize(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return new string([.. name.Select(c => invalid.Contains(c) || c == ' ' ? '_' : c)]);
    }
}
