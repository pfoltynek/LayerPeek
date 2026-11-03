using LayerPeek.App.Windows;

namespace LayerPeek.App.Cameras;

/// <summary>
/// The camera windows as the automatic close and reopen (T-151, <see cref="CameraWindowAutoVisibilityObserver"/>) drives them:
/// the real <see cref="CameraWindows"/> in the app, a fake in tests. UI thread only.
/// </summary>
internal interface ICameraWindowAutoTarget
{
    /// <summary>
    /// A camera window opened or closed, in any way; <see cref="CameraWindowOpenChangedEventArgs.Automatic"/> marks the close
    /// done by <see cref="CloseAutomatically"/>.
    /// </summary>
    event EventHandler<CameraWindowOpenChangedEventArgs>? OpenChanged;

    /// <summary>Whether an automatic close of an open window waits (its layout mode or context menu) changed.</summary>
    event EventHandler<CameraWindowCloseDeferralChangedEventArgs>? CloseDeferralChanged;

    /// <summary>The printers whose window is open now.</summary>
    IReadOnlyCollection<string> OpenPrinterIds { get; }

    /// <summary>
    /// Opens the window of <paramref name="printerId"/> like "Show camera" of the tray menu (last placement, the defaults of
    /// the printer), but without taking the focus or activating the app (user decision 2026-11-03 #1); an open window stays
    /// as it is.
    /// </summary>
    /// <returns>Whether a window of the printer is open now (not when its camera is disabled, or when quitting).</returns>
    bool ShowAutomatically(string printerId);

    /// <summary>
    /// Closes the open window of <paramref name="printerId"/> like its close button (placement remembered, stream stopped);
    /// the close is reported with <see cref="CameraWindowOpenChangedEventArgs.Automatic"/>.
    /// </summary>
    /// <returns>Whether a window was open and is closed now.</returns>
    bool CloseAutomatically(string printerId);

    /// <summary>Whether an automatic close of the open window of <paramref name="printerId"/> has to wait now.</summary>
    bool IsCloseDeferred(string printerId);
}

/// <summary>Whether an automatic close of the camera window of a printer waits changed (T-151).</summary>
internal sealed class CameraWindowCloseDeferralChangedEventArgs : EventArgs
{
    public CameraWindowCloseDeferralChangedEventArgs(string printerId, bool deferred)
    {
        PrinterId = printerId;
        Deferred = deferred;
    }

    public string PrinterId { get; }

    /// <summary>The layout mode is on or the context menu is open.</summary>
    public bool Deferred { get; }
}
