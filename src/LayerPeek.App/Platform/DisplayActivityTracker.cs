using LayerPeek.Core.Display;

namespace LayerPeek.App.Platform;

/// <summary>
/// Combines what the OS reports into "the screen can be seen" (T-186): the session is unlocked and the console display
/// is on (dimmed counts as on). In a remote session (T-252, Remote Desktop) the console display is not the one the user
/// looks at, so its state is ignored there (it is kept for the return to the console); the lock still counts. The
/// platform implementation feeds it (<see cref="Windows.WindowsDisplayActivityService"/>,
/// <see cref="MacOS.MacDisplayActivityService"/>);
/// <see cref="ScreenVisibilityChanged"/> is raised on the reporting thread, only when the result changed. Starts as
/// visible until the platform reports the current state (the lock state is queried when listening starts). Thread-safe.
/// </summary>
internal sealed class DisplayActivityTracker : IDisplayActivityService
{
    /// <summary><c>GUID_CONSOLE_DISPLAY_STATE</c>: the display is off.</summary>
    public const uint DisplayOff = 0;

    /// <summary><c>GUID_CONSOLE_DISPLAY_STATE</c>: the display is on.</summary>
    public const uint DisplayOn = 1;

    /// <summary><c>GUID_CONSOLE_DISPLAY_STATE</c>: the display is dimmed (still readable).</summary>
    public const uint DisplayDimmed = 2;

    /// <summary><c>WTSINFOEX_LEVEL1.SessionFlags</c>: the session is locked (Windows 8 and later; Windows 7 had it reversed).</summary>
    public const int SessionStateLock = 0;

    /// <summary><c>WTSINFOEX_LEVEL1.SessionFlags</c>: the session is unlocked.</summary>
    public const int SessionStateUnlock = 1;

    private readonly Lock _gate = new();
    private bool _locked;
    private bool _displayOff;
    private bool _remoteSession;

    public bool IsScreenVisible
    {
        get
        {
            lock (_gate)
            {
                return Visible;
            }
        }
    }

    // Under _gate.
    private bool Visible => !_locked && (_remoteSession || !_displayOff);

    public event EventHandler? ScreenVisibilityChanged;

    /// <summary>The session was locked (<see langword="true"/>) or unlocked.</summary>
    public void SetSessionLocked(bool locked) => Update(() => _locked = locked);

    /// <summary>The console display state (<see cref="DisplayOff"/>, <see cref="DisplayOn"/>, <see cref="DisplayDimmed"/>); unknown values count as on.</summary>
    public void SetConsoleDisplayState(uint state) => SetDisplayAsleep(state == DisplayOff);

    /// <summary>The display went to sleep (<see langword="true"/>) or woke up (macOS: all screens together).</summary>
    public void SetDisplayAsleep(bool asleep) => Update(() => _displayOff = asleep);

    /// <summary>
    /// The session is a remote one (<see langword="true"/>, T-252: Remote Desktop) or on the console: in a remote session
    /// the console display state does not hide the screen.
    /// </summary>
    public void SetRemoteSession(bool remote) => Update(() => _remoteSession = remote);

    /// <summary>
    /// Whether <c>WTSINFOEX_LEVEL1.SessionFlags</c> means a locked session: <see cref="SessionStateLock"/>; unlocked,
    /// unknown (<c>WTS_SESSIONSTATE_UNKNOWN</c>, -1) and anything else count as unlocked.
    /// </summary>
    public static bool IsLockedSession(int sessionFlags) => sessionFlags == SessionStateLock;

    private void Update(Action change)
    {
        bool changed;
        lock (_gate)
        {
            bool before = Visible;
            change();
            changed = before != Visible;
        }

        if (changed)
        {
            ScreenVisibilityChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
