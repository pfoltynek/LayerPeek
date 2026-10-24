using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Avalonia;

using LayerPeek.App.Cameras;

namespace LayerPeek.App.Platform.Windows;

/// <summary>
/// The whole and the visible rectangle of a top-level window (T-035 Q9): Windows 10/11 draw part of the frame as invisible
/// resize borders, which <c>GetWindowRect</c> includes and <c>DWMWA_EXTENDED_FRAME_BOUNDS</c> does not. Both are screen
/// pixels (the app is per-monitor DPI aware). Blittable structures only (<c>[LibraryImport]</c>, Native AOT).
/// </summary>
[SupportedOSPlatform("windows")]
internal static unsafe partial class WindowFrameNative
{
    private const uint DwmwaExtendedFrameBounds = 9;
    private const uint DwmwaVisibleFrameBorderThickness = 37;
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;

    /// <summary>The window and its visible part; <see langword="null"/> when Windows cannot tell (no DWM, invalid handle).</summary>
    public static (PixelRect Window, PixelRect Visible)? TryGetBounds(nint hwnd)
    {
        if (hwnd == 0)
        {
            return null;
        }

        try
        {
            Rect window;
            Rect visible;
            if (!GetWindowRect(hwnd, &window) || DwmGetWindowAttribute(hwnd, DwmwaExtendedFrameBounds, &visible, (uint)sizeof(Rect)) != 0)
            {
                return null;
            }

            return (window.ToPixelRect(), visible.ToPixelRect());
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // No desktop window manager (e.g. Windows Server Core): placed without the correction.
            return null;
        }
    }

    /// <summary>
    /// The frame <paramref name="hwnd"/> gets with its current style on a monitor of <paramref name="dpi"/> (T-071 Q3: the
    /// camera window is placed with it before it is shown, so that nothing moves afterwards). The visible border is what
    /// the window manager reports for the window when the window already has that DPI (Windows 11,
    /// <c>DWMWA_VISIBLE_FRAME_BORDER_THICKNESS</c>), else <see cref="ScaledVisibleBorder"/>. <see langword="null"/> when
    /// Windows cannot tell (invalid handle, before Windows 10 1607).
    /// </summary>
    public static PixelFrame? TryPredictFrame(nint hwnd, uint dpi)
    {
        if (hwnd == 0 || dpi == 0)
        {
            return null;
        }

        try
        {
            nint style = GetWindowLongPtrW(hwnd, GwlStyle);
            nint exStyle = GetWindowLongPtrW(hwnd, GwlExStyle);
            if (style == 0)
            {
                // No such window (a real top-level window always has a style).
                return null;
            }

            return FrameForStyle((uint)style, (uint)exStyle, dpi, VisibleBorderOf(hwnd, dpi));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Before Windows 10 1607 (no AdjustWindowRectExForDpi / GetDpiForWindow): the assumed chrome instead.
            return null;
        }
    }

    /// <summary>
    /// The frame of a window with <paramref name="style"/> and <paramref name="exStyle"/> at <paramref name="dpi"/>, in
    /// pixels per side, without a menu (<c>AdjustWindowRectExForDpi</c>, which Avalonia uses to size the window too);
    /// <see langword="null"/> when Windows refuses. <paramref name="visibleBorder"/> is passed through (none for a window
    /// without a frame).
    /// </summary>
    public static PixelFrame? FrameForStyle(uint style, uint exStyle, uint dpi, int visibleBorder)
    {
        Rect rect = default;
        if (!AdjustWindowRectExForDpi(&rect, style, menu: false, exStyle, dpi))
        {
            return null;
        }

        PixelFrame frame = new(-rect.Left, -rect.Top, rect.Right, rect.Bottom, visibleBorder);
        return frame.Left == 0 && frame.Top == 0 && frame.Right == 0 && frame.Bottom == 0 ? frame with { VisibleBorder = 0 } : frame;
    }

    /// <summary>
    /// The visible border of a window frame at <paramref name="dpi"/> when the window manager cannot be asked (the window
    /// still has another DPI, or Windows 10): 1 px at 96 DPI scaled and rounded like Windows scales pixels
    /// (<c>MulDiv(1, dpi, 96)</c>). Measured on Windows 11 (2026-10-23): 1 px at 100 % and 125 %, 2 px at 150 %.
    /// </summary>
    public static int ScaledVisibleBorder(uint dpi) => Math.Max((int)((dpi + 48) / 96), 1);

    private static int VisibleBorderOf(nint hwnd, uint dpi)
    {
        uint thickness;
        return GetDpiForWindow(hwnd) == dpi
            && DwmGetWindowAttribute(hwnd, DwmwaVisibleFrameBorderThickness, &thickness, sizeof(uint)) == 0
            && thickness is > 0 and < 64
                ? (int)thickness
                : ScaledVisibleBorder(dpi);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly PixelRect ToPixelRect() => new(Left, Top, Math.Max(Right - Left, 0), Math.Max(Bottom - Top, 0));
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint hwnd, Rect* rect);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmGetWindowAttribute(nint hwnd, uint attribute, Rect* value, uint size);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmGetWindowAttribute(nint hwnd, uint attribute, uint* value, uint size);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AdjustWindowRectExForDpi(Rect* rect, uint style, [MarshalAs(UnmanagedType.Bool)] bool menu, uint exStyle, uint dpi);

    [LibraryImport("user32.dll")]
    private static partial nint GetWindowLongPtrW(nint hwnd, int index);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint hwnd);
}
