using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;

/// <summary>
/// The only way the end-to-end tests read pixels from the screen (user decision 2026-10-22, T-069, T-070): single elements
/// that are not windows of the app (a taskbar button, a tray icon, the overflow flyout, a jump list, the taskbar), or a
/// small probe of what is in front in the middle of a camera window (T-071, always on top over a window of the test),
/// never the desktop. An area must lie on one monitor and cover at most a quarter of it. Windows of the app are captured with
/// <see cref="Screenshots.Capture"/> (PrintWindow) instead.
/// </summary>
internal static class ScreenArea
{
    /// <summary>The largest share of its monitor an area may cover.</summary>
    public const double MaxMonitorShare = 0.25;

    /// <summary>
    /// Why <paramref name="area"/> must not be captured on <paramref name="monitor"/>, or <see langword="null"/>: it is
    /// empty, reaches beyond the monitor or covers more than <see cref="MaxMonitorShare"/> of it.
    /// </summary>
    public static string? Refusal(Rectangle area, Rectangle monitor)
    {
        if (area.Width <= 0 || area.Height <= 0)
        {
            return $"The screen area {area} is empty.";
        }

        if (!monitor.Contains(area))
        {
            return $"Refusing to capture {area}: it is not inside the monitor {monitor}; only one element under test may be captured.";
        }

        double share = (double)area.Width * area.Height / ((double)monitor.Width * monitor.Height);
        return share > MaxMonitorShare
            ? $"Refusing to capture {area}: it covers {share:P0} of the monitor {monitor} (at most {MaxMonitorShare:P0}); only the element under test may be captured, never the desktop."
            : null;
    }

    /// <summary>
    /// The screen pixels of <paramref name="area"/> exactly (physical pixels, for measurements); refuses areas
    /// <see cref="Refusal"/> names.
    /// </summary>
    public static Pixels Capture(Rectangle area)
    {
        using Bitmap bitmap = CaptureExactly(area);
        return Pixels.From(bitmap);
    }

    /// <summary>
    /// A screenshot of an element: <paramref name="area"/> grown by <paramref name="margin"/> and cut to the element's monitor
    /// (an element at the screen edge keeps its margin on the other sides); refuses areas <see cref="Refusal"/> names.
    /// </summary>
    public static Bitmap CaptureBitmap(Rectangle area, int margin = 0)
    {
        area.Inflate(margin, margin);
        area.Intersect(MonitorOf(area));
        return CaptureExactly(area);
    }

    /// <summary><see cref="CaptureBitmap"/> saved into the results as <c>NNN-name.png</c>; returns the file.</summary>
    public static string Save(Rectangle area, int margin, string name)
    {
        using Bitmap bitmap = CaptureBitmap(area, margin);
        return Screenshots.Save(bitmap, name);
    }

    private static Bitmap CaptureExactly(Rectangle area)
    {
        if (Refusal(area, MonitorOf(area)) is { } refusal)
        {
            throw new InvalidOperationException(refusal);
        }

        Bitmap bitmap = new(area.Width, area.Height, PixelFormat.Format32bppArgb);
        try
        {
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(area.Left, area.Top, 0, 0, area.Size);
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    /// <summary>The monitor of the centre of <paramref name="area"/> (the nearest one when the centre is on none).</summary>
    private static Rectangle MonitorOf(Rectangle area)
    {
        Native.MonitorInfo info = new() { Size = Marshal.SizeOf<Native.MonitorInfo>() };
        Native.Point center = new() { X = area.Left + (area.Width / 2), Y = area.Top + (area.Height / 2) };
        if (!Native.GetMonitorInfo(Native.MonitorFromPoint(center, Native.MonitorDefaultToNearest), ref info))
        {
            throw new InvalidOperationException("GetMonitorInfo failed.");
        }

        return Rectangle.FromLTRB(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom);
    }
}
