using Avalonia;
using Avalonia.Controls;

namespace LayerPeek.App.Windows;

/// <summary>
/// Follows the size and state of one auxiliary window while it is open and tells which state to remember when it closes
/// (T-094). Only a size the user gave the window in its normal state counts: sizes the app set itself (the fit to the
/// work area before showing and after opening, see <see cref="OnAutomaticSize"/>), resizes for a DPI change and the size
/// of a maximized window never replace the remembered size, so a window shrunk only to fit a small screen keeps the
/// user's larger preference for the next, larger screen. Maximizing remembers the flag and keeps the last normal size
/// for restoring. Pure, testable without a screen; <see cref="WorkAreaFitter"/> feeds it from the window. UI thread only.
/// </summary>
internal sealed class AuxiliaryWindowSizeMemory
{
    /// <summary>
    /// Sizes closer than this (DIP) are the same: a size set in DIP comes back from the platform rounded to whole pixels
    /// (at most about 0.4 DIP off at 125 %).
    /// </summary>
    internal const double Tolerance = 1.0;

    private Size? _size;
    private bool _maximized;
    private Size? _automatic;

    // The last resize changed the remembered size: undone when it turns out to be the start of maximizing (Windows
    // reports the maximized size before the new state).
    private bool _lastResizeRemembered;
    private Size? _beforeLastResize;

    public AuxiliaryWindowSizeMemory(AuxiliaryWindowState remembered)
    {
        ArgumentNullException.ThrowIfNull(remembered);
        Remembered = remembered;
        _size = remembered.ClientSize is { } size && AuxiliaryWindowPlacement.IsUsable(size) ? size : null;
        _maximized = remembered.Maximized;
    }

    /// <summary>The state the window opened with.</summary>
    public AuxiliaryWindowState Remembered { get; }

    /// <summary>The state to remember now: the user's last normal size (or the one remembered before) and the maximized flag.</summary>
    public AuxiliaryWindowState Current => new(_size, _maximized);

    /// <summary>The app sized the window itself (fit to the work area): a resize to this size is not the user's.</summary>
    public void OnAutomaticSize(Size clientSize) => _automatic = clientSize;

    /// <summary>
    /// The window got a new client size. <paramref name="byUser"/>: the platform reports a resize by the user or without a
    /// reason (not one the app requested, not a DPI change). <paramref name="normal"/>: the window is in its normal state
    /// as far as the app knows yet.
    /// </summary>
    public void OnResized(Size clientSize, bool byUser, bool normal)
    {
        _lastResizeRemembered = false;
        if (!byUser || !normal || !AuxiliaryWindowPlacement.IsUsable(clientSize)
            || (_automatic is { } automatic && IsSame(clientSize, automatic))
            || (_size is { } size && IsSame(clientSize, size)))
        {
            return;
        }

        _beforeLastResize = _size;
        _size = new Size(Math.Round(clientSize.Width, 1), Math.Round(clientSize.Height, 1));
        _lastResizeRemembered = true;
    }

    /// <summary>The window state changed (maximized, restored, full screen, minimized).</summary>
    public void OnStateChanged(WindowState state)
    {
        if (_lastResizeRemembered && state is WindowState.Maximized or WindowState.FullScreen)
        {
            // That resize was the maximized size arriving before the state: not a normal size.
            _size = _beforeLastResize;
        }

        _lastResizeRemembered = false;
        if (state == WindowState.Maximized)
        {
            _maximized = true;
        }
        else if (state == WindowState.Normal)
        {
            _maximized = false;
        }
    }

    private static bool IsSame(Size a, Size b) => Math.Abs(a.Width - b.Width) < Tolerance && Math.Abs(a.Height - b.Height) < Tolerance;
}
