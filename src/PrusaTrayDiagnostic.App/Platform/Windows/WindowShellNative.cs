using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Microsoft.Win32.SafeHandles;

namespace PrusaTrayDiagnostic.App.Platform.Windows;

/// <summary>
/// Shell and window manager functions of T-043 (taskbar properties of a window, foreground, flashing, the pipe server's
/// process). Source-generated marshalling only (Native AOT).
/// </summary>
[SupportedOSPlatform("windows")]
internal static unsafe partial class WindowShellNative
{
    private const uint FlashAll = 0x3;
    private const uint FlashTimerNoForeground = 0xC;

    /// <summary>How often the taskbar button flashes before it stays highlighted until the window is activated.</summary>
    private const uint FlashCount = 3;

    /// <summary>IID_IPropertyStore.</summary>
    public static readonly Guid PropertyStoreId = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");

    /// <summary>The property store of a top-level window (one reference, to be wrapped or released).</summary>
    public static int GetPropertyStoreForWindow(nint hwnd, out nint store) =>
        SHGetPropertyStoreForWindow(hwnd, in PropertyStoreId, out store);

    /// <summary>Whether <paramref name="hwnd"/> is the foreground window.</summary>
    public static bool IsForeground(nint hwnd) => hwnd != 0 && GetForegroundWindow() == hwnd;

    /// <summary>
    /// Flashes the taskbar button a few times; it then stays highlighted until the window comes to the foreground.
    /// </summary>
    public static void Flash(nint hwnd)
    {
        FlashInfo info = new()
        {
            Size = (uint)sizeof(FlashInfo),
            Window = hwnd,
            Flags = FlashAll | FlashTimerNoForeground,
            Count = FlashCount,
            Timeout = 0,
        };
        _ = FlashWindowEx(&info);
    }

    /// <summary>The process at the other end of a connected client pipe.</summary>
    public static bool TryGetPipeServerProcessId(SafePipeHandle pipe, out uint processId) =>
        GetNamedPipeServerProcessId(pipe, out processId);

    /// <summary>Lets <paramref name="processId"/> take the foreground (only works while this process may do so itself).</summary>
    public static void AllowForeground(uint processId) => _ = AllowSetForegroundWindow(processId);

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo
    {
        public uint Size;
        public nint Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [LibraryImport("shell32.dll")]
    private static partial int SHGetPropertyStoreForWindow(nint hwnd, in Guid interfaceId, out nint store);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FlashWindowEx(FlashInfo* info);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(uint processId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);
}
