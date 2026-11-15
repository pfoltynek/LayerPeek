using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Avalonia.Controls;

namespace LayerPeek.App.Platform.Windows;

/// <summary>
/// Cloaked windows on Windows (T-186): <c>DwmGetWindowAttribute(DWMWA_CLOAKED)</c> when a watch starts, then the
/// <c>EVENT_OBJECT_CLOAKED</c> / <c>EVENT_OBJECT_UNCLOAKED</c> WinEvents, which Windows raises for every window it cloaks or
/// uncloaks, also when the user switches virtual desktops or moves the window to another one. The hook is out of context
/// (<c>WINEVENT_OUTOFCONTEXT</c>): its callback runs on the UI thread that set it, from its message loop, with no polling.
/// It listens to all processes (cloaking is done by the shell, so which process "raises" the event is not documented) and
/// filters by the watched windows; it exists only while at least one window is watched. UI thread only. Never throws.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe partial class WindowsWindowCloaking : IWindowCloaking
{
    private const uint EventObjectCloaked = 0x8017;
    private const uint EventObjectUncloaked = 0x8018;
    private const uint WinEventOutOfContext = 0;
    private const int ObjectIdWindow = 0;
    private const int ChildIdSelf = 0;
    private const uint DwmwaCloaked = 14;

    /// <summary>One per process (the hook callback is static).</summary>
    public static readonly WindowsWindowCloaking Shared = new();

    private readonly Dictionary<nint, Subscription> _watches = [];
    private nint _hook;

    private WindowsWindowCloaking()
    {
    }

    IDisposable IWindowCloaking.Watch(TopLevel window, Action<bool> cloakedChanged)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(cloakedChanged);
        if (window.TryGetPlatformHandle() is not { HandleDescriptor: "HWND", Handle: var hwnd } || hwnd == 0)
        {
            cloakedChanged(false);
            return new Subscription(this, 0, cloakedChanged);
        }

        Subscription watch = new(this, hwnd, cloakedChanged);
        _watches[hwnd] = watch;
        EnsureHook();
        cloakedChanged(IsCloaked(hwnd));
        return watch;
    }

    /// <summary>Whether Windows cloaks <paramref name="hwnd"/> now (any reason: another virtual desktop, the shell, the app).</summary>
    internal static bool IsCloaked(nint hwnd)
    {
        try
        {
            uint cloaked = 0;
            return DwmGetWindowAttribute(hwnd, DwmwaCloaked, &cloaked, sizeof(uint)) == 0 && cloaked != 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    private void EnsureHook()
    {
        if (_hook != 0)
        {
            return;
        }

        try
        {
            _hook = SetWinEventHook(
                EventObjectCloaked,
                EventObjectUncloaked,
                0,
                (nint)(delegate* unmanaged[Stdcall]<nint, uint, nint, int, int, uint, uint, void>)&OnWinEvent,
                0,
                0,
                WinEventOutOfContext);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            _hook = 0;
        }
    }

    private void Remove(Subscription watch)
    {
        if (watch.Window == 0 || !_watches.TryGetValue(watch.Window, out Subscription? current) || !ReferenceEquals(current, watch))
        {
            return;
        }

        _ = _watches.Remove(watch.Window);
        if (_watches.Count == 0 && _hook != 0)
        {
            try
            {
                _ = UnhookWinEvent(_hook);
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
            }

            _hook = 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void OnWinEvent(nint hook, uint eventType, nint hwnd, int objectId, int childId, uint eventThread, uint eventTime)
    {
        try
        {
            if (objectId == ObjectIdWindow && childId == ChildIdSelf && hwnd != 0 && Shared._watches.TryGetValue(hwnd, out Subscription? watch))
            {
                // The attribute, not the event type: it is the state now, also when events of a quick switch overtake each other.
                watch.Changed(IsCloaked(hwnd));
            }
        }
        catch (Exception)
        {
            // Never into the window manager: the view model only switches its stream.
        }
    }

    private sealed class Subscription(WindowsWindowCloaking owner, nint window, Action<bool> changed) : IDisposable
    {
        private bool _disposed;

        public nint Window { get; } = window;

        public Action<bool> Changed { get; } = changed;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                owner.Remove(this);
            }
        }
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmGetWindowAttribute(nint hwnd, uint attribute, void* value, uint size);

    [LibraryImport("user32.dll")]
    private static partial nint SetWinEventHook(uint eventMin, uint eventMax, nint module, nint callback, uint processId, uint threadId, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWinEvent(nint hook);
}
