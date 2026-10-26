using LayerPeek.Core.Cameras.Overlay;

namespace LayerPeek.App.Cameras.Layout;

/// <summary>
/// Starts the layout mode of a printer's camera window from outside the window (T-091, user decisions 2026-10-25): the
/// "Edit layout in camera window…" button of the settings window (T-092). Implemented by the camera windows of the app and
/// registered explicitly in the composition root. UI thread only.
/// </summary>
internal interface ICameraLayoutModeLauncher
{
    /// <summary>
    /// Opens the camera window of <paramref name="printerId"/> (or activates it and switches it to the normal window, from
    /// the mini preview and full screen; it stays there afterwards) and starts its layout mode with
    /// <paramref name="startingLayout"/> as the draft — the unsaved layout of the settings window — or with the saved layout
    /// when <see langword="null"/>. A window smaller than 480 × 300 is enlarged to 640 × 360 for the time of the mode (moved
    /// into the work area when needed) and gets its size and position back afterwards; the temporary size is never
    /// remembered. When the window is in the layout mode already, it is only activated and the edit goes on.
    /// </summary>
    /// <returns>
    /// Whether the window is open in the layout mode (not when the printer has no enabled camera or the app is quitting).
    /// </returns>
    bool Start(string printerId, CameraOverlayLayout? startingLayout);
}
