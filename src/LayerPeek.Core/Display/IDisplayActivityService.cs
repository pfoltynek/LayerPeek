namespace LayerPeek.Core.Display;

/// <summary>
/// Whether the user can see the screen at all (T-186): not while the session is locked or the display is off. Camera
/// windows stop taking frames meanwhile; recording, the dashcam, timelapses and print failure detection do not follow it.
/// Implemented per platform in App (Windows: <c>WindowsDisplayActivityService</c>; macOS: T-187, until then
/// <see cref="AlwaysVisibleDisplayActivity"/>).
/// </summary>
public interface IDisplayActivityService
{
    /// <summary>The screen can be seen: the session is unlocked and the display is on (dimmed counts as on).</summary>
    bool IsScreenVisible { get; }

    /// <summary>
    /// <see cref="IsScreenVisible"/> changed; raised on any thread (the Windows implementation: its own message thread).
    /// Handlers read <see cref="IsScreenVisible"/> again and must return quickly.
    /// </summary>
    event EventHandler? ScreenVisibilityChanged;
}

/// <summary>The screen is always visible (platforms without an implementation yet, tests).</summary>
public sealed class AlwaysVisibleDisplayActivity : IDisplayActivityService
{
    public static readonly AlwaysVisibleDisplayActivity Instance = new();

    private AlwaysVisibleDisplayActivity()
    {
    }

    public bool IsScreenVisible => true;

    public event EventHandler? ScreenVisibilityChanged
    {
        add { }
        remove { }
    }
}
