namespace LayerPeek.App.Platform.Windows;

/// <summary>What <see cref="ForegroundRaiser.Raise"/> achieved.</summary>
internal enum ForegroundRaiseResult
{
    /// <summary>The window was the foreground window already.</summary>
    AlreadyForeground,

    /// <summary>The window is the foreground window now.</summary>
    Foreground,

    /// <summary>Windows refused the foreground; the window was only moved to the top of the z-order of its band.</summary>
    RaisedOnly,
}

/// <summary>
/// The window manager functions <see cref="ForegroundRaiser"/> needs (T-119): <see cref="ForegroundNative"/> in the app,
/// a fake in tests. Calls never throw for an invalid handle (Win32 just fails).
/// </summary>
internal interface IForegroundWindowApi
{
    nint GetForegroundWindow();

    /// <summary>The thread that created <paramref name="hwnd"/>; 0 for an invalid handle.</summary>
    uint GetWindowThreadId(nint hwnd);

    /// <summary>Whether the thread of <paramref name="hwnd"/> stopped handling messages (attaching to it could hang the UI thread).</summary>
    bool IsHung(nint hwnd);

    /// <summary><c>AttachThreadInput</c>: shares (or stops sharing) the input state of two threads.</summary>
    bool AttachThreadInput(uint thread, uint toThread, bool attach);

    /// <summary><c>BringWindowToTop</c>: the top of the z-order of the window's band (topmost or not), activating it if allowed.</summary>
    bool BringWindowToTop(nint hwnd);

    bool SetForegroundWindow(nint hwnd);

    /// <summary>
    /// <c>SetWindowPos(HWND_TOP, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE)</c>: the top of the z-order of the window's band,
    /// without activating it and without touching its topmost state.
    /// </summary>
    bool RaiseWithoutActivation(nint hwnd);
}

/// <summary>
/// Brings a window of the app in front of the other apps' windows on request of the user (T-119: "Bring camera to front"
/// of the tray menu, a second Settings… / Diagnostics… / About, a second start of the app). A plain
/// <c>SetForegroundWindow</c> (all Avalonia's <c>Window.Activate</c> does on Windows) is refused by the foreground lock
/// while another process owns the foreground and the app did not get the last input (e.g. an item invoked through
/// UI Automation, or a pipe command): then the window stays behind and only its taskbar button flashes.
/// <para>
/// The input state of the window's thread is attached for the moment of the call to the thread of the foreground window
/// (<c>AttachThreadInput</c>): with the shared input state Windows treats the call like one of the foreground thread and
/// lets it pass. It is always detached again; not attached when the foreground window is the app's own thread, no
/// window has the foreground, or the foreground thread hangs (attaching would block the UI thread too). Refused even so
/// (2026-10-31: Windows denied attaching to the foreground thread of VMware Workstation; a zero mouse input did not
/// help either and was removed), the window is at least moved to the top of the z-order without activation: Windows
/// keeps it directly below the foreground window, above all others (accepted by the user 2026-10-31). Only <c>HWND_TOP</c> is used, never <c>HWND_TOPMOST</c> / <c>HWND_NOTOPMOST</c>: the window keeps
/// its "always on top" state (Avalonia's <c>Window.Topmost</c>, which the taskbar button and the Dock icon follow, T-102)
/// exactly as it was. UI thread (the window's own thread). Pure logic over <see cref="IForegroundWindowApi"/>.
/// </para>
/// </summary>
internal static class ForegroundRaiser
{
    public static ForegroundRaiseResult Raise(IForegroundWindowApi api, nint hwnd)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentOutOfRangeException.ThrowIfZero(hwnd);
        nint foreground = api.GetForegroundWindow();
        if (foreground == hwnd)
        {
            return ForegroundRaiseResult.AlreadyForeground;
        }

        uint windowThread = api.GetWindowThreadId(hwnd);
        uint foregroundThread = foreground != 0 && !api.IsHung(foreground) ? api.GetWindowThreadId(foreground) : 0;
        bool attached = windowThread != 0
            && foregroundThread != 0
            && foregroundThread != windowThread
            && api.AttachThreadInput(foregroundThread, windowThread, attach: true);
        try
        {
            _ = api.BringWindowToTop(hwnd);
            _ = api.SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached)
            {
                _ = api.AttachThreadInput(foregroundThread, windowThread, attach: false);
            }
        }

        if (api.GetForegroundWindow() == hwnd)
        {
            return ForegroundRaiseResult.Foreground;
        }

        _ = api.RaiseWithoutActivation(hwnd);
        return ForegroundRaiseResult.RaisedOnly;
    }
}
