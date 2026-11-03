namespace LayerPeek.App.Cameras;

/// <summary>
/// The automatic close of the window of a switched-off printer (T-151, user decision 2026-11-03 #2) waits while the user
/// works in the window: in the layout mode (T-091) and while its context menu is open.
/// </summary>
public sealed partial class CameraViewModel
{
    private bool _contextMenuOpen;

    /// <summary>
    /// Whether an automatic close of this window waits (<see cref="LayerPeek.Core.Cameras.CameraWindowAutoVisibility.SetCloseDeferred"/>):
    /// the layout mode is on or the context menu is open. Raises <c>PropertyChanged</c> when it changes.
    /// </summary>
    public bool DefersAutoClose => IsLayoutModeActive || _contextMenuOpen;

    /// <summary>The window's context menu opened or closed (the window reports it).</summary>
    public void SetContextMenuOpen(bool open)
    {
        if (_contextMenuOpen == open)
        {
            return;
        }

        bool before = DefersAutoClose;
        _contextMenuOpen = open;
        if (DefersAutoClose != before)
        {
            OnPropertyChanged(nameof(DefersAutoClose));
        }
    }
}
