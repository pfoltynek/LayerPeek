using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;

namespace PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;

/// <summary>
/// A capture of a small area of the screen as the user sees it (what lies on top there), for checks that a window
/// capture cannot answer, e.g. which window is in front (T-071). Never the whole desktop: the area must lie on one
/// monitor and cover at most a quarter of it (user decision 2026-10-22), and the tests take it only inside a window of
/// the app or of the test itself.
/// <para>Overlaps with the screenshot guard of T-069 (<c>CaptureScreenArea</c>); merge the two when both are in.</para>
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ScreenArea
{
    /// <summary>The largest share of a monitor an area may cover.</summary>
    public const double MaximumMonitorShare = 0.25;

    /// <summary>A rectangle of <paramref name="width"/> × <paramref name="height"/> centred on <paramref name="center"/>.</summary>
    public static Native.Rect Around(Point center, int width, int height) => new()
    {
        Left = center.X - (width / 2),
        Top = center.Y - (height / 2),
        Right = center.X - (width / 2) + width,
        Bottom = center.Y - (height / 2) + height,
    };

    /// <summary>Refuses an empty area, one that is not inside <paramref name="monitor"/> or covers more than a quarter of it.</summary>
    public static void EnsureAllowed(Native.Rect area, Native.Rect monitor)
    {
        if (area.Width <= 0 || area.Height <= 0)
        {
            throw new ArgumentException($"The screen area {area} is empty.", nameof(area));
        }

        if (area.Left < monitor.Left || area.Top < monitor.Top || area.Right > monitor.Right || area.Bottom > monitor.Bottom)
        {
            throw new ArgumentException($"The screen area {area} is not inside one monitor {monitor}.", nameof(area));
        }

        if ((double)area.Width * area.Height > MaximumMonitorShare * monitor.Width * monitor.Height)
        {
            throw new ArgumentException($"The screen area {area} covers more than {MaximumMonitorShare:P0} of the monitor {monitor}.", nameof(area));
        }
    }

    /// <summary>The screen pixels of <paramref name="area"/> (physical pixels; see <see cref="EnsureAllowed"/>).</summary>
    public static Bitmap Capture(Native.Rect area)
    {
        Native.Point center = new() { X = area.Left + (area.Width / 2), Y = area.Top + (area.Height / 2) };
        Native.MonitorInfo monitor = new() { Size = System.Runtime.InteropServices.Marshal.SizeOf<Native.MonitorInfo>() };
        if (!Native.GetMonitorInfo(Native.MonitorFromPoint(center, Native.MonitorDefaultToNearest), ref monitor))
        {
            throw new InvalidOperationException("GetMonitorInfo failed.");
        }

        EnsureAllowed(area, monitor.Monitor);
        Bitmap bitmap = new(area.Width, area.Height, PixelFormat.Format32bppArgb);
        try
        {
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(area.Left, area.Top, 0, 0, bitmap.Size);
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    /// <summary>Fraction (0..1) of the pixels within <paramref name="tolerance"/> (sum of channel differences) of <paramref name="color"/>.</summary>
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
}
