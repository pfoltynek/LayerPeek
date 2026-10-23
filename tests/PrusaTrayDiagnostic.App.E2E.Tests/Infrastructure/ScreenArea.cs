using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;

/// <summary>
/// Screen captures of single elements that are not windows of the app (a taskbar button, a tray icon), never of the
/// desktop (user decision 2026-10-22, T-070): an area must lie on one monitor and cover at most a quarter of it.
/// Windows of the app are captured with <see cref="Screenshots.Capture"/> instead.
/// <para>
/// T-069 adds the same guard as <c>Screenshots.CaptureScreenArea</c>; when both are merged, one of them should go.
/// </para>
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

    /// <summary>The screen pixels of <paramref name="area"/> (physical pixels); refuses areas <see cref="Refusal"/> names.</summary>
    public static Pixels Capture(Rectangle area)
    {
        Native.MonitorInfo info = new() { Size = Marshal.SizeOf<Native.MonitorInfo>() };
        Native.Point center = new() { X = area.Left + (area.Width / 2), Y = area.Top + (area.Height / 2) };
        if (!Native.GetMonitorInfo(Native.MonitorFromPoint(center, Native.MonitorDefaultToNearest), ref info))
        {
            throw new InvalidOperationException("GetMonitorInfo failed.");
        }

        Rectangle monitor = Rectangle.FromLTRB(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom);
        if (Refusal(area, monitor) is { } refusal)
        {
            throw new InvalidOperationException(refusal);
        }

        using Bitmap bitmap = new(area.Width, area.Height, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(area.Left, area.Top, 0, 0, area.Size);
        }

        return Pixels.From(bitmap);
    }
}
