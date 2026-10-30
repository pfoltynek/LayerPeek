namespace LayerPeek.App.Platform;

/// <summary>
/// Whether the app shows in the app switchers of the OS (T-102). macOS: the Dock icon and ⌘Tab (activation policy
/// regular) while at least one window that is not always on top is open, otherwise only the menu bar extra (accessory,
/// as at the start). Windows: nothing here, each window has its own taskbar button (<c>ShowInTaskbar</c>). Driven by
/// <see cref="LayerPeek.App.Windows.AppPresence"/>; UI thread; calls never throw.
/// </summary>
internal interface IAppPresence
{
    /// <summary>Shows the app in the Dock (<paramref name="visible"/>) or keeps it in the menu bar only.</summary>
    /// <param name="keepActive">
    /// Leaving the Dock while other windows (always on top, e.g. the mini preview) stay open: the app stays active if it
    /// was, so that the window the user just switched keeps the keyboard focus. <see langword="false"/> when no window is
    /// left (the system decides which app becomes active).
    /// </param>
    void SetVisible(bool visible, bool keepActive);
}

/// <summary>An OS without an app presence of its own (Windows: a taskbar button per window; tests).</summary>
internal sealed class NoAppPresence : IAppPresence
{
    public static readonly NoAppPresence Instance = new();

    public void SetVisible(bool visible, bool keepActive)
    {
    }
}
