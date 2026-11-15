using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using LayerPeek.Core.Display;

namespace LayerPeek.App.Platform.Windows;

/// <summary>
/// Whether the screen can be seen on Windows (T-186): the lock and unlock of this session
/// (<c>WTSRegisterSessionNotification</c>, <c>WM_WTSSESSION_CHANGE</c>) and the state of the console display
/// (<c>RegisterPowerSettingNotification</c> with <c>GUID_CONSOLE_DISPLAY_STATE</c>, <c>WM_POWERBROADCAST</c>).
/// </summary>
/// <remarks>
/// Both notifications go to a message-only window on a thread of its own with its own message loop (like
/// <c>SystemEvents</c>), so the service works from any calling thread, and <see cref="Dispose"/> always destroys the
/// window and its registrations on the thread that created them. <see cref="ScreenVisibilityChanged"/> is raised on that
/// thread. The current lock state is queried (<c>WTSQuerySessionInformationW</c>, <c>WTSSessionInfoEx</c>) when listening
/// starts, before and after registering: a camera window may open while the session is locked (T-151 reopens it when the
/// printer comes back). The display state is reported by the power setting registration itself at once. The window
/// procedure finds its instance by the window handle, so several instances (tests) never mix up their notifications.
/// Never throws.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed unsafe partial class WindowsDisplayActivityService : IDisplayActivityService, IDisposable
{
    private const string ClassName = "LayerPeek.DisplayActivity";
    private const uint WmDestroy = 0x0002;
    private const uint WmClose = 0x0010;
    private const uint WmPowerBroadcast = 0x0218;
    private const uint WmWtsSessionChange = 0x02B1;
    private const nint PbtPowerSettingChange = 0x8013;
    private const nint WtsSessionLock = 0x7;
    private const nint WtsSessionUnlock = 0x8;
    private const uint NotifyForThisSession = 0;
    private const uint DeviceNotifyWindowHandle = 0;
    private const int ErrorClassAlreadyExists = 1410;
    private const nint HwndMessage = -3;
    private const uint WtsCurrentSession = uint.MaxValue;
    private const int WtsSessionInfoEx = 25;
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    // GUID_CONSOLE_DISPLAY_STATE (winnt.h).
    private static readonly Guid ConsoleDisplayState = new(0x6fe69556, 0x704a, 0x47a0, 0x8f, 0x24, 0xc2, 0x8d, 0x93, 0x6f, 0xda, 0x47);

    // The window procedure is static: it finds the instance of a window here.
    private static readonly ConcurrentDictionary<nint, WindowsDisplayActivityService> s_windows = new();
    private static readonly Lock s_classGate = new();
    private static bool s_classRegistered;

    private readonly DisplayActivityTracker _tracker = new();
    private readonly TaskCompletionSource _listening = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _displayStateReported = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ManualResetEventSlim _ready = new();
    private readonly Lock _gate = new();

    // Guarded by _gate.
    private Thread? _thread;
    private bool _started;
    private bool _disposed;

    // Written by the window thread only (read by Dispose after it was published through _listening, and by tests).
    private nint _window;
    private nint _powerNotification;
    private bool _sessionRegistered;

    public bool IsScreenVisible => _tracker.IsScreenVisible;

    public event EventHandler? ScreenVisibilityChanged
    {
        add => _tracker.ScreenVisibilityChanged += value;
        remove => _tracker.ScreenVisibilityChanged -= value;
    }

    /// <summary>Completes when the window thread is listening, or gave up (then <see cref="IsListening"/> is false).</summary>
    internal Task Listening => _listening.Task;

    /// <summary>The message window exists and gets the display state (tests).</summary>
    internal bool IsListening => Volatile.Read(ref _window) != 0 && _powerNotification != 0;

    /// <summary>Completes when Windows reported the console display state for the first time (the registration does at once).</summary>
    internal Task DisplayStateReported => _displayStateReported.Task;

    /// <summary>The window thread runs (tests: none after <see cref="Dispose"/>).</summary>
    internal bool HasThread
    {
        get
        {
            lock (_gate)
            {
                return _thread is { IsAlive: true };
            }
        }
    }

    /// <summary>
    /// Starts listening on a thread of its own; any thread, returns at once after the current lock state was read. Later
    /// calls and calls after <see cref="Dispose"/> do nothing. Without the window the screen counts as visible, except a
    /// session found locked.
    /// </summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_started || _disposed)
            {
                return;
            }

            _started = true;
            ApplySessionLockState();
            _thread = new Thread(Run) { IsBackground = true, Name = "LayerPeek display activity" };
            _thread.Start();
        }
    }

    /// <summary>Stops listening and waits (bounded) until the window thread removed its window and registrations.</summary>
    public void Dispose()
    {
        Thread? thread;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            thread = _thread;
        }

        if (thread is null)
        {
            return;
        }

        if (ReferenceEquals(thread, Thread.CurrentThread))
        {
            // Disposed by a handler on the window thread: it cannot wait for itself; its loop closes the window next.
            if (Volatile.Read(ref _window) is var own and not 0)
            {
                _ = PostMessageW(own, WmClose, 0, 0);
            }

            return;
        }

        // One bound for the whole stop (app shutdown with a stuck window thread waits no longer than that).
        long started = Environment.TickCount64;
        // The window is published before Listening completes; a thread that failed has no window and ends by itself.
        if (_ready.Wait(StopTimeout) && Volatile.Read(ref _window) is var window and not 0)
        {
            try
            {
                _ = PostMessageW(window, WmClose, 0, 0);
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
            }
        }

        TimeSpan left = StopTimeout - TimeSpan.FromMilliseconds(Environment.TickCount64 - started);
        if (thread.Join(left > TimeSpan.Zero ? left : TimeSpan.Zero))
        {
            _ready.Dispose();
        }
    }

    /// <summary>
    /// <c>WTSINFOEX_LEVEL1.SessionFlags</c> of this session (<see cref="DisplayActivityTracker.IsLockedSession"/>), or
    /// <see langword="null"/> when Windows does not tell.
    /// </summary>
    internal static int? QuerySessionFlags()
    {
        try
        {
            if (!WTSQuerySessionInformationW(0, WtsCurrentSession, WtsSessionInfoEx, out nint buffer, out uint bytes))
            {
                return null;
            }

            try
            {
                SessionInfoEx* info = (SessionInfoEx*)buffer;
                return info is not null && bytes >= (uint)sizeof(SessionInfoEx) && info->Level == 1 ? info->SessionFlags : null;
            }
            finally
            {
                WTSFreeMemory(buffer);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    private void ApplySessionLockState()
    {
        if (QuerySessionFlags() is { } flags)
        {
            _tracker.SetSessionLocked(DisplayActivityTracker.IsLockedSession(flags));
        }
    }

    // The window thread: creates the window, registers, pumps until WM_CLOSE destroyed the window, then cleans up.
    private void Run()
    {
        try
        {
            if (!CreateMessageWindow())
            {
                return;
            }

            _sessionRegistered = WTSRegisterSessionNotification(_window, NotifyForThisSession);
            Guid setting = ConsoleDisplayState;
            _powerNotification = RegisterPowerSettingNotification(_window, &setting, DeviceNotifyWindowHandle);
            // A lock between the first query (Start) and the registration is not missed.
            ApplySessionLockState();
            _ready.Set();
            _listening.TrySetResult();
            lock (_gate)
            {
                if (_disposed)
                {
                    // Disposed while starting (its wait may have ended before the window existed): clean up right away.
                    return;
                }
            }

            Message message;
            while (GetMessageW(&message, 0, 0, 0) > 0)
            {
                _ = TranslateMessage(&message);
                _ = DispatchMessageW(&message);
            }
        }
        catch (Exception)
        {
            // Never crashes the process from its own thread (e.g. a throwing handler of the initial lock state).
        }
        finally
        {
            Unregister();
            if (_window != 0)
            {
                // Destroyed by WM_CLOSE normally; this covers a loop that ended otherwise.
                _ = DestroyWindow(_window);
                _ = s_windows.TryRemove(_window, out _);
                Volatile.Write(ref _window, 0);
            }

            _ready.Set();
            _listening.TrySetResult();
        }
    }

    private bool CreateMessageWindow()
    {
        nint instance = GetModuleHandleW(null);
        fixed (char* className = ClassName)
        {
            lock (s_classGate)
            {
                if (!s_classRegistered)
                {
                    WindowClass windowClass = new()
                    {
                        Size = (uint)sizeof(WindowClass),
                        WindowProcedure = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint>)&WindowProcedure,
                        Instance = instance,
                        ClassName = className,
                    };
                    if (RegisterClassExW(&windowClass) == 0 && Marshal.GetLastPInvokeError() != ErrorClassAlreadyExists)
                    {
                        return false;
                    }

                    s_classRegistered = true;
                }
            }

            nint window = CreateWindowExW(0, className, null, 0, 0, 0, 0, 0, HwndMessage, 0, instance, 0);
            if (window == 0)
            {
                return false;
            }

            s_windows[window] = this;
            Volatile.Write(ref _window, window);
            return true;
        }
    }

    // The window thread.
    private void Unregister()
    {
        try
        {
            if (_powerNotification != 0)
            {
                _ = UnregisterPowerSettingNotification(_powerNotification);
                _powerNotification = 0;
            }

            if (_sessionRegistered)
            {
                _ = WTSUnRegisterSessionNotification(_window);
                _sessionRegistered = false;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WindowProcedure(nint window, uint message, nint wParam, nint lParam)
    {
        try
        {
            if (s_windows.TryGetValue(window, out WindowsDisplayActivityService? current))
            {
                switch (message)
                {
                    case WmWtsSessionChange:
                        if (wParam == WtsSessionLock)
                        {
                            current._tracker.SetSessionLocked(true);
                        }
                        else if (wParam == WtsSessionUnlock)
                        {
                            current._tracker.SetSessionLocked(false);
                        }

                        return 0;

                    case WmPowerBroadcast when wParam == PbtPowerSettingChange && lParam != 0:
                        PowerBroadcastSetting* setting = (PowerBroadcastSetting*)lParam;
                        if (setting->PowerSetting == ConsoleDisplayState && setting->DataLength >= sizeof(uint))
                        {
                            current._tracker.SetConsoleDisplayState(setting->Data);
                            current._displayStateReported.TrySetResult();
                        }

                        return 1;

                    case WmClose:
                        // The registrations go before the window; then the loop ends (WM_DESTROY).
                        current.Unregister();
                        _ = DestroyWindow(window);
                        return 0;

                    case WmDestroy:
                        _ = s_windows.TryRemove(window, out _);
                        Volatile.Write(ref current._window, 0);
                        PostQuitMessage(0);
                        return 0;
                }
            }
        }
        catch (Exception)
        {
            // Never into the window manager: the handlers post to the UI thread and do not throw.
        }

        return DefWindowProcW(window, message, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowClass
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

    // POWERBROADCAST_SETTING with the first four bytes of its data (a DWORD for GUID_CONSOLE_DISPLAY_STATE).
    [StructLayout(LayoutKind.Sequential)]
    private struct PowerBroadcastSetting
    {
        public Guid PowerSetting;
        public uint DataLength;
        public uint Data;
    }

    // The start of WTSINFOEXW with WTSINFOEX_LEVEL1_W: the union starts at 8 (it holds a LARGE_INTEGER).
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct SessionInfoEx
    {
        [FieldOffset(0)]
        public uint Level;

        [FieldOffset(8)]
        public uint SessionId;

        [FieldOffset(12)]
        public int SessionState;

        [FieldOffset(16)]
        public int SessionFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public nint Window;
        public uint Id;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? moduleName);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial ushort RegisterClassExW(WindowClass* windowClass);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint CreateWindowExW(uint exStyle, char* className, char* windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessageW(nint window, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial void PostQuitMessage(int exitCode);

    [LibraryImport("user32.dll")]
    private static partial int GetMessageW(Message* message, nint window, uint filterMin, uint filterMax);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(Message* message);

    [LibraryImport("user32.dll")]
    private static partial nint DispatchMessageW(Message* message);

    [LibraryImport("user32.dll")]
    private static partial nint DefWindowProcW(nint window, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial nint RegisterPowerSettingNotification(nint recipient, Guid* powerSetting, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterPowerSettingNotification(nint handle);

    [LibraryImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WTSRegisterSessionNotification(nint window, uint flags);

    [LibraryImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WTSUnRegisterSessionNotification(nint window);

    [LibraryImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WTSQuerySessionInformationW(nint server, uint sessionId, int infoClass, out nint buffer, out uint bytesReturned);

    [LibraryImport("wtsapi32.dll")]
    private static partial void WTSFreeMemory(nint memory);
}
