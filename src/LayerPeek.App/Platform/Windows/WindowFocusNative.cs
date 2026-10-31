using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LayerPeek.App.Platform.Windows;

/// <summary>
/// The keyboard focus of the window manager for a window of this thread (T-122). Source-generated marshalling only
/// (Native AOT).
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class WindowFocusNative
{
    /// <summary>
    /// Gives <paramref name="hwnd"/> the keyboard focus when it is the active window of this thread but the focus is
    /// elsewhere (or nowhere); never activates it.
    /// </summary>
    public static void FocusIfActive(nint hwnd)
    {
        if (hwnd != 0 && GetActiveWindow() == hwnd && GetFocus() != hwnd)
        {
            _ = SetFocus(hwnd);
        }
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetActiveWindow();

    [LibraryImport("user32.dll")]
    private static partial nint GetFocus();

    [LibraryImport("user32.dll")]
    private static partial nint SetFocus(nint hwnd);
}
