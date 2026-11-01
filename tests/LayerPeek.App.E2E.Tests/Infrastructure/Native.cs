using System.Runtime.InteropServices;

namespace LayerPeek.App.E2E.Tests.Infrastructure;

/// <summary>Win32 calls the end-to-end tests use to inspect the app's windows (read-only, except for capturing).</summary>
internal static partial class Native
{
    public const int GwlStyle = -16;
    public const int GwlExStyle = -20;

    public const long WsMinimizeBox = 0x0002_0000;
    public const long WsMaximizeBox = 0x0001_0000;
    public const long WsThickFrame = 0x0004_0000;
    public const long WsCaption = 0x00C0_0000;
    public const long WsSysMenu = 0x0008_0000;

    public const long WsExTopmost = 0x0000_0008;
    public const long WsExToolWindow = 0x0000_0080;
    public const long WsExAppWindow = 0x0004_0000;

    public const uint GwOwner = 4;
    public const uint WmGetIcon = 0x007F;
    public const nint IconSmall = 0;
    public const nint IconBig = 1;
    public const nint IconSmall2 = 2;
    public const int GclpHIcon = -14;
    public const int GclpHIconSm = -34;

    public const uint MonitorDefaultToNearest = 2;

    /// <summary>PrintWindow: also content drawn by DirectComposition / the GPU.</summary>
    public const uint PwRenderFullContent = 2;

    public const int SmXVirtualScreen = 76;
    public const int SmYVirtualScreen = 77;
    public const int SmCxVirtualScreen = 78;
    public const int SmCyVirtualScreen = 79;

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;

        public readonly int Height => Bottom - Top;

        public override readonly string ToString() => $"({Left},{Top})-({Right},{Bottom}) {Width}x{Height}";
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    public delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static partial nint GetWindowLongPtr(nint hwnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "GetClassLongPtrW")]
    public static partial nint GetClassLongPtr(nint hwnd, int index);

    [LibraryImport("user32.dll")]
    public static partial nint GetWindow(nint hwnd, uint command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetClientRect(nint hwnd, out Rect rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(nint hwnd, out Rect rect);

    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsIconic(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsZoomed(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [LibraryImport("user32.dll")]
    public static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
    public static partial nint SendMessageTimeout(nint hwnd, uint message, nint wParam, nint lParam, uint flags, uint timeoutMs, out nint result);

    [LibraryImport("user32.dll")]
    public static partial nint MonitorFromWindow(nint hwnd, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PrintWindow(nint hwnd, nint hdc, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int GetWindowText(nint hwnd, [Out] char[] text, int maxCount);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(nint hwnd);

    public const int SwMinimize = 6;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(nint hwnd, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [LibraryImport("shell32.dll", EntryPoint = "ExtractIconExW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint ExtractIconEx(string file, int index, nint[]? large, nint[]? small, uint icons);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(nint icon);

    /// <summary>DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2: coordinates of other processes' windows in physical pixels.</summary>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetProcessDpiAwarenessContext(nint context);

    public const nint PerMonitorAwareV2 = -4;

    public static long Style(nint hwnd) => GetWindowLongPtr(hwnd, GwlStyle);

    public static long ExStyle(nint hwnd) => GetWindowLongPtr(hwnd, GwlExStyle);

    public static string Title(nint hwnd)
    {
        char[] buffer = new char[512];
        int length = GetWindowText(hwnd, buffer, buffer.Length);
        return new string(buffer, 0, Math.Max(0, length));
    }

    /// <summary>Visible top-level windows of <paramref name="processId"/>, from the top of the z-order down.</summary>
    public static List<nint> TopLevelWindows(int processId)
    {
        List<nint> windows = [];
        EnumWindows(
            (hwnd, _) =>
            {
                GetWindowThreadProcessId(hwnd, out uint owner);
                if (owner == (uint)processId && IsWindowVisible(hwnd))
                {
                    windows.Add(hwnd);
                }

                return true;
            },
            0);
        return windows;
    }

    /// <summary>
    /// Whether Windows shows a taskbar button for the window: visible and either <c>WS_EX_APPWINDOW</c> or an unowned
    /// window without <c>WS_EX_TOOLWINDOW</c> (the documented taskbar rules).
    /// </summary>
    public static bool HasTaskbarButton(nint hwnd)
    {
        long ex = ExStyle(hwnd);
        return IsWindowVisible(hwnd) && ((ex & WsExAppWindow) != 0 || (GetWindow(hwnd, GwOwner) == 0 && (ex & WsExToolWindow) == 0));
    }

    /// <summary>The icon the window shows (title bar / taskbar), or 0.</summary>
    public static nint WindowIcon(nint hwnd, bool big)
    {
        nint icon = 0;
        SendMessageTimeout(hwnd, WmGetIcon, big ? IconBig : IconSmall2, 0, 0x0002, 2000, out icon);
        if (icon == 0 && !big)
        {
            SendMessageTimeout(hwnd, WmGetIcon, IconSmall, 0, 0x0002, 2000, out icon);
        }

        return icon != 0 ? icon : GetClassLongPtr(hwnd, big ? GclpHIcon : GclpHIconSm);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X;
        public int Y;
    }

    [LibraryImport("user32.dll")]
    public static partial nint MonitorFromPoint(Point point, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out Point point);

    [LibraryImport("user32.dll")]
    public static partial nint WindowFromPoint(Point point);

    /// <summary>The system cursors of a resize grip (T-120): left / right, top / bottom, top left / bottom right, top right / bottom left.</summary>
    public const int IdcSizeWe = 32644;
    public const int IdcSizeNs = 32645;
    public const int IdcSizeNwse = 32642;
    public const int IdcSizeNesw = 32643;

    [StructLayout(LayoutKind.Sequential)]
    public struct CursorInfo
    {
        public int Size;
        public uint Flags;
        public nint Cursor;
        public Point ScreenPosition;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorInfo(ref CursorInfo info);

    [LibraryImport("user32.dll", EntryPoint = "LoadCursorW")]
    public static partial nint LoadCursor(nint instance, nint name);

    /// <summary>The cursor shown now (a shared system cursor has the same handle in every process), or 0.</summary>
    public static nint CurrentCursor()
    {
        CursorInfo info = new() { Size = Marshal.SizeOf<CursorInfo>() };
        return GetCursorInfo(ref info) ? info.Cursor : 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GuiThreadInfo
    {
        public int Size;
        public uint Flags;
        public nint Active;
        public nint Focus;
        public nint Capture;
        public nint MenuOwner;
        public nint MoveSize;
        public nint Caret;
        public Rect CaretRect;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);

    /// <summary>
    /// The Win32 active and focus windows of the thread that owns <paramref name="hwnd"/> (T-120: where keys go, apart
    /// from what UI Automation reports), for a report.
    /// </summary>
    public static string ThreadFocus(nint hwnd)
    {
        uint thread = GetWindowThreadProcessId(hwnd, out _);
        GuiThreadInfo info = new() { Size = Marshal.SizeOf<GuiThreadInfo>() };
        return GetGUIThreadInfo(thread, ref info)
            ? $"Win32 active 0x{info.Active:X}, focus 0x{info.Focus:X} (window 0x{hwnd:X}, owner 0x{GetWindow(hwnd, GwOwner):X}, foreground 0x{GetForegroundWindow():X})"
            : "Win32 focus unknown (GetGUIThreadInfo failed)";
    }

    /// <summary>The handle of the shared system cursor <paramref name="id"/> (<c>IDC_*</c>).</summary>
    public static nint SystemCursor(int id) => LoadCursor(0, id);

    [LibraryImport("user32.dll")]
    public static partial nint GetAncestor(nint hwnd, uint flags);

    /// <summary>GA_ROOT: the top-level window of a child window.</summary>
    public const uint GaRoot = 2;

    /// <summary>The top-level window that a click at <paramref name="point"/> would hit (0 when none).</summary>
    public static nint TopLevelWindowAt(System.Drawing.Point point)
    {
        nint hwnd = WindowFromPoint(new Point { X = point.X, Y = point.Y });
        return hwnd == 0 ? 0 : GetAncestor(hwnd, GaRoot);
    }

    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint FindWindow(string? className, string? windowName);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int GetClassName(nint hwnd, [Out] char[] name, int maxCount);

    [LibraryImport("user32.dll")]
    public static partial int GetSystemMetrics(int index);

    public static string ClassName(nint hwnd)
    {
        char[] buffer = new char[256];
        int length = GetClassName(hwnd, buffer, buffer.Length);
        return new string(buffer, 0, Math.Max(0, length));
    }

    /// <summary>The bounds of all monitors together, in physical pixels.</summary>
    public static Rect VirtualScreen()
    {
        int left = GetSystemMetrics(SmXVirtualScreen);
        int top = GetSystemMetrics(SmYVirtualScreen);
        return new Rect { Left = left, Top = top, Right = left + GetSystemMetrics(SmCxVirtualScreen), Bottom = top + GetSystemMetrics(SmCyVirtualScreen) };
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ClientToScreen(nint hwnd, ref Point point);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmGetWindowAttribute(nint hwnd, uint attribute, out Rect value, int size);

    /// <summary>The primary monitor (the one at 0,0) and its work area, in physical pixels.</summary>
    public static MonitorInfo PrimaryMonitor()
    {
        MonitorInfo info = new() { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromPoint(default, 1), ref info))
        {
            throw new InvalidOperationException("GetMonitorInfo failed.");
        }

        return info;
    }

    /// <summary>The visible window frame (DWMWA_EXTENDED_FRAME_BOUNDS, without the invisible resize borders).</summary>
    public static Rect FrameBounds(nint hwnd)
    {
        int result = DwmGetWindowAttribute(hwnd, 9, out Rect frame, Marshal.SizeOf<Rect>());
        if (result != 0)
        {
            Marshal.ThrowExceptionForHR(result);
        }

        return frame;
    }

    /// <summary>The client area in screen coordinates.</summary>
    public static Rect VisibleClientRect(nint hwnd)
    {
        if (!GetClientRect(hwnd, out Rect client))
        {
            throw new InvalidOperationException("GetClientRect failed.");
        }

        Point origin = default;
        ClientToScreen(hwnd, ref origin);
        return new Rect { Left = origin.X, Top = origin.Y, Right = origin.X + client.Width, Bottom = origin.Y + client.Height };
    }

    /// <summary>The monitor and work area of the monitor the window is on, in physical pixels.</summary>
    public static MonitorInfo MonitorOf(nint hwnd)
    {
        MonitorInfo info = new() { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(hwnd, MonitorDefaultToNearest), ref info))
        {
            throw new InvalidOperationException("GetMonitorInfo failed.");
        }

        return info;
    }
}
