using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Avalonia;

namespace PrusaTrayDiagnostic.App.Platform.Windows;

/// <summary>
/// The whole and the visible rectangle of a top-level window (T-035 Q9): Windows 10/11 draw part of the frame as invisible
/// resize borders, which <c>GetWindowRect</c> includes and <c>DWMWA_EXTENDED_FRAME_BOUNDS</c> does not. Both are screen
/// pixels (the app is per-monitor DPI aware). Blittable structures only (<c>[LibraryImport]</c>, Native AOT).
/// </summary>
[SupportedOSPlatform("windows")]
internal static unsafe partial class WindowFrameNative
{
    private const uint DwmwaExtendedFrameBounds = 9;

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
}
