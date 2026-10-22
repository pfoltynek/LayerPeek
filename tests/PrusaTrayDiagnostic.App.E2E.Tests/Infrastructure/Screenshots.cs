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
            throw new InvalidOperationException("The window has no area to capture.");
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

    /// <summary>
    /// Largest share of its monitor a screen area may cover (decided by the user 2026-10-22, T-069): the tests capture only
    /// the element under test (a tray button, the overflow flyout, a menu, a jump list, the taskbar), never the desktop
    /// with the user's windows.
    /// </summary>
    public const double MaxScreenAreaShare = 0.25;

    /// <summary>
    /// Why <paramref name="area"/> must not be captured from the screen of <paramref name="monitor"/>, or
    /// <see langword="null"/>: an empty area, or one covering more than <see cref="MaxScreenAreaShare"/> of the monitor
    /// (the whole screen or most of it).
    /// </summary>
    public static string? ScreenAreaRefusal(Rectangle area, Rectangle monitor)
    {
        if (area.Width <= 0 || area.Height <= 0)
        {
            return $"The screen area {area} is empty.";
        }

        Rectangle onMonitor = Rectangle.Intersect(area, monitor);
        double share = monitor.Width <= 0 || monitor.Height <= 0 ? 1 : (double)onMonitor.Width * onMonitor.Height / ((double)monitor.Width * monitor.Height);
        return share > MaxScreenAreaShare || area.Contains(monitor)
            ? $"Refusing to capture {area}: it covers {share:P0} of the monitor {monitor}; only the element under test may be captured, never the desktop."
            : null;
    }

    /// <summary>
    /// The screen pixels of <paramref name="area"/> (grown by <paramref name="margin"/>, cut to the screens) for elements that
    /// are not windows of the app (the tray button, the overflow flyout, a jump list). Refuses the whole desktop or most of a
    /// monitor (<see cref="ScreenAreaRefusal"/>).
    /// </summary>
    public static Bitmap CaptureScreenArea(Rectangle area, int margin = 0)
    {
        area.Inflate(margin, margin);
        Native.Rect screen = Native.VirtualScreen();
        area.Intersect(Rectangle.FromLTRB(screen.Left, screen.Top, screen.Right, screen.Bottom));
        Native.MonitorInfo info = new() { Size = System.Runtime.InteropServices.Marshal.SizeOf<Native.MonitorInfo>() };
        Native.Point center = new() { X = area.Left + (area.Width / 2), Y = area.Top + (area.Height / 2) };
        if (!Native.GetMonitorInfo(Native.MonitorFromPoint(center, 2), ref info))
        {
            throw new InvalidOperationException("GetMonitorInfo failed.");
        }

        Rectangle monitor = Rectangle.FromLTRB(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom);
        if (ScreenAreaRefusal(area, monitor) is { } refusal)
        {
            throw new InvalidOperationException(refusal);
        }

        Bitmap bitmap = new(area.Width, area.Height, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(area.Left, area.Top, 0, 0, area.Size);
        }

        return bitmap;
    }

    /// <summary><see cref="CaptureScreenArea"/> saved as <c>NNN-name.png</c>.</summary>
    public static string SaveScreenArea(Rectangle area, int margin, string name)
    {
        using Bitmap bitmap = CaptureScreenArea(area, margin);
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

    private static string Sanitize(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return new string([.. name.Select(c => invalid.Contains(c) || c == ' ' ? '_' : c)]);
    }
}
