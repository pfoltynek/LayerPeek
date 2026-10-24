using System.Drawing;
using System.Runtime.InteropServices;

using FlaUI.Core.Input;

namespace LayerPeek.App.E2E.Tests.Infrastructure;

/// <summary>
/// The "other app" of the always-on-top checks (T-071): a plain magenta popup window of the test process – another
/// process than the app, never a window of the user. Not topmost, no taskbar button, served by its own
/// <see cref="MessageLoopThread{T}"/>; disposing destroys it on that thread (it dies with the test process at the latest).
/// </summary>
internal sealed partial class CoverWindow : IDisposable
{
    /// <summary>The colour of the window: rare in the camera picture, easy to tell in a capture.</summary>
    public static readonly Color Color = Color.FromArgb(255, 0, 255);

    private const string ClassName = "LayerPeek.E2E.CoverWindow";
    private const string Title = "LayerPeek E2E – cover window";

    private const uint WsPopup = 0x8000_0000;
    private const uint WsExToolWindow = 0x0000_0080;
    private const int SwShowNoActivate = 4;
    private const int ErrorClassAlreadyExists = 1410;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const int IdcArrow = 32512;

    private static readonly Lock ClassLock = new();
    private static bool _classRegistered;

    private readonly MessageLoopThread<nint> _loop;

    private CoverWindow(MessageLoopThread<nint> loop) => _loop = loop;

    public nint Handle => _loop.State;

    /// <summary>Creates the window over <paramref name="bounds"/> (physical pixels), shown without activation.</summary>
    public static async Task<CoverWindow> CreateAsync(Native.Rect bounds)
    {
        RegisterClass();
        MessageLoopThread<nint> loop = await MessageLoopThread<nint>.StartAsync(
            "E2E cover window",
            () => CreateShown(bounds),
            static hwnd => DestroyWindow(hwnd),
            ApartmentState.STA);
        return new CoverWindow(loop);
    }

    /// <summary>
    /// The window for a camera window with the visible <paramref name="frame"/>: the frame grown by
    /// <paramref name="margin"/> on every side, so that a strip of it stays clickable around a camera window on top,
    /// clipped to the camera's <paramref name="monitor"/>.
    /// </summary>
    public static Native.Rect BoundsFor(Native.Rect frame, Native.Rect monitor, int margin) => new()
    {
        Left = Math.Max(monitor.Left, frame.Left - margin),
        Top = Math.Max(monitor.Top, frame.Top - margin),
        Right = Math.Min(monitor.Right, frame.Right + margin),
        Bottom = Math.Min(monitor.Bottom, frame.Bottom + margin),
    };

    /// <summary>Where a click may hit the window: its centre, then the middle of each edge strip and its corners (8 px in).</summary>
    public static List<Point> ClickCandidates(Native.Rect bounds)
    {
        int left = bounds.Left + 8;
        int top = bounds.Top + 8;
        int right = bounds.Right - 9;
        int bottom = bounds.Bottom - 9;
        int x = bounds.Left + (bounds.Width / 2);
        int y = bounds.Top + (bounds.Height / 2);
        return [new(x, y), new(x, top), new(left, y), new(right, y), new(x, bottom), new(left, top), new(right, top), new(left, bottom), new(right, bottom)];
    }

    /// <summary>Whether <paramref name="upper"/> is above <paramref name="lower"/> in the z-order of the top-level windows.</summary>
    public static bool IsAbove(nint upper, nint lower)
    {
        int upperIndex = -1;
        int lowerIndex = -1;
        int index = 0;
        Native.EnumWindows(
            (hwnd, _) =>
            {
                upperIndex = hwnd == upper ? index : upperIndex;
                lowerIndex = hwnd == lower ? index : lowerIndex;
                index++;
                return upperIndex < 0 || lowerIndex < 0;
            },
            0);
        return upperIndex >= 0 && lowerIndex >= 0 && upperIndex < lowerIndex;
    }

    /// <summary>
    /// Brings the window to the top of the non-topmost windows and activates it like a user switching to another app:
    /// <c>SetWindowPos(HWND_TOP)</c> and <c>SetForegroundWindow</c>; when Windows refuses the foreground to the test
    /// process, a click of the real mouse into a part of the window nothing covers.
    /// <para>
    /// A window that was just shown (the camera window of the test) can still activate itself a moment later and take the
    /// foreground back (T-073: once, right after the camera window opened); then all of it is tried again, up to three
    /// times.
    /// </para>
    /// </summary>
    public async Task BringToFrontAsync()
    {
        for (int attempt = 1; ; attempt++)
        {
            SetWindowPos(Handle, 0, 0, 0, 0, 0, SwpNoMove | SwpNoSize);
            if (!Native.SetForegroundWindow(Handle) || Native.GetForegroundWindow() != Handle)
            {
                Assert.True(Native.GetWindowRect(Handle, out Native.Rect bounds));
                Point? target = ClickCandidates(bounds).Cast<Point?>().FirstOrDefault(point => Native.TopLevelWindowAt(point!.Value) == Handle)
                    ?? throw new InvalidOperationException($"No part of the cover window {bounds} can be clicked: other windows cover all of it.");
                Mouse.Position = target.Value;
                Mouse.Click(MouseButton.Left);
            }

            try
            {
                await Wait.UntilAsync(() => Native.GetForegroundWindow() == Handle, TimeSpan.FromSeconds(2), "the cover window in the foreground");
                // Still in the foreground a moment later: not taken back by a window that activates itself late.
                await Task.Delay(300);
                if (Native.GetForegroundWindow() == Handle)
                {
                    return;
                }
            }
            catch (WaitTimeoutException)
            {
                // Tried again below, or reported with the window that has the foreground.
            }

            if (attempt == 3)
            {
                nint foreground = Native.GetForegroundWindow();
                throw new InvalidOperationException(
                    $"The cover window did not keep the foreground after three attempts; the foreground is 0x{foreground:X} "
                    + $"({NotificationCenter.ProcessNameOf(foreground)}, class '{Native.ClassName(foreground)}').");
            }
        }
    }

    public void Dispose() => _loop.Dispose();

    private static unsafe nint CreateShown(Native.Rect bounds)
    {
        nint hwnd;
        fixed (char* className = ClassName)
        fixed (char* title = Title)
        {
            hwnd = CreateWindowEx(WsExToolWindow, className, title, WsPopup, bounds.Left, bounds.Top, bounds.Width, bounds.Height, 0, 0, GetModuleHandle(null), 0);
        }

        if (hwnd == 0)
        {
            throw new InvalidOperationException($"CreateWindowEx failed ({Marshal.GetLastPInvokeError()}).");
        }

        ShowWindow(hwnd, SwShowNoActivate);
        return hwnd;
    }

    private static unsafe void RegisterClass()
    {
        lock (ClassLock)
        {
            if (_classRegistered)
            {
                return;
            }

            fixed (char* className = ClassName)
            {
                WndClassEx windowClass = new()
                {
                    Size = (uint)sizeof(WndClassEx),
                    WindowProcedure = (nint)(delegate* unmanaged<nint, uint, nint, nint, nint>)&WindowProcedure,
                    Instance = GetModuleHandle(null),
                    Cursor = LoadCursor(0, IdcArrow),
                    // COLORREF 0x00BBGGRR; one brush for the class, released with the test process.
                    Background = CreateSolidBrush((uint)(Color.B << 16 | Color.G << 8 | Color.R)),
                    ClassName = className,
                };
                if (RegisterClassEx(&windowClass) == 0 && Marshal.GetLastPInvokeError() != ErrorClassAlreadyExists)
                {
                    throw new InvalidOperationException($"RegisterClassEx failed ({Marshal.GetLastPInvokeError()}).");
                }
            }

            _classRegistered = true;
        }
    }

    /// <summary>Default handling only: the window lives until the test disposes it (destroyed on its own thread).</summary>
    [UnmanagedCallersOnly]
    private static nint WindowProcedure(nint hwnd, uint message, nint wParam, nint lParam) => DefWindowProc(hwnd, message, wParam, lParam);

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct WndClassEx
    {
        public uint Size;
        public uint Style;
        public nint WindowProcedure;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public char* MenuName;
        public char* ClassName;
        public nint SmallIcon;
    }

    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true)]
    private static unsafe partial ushort RegisterClassEx(WndClassEx* windowClass);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true)]
    private static unsafe partial nint CreateWindowEx(uint exStyle, char* className, char* windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static partial nint DefWindowProc(nint hwnd, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint hwnd, int command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int width, int height, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "LoadCursorW")]
    private static partial nint LoadCursor(nint instance, nint name);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateSolidBrush(uint color);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandle(string? moduleName);
}
