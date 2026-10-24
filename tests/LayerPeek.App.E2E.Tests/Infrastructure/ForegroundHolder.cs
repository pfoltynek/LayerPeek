using System.Collections.Concurrent;
using System.Runtime.InteropServices;

using FlaUI.Core.Input;

namespace LayerPeek.App.E2E.Tests.Infrastructure;

/// <summary>
/// A small window of the test process itself (T-072) that takes the foreground when it is clicked with the real mouse, so
/// that the test process has the right to pass the foreground on (it is the foreground process and received the last
/// input) – the situation of a user double-clicking a shortcut. It can also lock the foreground
/// (<c>LockSetForegroundWindow</c>), so that no other process may take it without new user input, and it receives the
/// shell hook messages (<c>RegisterShellHookWindow</c>), among them <c>HSHELL_FLASH</c> when a window flashes its taskbar
/// button. Topmost tool window without a taskbar button, on its own thread with a message loop; disposing unlocks the
/// foreground and destroys it (it dies with the test process at the latest).
/// </summary>
internal sealed partial class ForegroundHolder : IDisposable
{
    /// <summary>HSHELL_FLASH: a window flashes its taskbar button (HSHELL_REDRAW | HSHELL_HIGHBIT).</summary>
    public const int ShellFlash = 0x8006;

    /// <summary>HSHELL_RUDEAPPACTIVATED: a window was activated.</summary>
    public const int ShellRudeAppActivated = 0x8004;

    private const string ClassName = "LayerPeek.E2E.ForegroundHolder";
    private const string Title = "LayerPeek E2E – foreground holder";

    private const uint WmDestroy = 0x0002;
    private const uint WmClose = 0x0010;
    private const uint WsOverlapped = 0x0000_0000;
    private const uint WsCaption = 0x00C0_0000;
    private const uint WsExTopmost = 0x0000_0008;
    private const uint WsExToolWindow = 0x0000_0080;
    private const int SwShowNoActivate = 4;
    private const int ErrorClassAlreadyExists = 1410;
    private const uint LsfwLock = 1;
    private const uint LsfwUnlock = 2;

    private static readonly ConcurrentDictionary<nint, ForegroundHolder> Holders = new();
    private static readonly Lock ClassLock = new();
    private static bool _classRegistered;
    private static uint _shellHookMessage;

    private readonly Thread _thread;
    private readonly ConcurrentQueue<ShellEvent> _shellEvents = new();
    private bool _locked;

    private ForegroundHolder(Thread thread, nint handle)
    {
        _thread = thread;
        Handle = handle;
    }

    public nint Handle { get; }

    /// <summary>The shell hook messages received so far, oldest first.</summary>
    public IReadOnlyList<ShellEvent> ShellEvents => [.. _shellEvents];

    /// <summary>Creates the window in the lower left corner of the primary monitor's work area (shown, not activated).</summary>
    public static unsafe ForegroundHolder Create()
    {
        RegisterClass();
        Native.Rect work = Native.PrimaryMonitor().Work;
        TaskCompletionSource<nint> created = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() =>
        {
            nint hwnd;
            fixed (char* className = ClassName)
            fixed (char* title = Title)
            {
                hwnd = CreateWindowEx(
                    WsExTopmost | WsExToolWindow,
                    className,
                    title,
                    WsOverlapped | WsCaption,
                    work.Left + 40,
                    work.Bottom - 200,
                    320,
                    160,
                    0,
                    0,
                    GetModuleHandle(null),
                    0);
            }

            if (hwnd == 0)
            {
                created.SetException(new InvalidOperationException($"CreateWindowEx failed ({Marshal.GetLastPInvokeError()})."));
                return;
            }

            if (!RegisterShellHookWindow(hwnd))
            {
                DestroyWindow(hwnd);
                created.SetException(new InvalidOperationException($"RegisterShellHookWindow failed ({Marshal.GetLastPInvokeError()})."));
                return;
            }

            ShowWindow(hwnd, SwShowNoActivate);
            created.SetResult(hwnd);
            Msg message;
            while (GetMessage(&message, 0, 0, 0) > 0)
            {
                TranslateMessage(&message);
                DispatchMessage(&message);
            }
        })
        {
            IsBackground = true,
            Name = "E2E foreground holder",
        };
        thread.SetApartmentState(ApartmentState.STA);

        thread.Start();
        nint handle = created.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        // Shell messages that arrive before this line are dropped; the tests clear them before they launch anything.
        ForegroundHolder holder = new(thread, handle);
        Holders[handle] = holder;
        return holder;
    }

    /// <summary>
    /// Clicks the window's client area with the real mouse (the pointer jumps there, like the tray tests) and waits until
    /// the window is the foreground window.
    /// </summary>
    public async Task ActivateWithMouseAsync()
    {
        Native.Rect client = Native.VisibleClientRect(Handle);
        System.Drawing.Point center = new(client.Left + (client.Width / 2), client.Top + (client.Height / 2));
        // Never click whatever covers the window (another topmost window of the user): the click must hit this window.
        nint hit = Native.TopLevelWindowAt(center);
        if (hit != Handle)
        {
            throw new InvalidOperationException(
                $"Another window (0x{hit:X} '{Native.ClassName(hit)}') covers the foreground holder at {center.X},{center.Y}; not clicking it.");
        }

        Mouse.Position = center;
        Mouse.Click(MouseButton.Left);
        Native.GetCursorPos(out Native.Point cursor);
        if (cursor.X < client.Left || cursor.X >= client.Right || cursor.Y < client.Top || cursor.Y >= client.Bottom)
        {
            throw new InvalidOperationException(
                $"The mouse pointer left the foreground holder {client} right after the click (it is at {cursor.X},{cursor.Y}): someone used the mouse during the end-to-end run.");
        }

        await Wait.UntilAsync(() => Native.GetForegroundWindow() == Handle, TimeSpan.FromSeconds(5), "the foreground holder in the foreground after the click");
    }

    /// <summary>
    /// Locks the foreground (the test process must be the foreground process): no other process may take it until the
    /// user presses Alt or clicks another window. Unlocked again by <see cref="Dispose"/>.
    /// </summary>
    public void LockForeground()
    {
        if (!LockSetForegroundWindow(LsfwLock))
        {
            throw new InvalidOperationException($"LockSetForegroundWindow failed ({Marshal.GetLastPInvokeError()}); the test process is not in the foreground.");
        }

        _locked = true;
    }

    /// <summary>
    /// Passes the foreground right of the test process on to <paramref name="processId"/> (<c>AllowSetForegroundWindow</c>;
    /// the test process must be the foreground process), like the second start does for the running instance.
    /// </summary>
    public static void AllowForeground(int processId)
    {
        if (!AllowSetForegroundWindow((uint)processId))
        {
            throw new InvalidOperationException($"AllowSetForegroundWindow failed ({Marshal.GetLastPInvokeError()}); the test process is not in the foreground.");
        }
    }

    /// <summary>Forgets the shell messages received so far.</summary>
    public void ClearShellEvents() => _shellEvents.Clear();

    public void Dispose()
    {
        if (_locked)
        {
            LockSetForegroundWindow(LsfwUnlock);
            _locked = false;
        }

        if (_thread.IsAlive)
        {
            PostMessage(Handle, WmClose, 0, 0);
            _thread.Join(TimeSpan.FromSeconds(5));
        }

        Holders.TryRemove(Handle, out _);
    }

    private static unsafe void RegisterClass()
    {
        lock (ClassLock)
        {
            if (_classRegistered)
            {
                return;
            }

            _shellHookMessage = RegisterWindowMessage("SHELLHOOK");
            if (_shellHookMessage == 0)
            {
                throw new InvalidOperationException($"RegisterWindowMessage(SHELLHOOK) failed ({Marshal.GetLastPInvokeError()}).");
            }

            fixed (char* className = ClassName)
            {
                WndClassEx windowClass = new()
                {
                    Size = (uint)sizeof(WndClassEx),
                    WindowProcedure = (nint)(delegate* unmanaged<nint, uint, nint, nint, nint>)&WindowProcedure,
                    Instance = GetModuleHandle(null),
                    Cursor = LoadCursor(0, 32512),
                    // COLOR_WINDOW + 1
                    Background = 6,
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

    [UnmanagedCallersOnly]
    private static nint WindowProcedure(nint hwnd, uint message, nint wParam, nint lParam)
    {
        if (message == _shellHookMessage && _shellHookMessage != 0)
        {
            if (Holders.TryGetValue(hwnd, out ForegroundHolder? holder))
            {
                holder._shellEvents.Enqueue(new ShellEvent((int)wParam, lParam, DateTime.UtcNow));
            }

            return 0;
        }

        switch (message)
        {
            case WmClose:
                DeregisterShellHookWindow(hwnd);
                DestroyWindow(hwnd);
                return 0;
            case WmDestroy:
                PostQuitMessage(0);
                return 0;
            default:
                return DefWindowProc(hwnd, message, wParam, lParam);
        }
    }

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

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public nint Window;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public Native.Point Point;
        public uint Private;
    }

    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true)]
    private static unsafe partial ushort RegisterClassEx(WndClassEx* windowClass);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true)]
    private static unsafe partial nint CreateWindowEx(uint exStyle, char* className, char* windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static partial nint DefWindowProc(nint hwnd, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")]
    private static unsafe partial int GetMessage(Msg* message, nint hwnd, uint filterMin, uint filterMax);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool TranslateMessage(Msg* message);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static unsafe partial nint DispatchMessage(Msg* message);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessage(nint hwnd, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial void PostQuitMessage(int exitCode);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint hwnd, int command);

    [LibraryImport("user32.dll", EntryPoint = "LoadCursorW")]
    private static partial nint LoadCursor(nint instance, nint name);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandle(string? moduleName);

    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial uint RegisterWindowMessage(string name);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterShellHookWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeregisterShellHookWindow(nint hwnd);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LockSetForegroundWindow(uint lockCode);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(uint processId);
}

/// <summary>A shell hook message (<c>HSHELL_*</c> code and window) and when it arrived.</summary>
internal readonly record struct ShellEvent(int Code, nint Window, DateTime ReceivedUtc);
