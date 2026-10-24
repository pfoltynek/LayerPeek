using LayerPeek.Core.Cameras.Overlay;

namespace LayerPeek.Core.Settings;

/// <summary>
/// Saves the overlay layout of one printer outside the settings window (the layout mode of the camera window, T-091) and
/// announces it (T-077 Q3), so that an open settings window takes over only that layout and keeps its other unsaved changes
/// (T-092).
/// </summary>
public interface ICameraOverlayLayoutWriter
{
    /// <summary>
    /// Raised after a layout was written to the settings file, on the thread that completed the write (not the UI thread);
    /// handlers marshal to their own thread. Not raised when nothing was written.
    /// </summary>
    event EventHandler<CameraOverlayLayoutSavedEventArgs>? LayoutSaved;

    /// <summary>
    /// Writes <paramref name="layout"/> as the overlay of printer <paramref name="printerId"/> and nothing else
    /// (<see cref="ISettingsStore.SetCameraOverlayLayoutAsync"/>), then raises <see cref="LayoutSaved"/>.
    /// </summary>
    /// <returns><see langword="true"/> when the file was written and the layout announced.</returns>
    /// <exception cref="IOException">The settings could not be read or written.</exception>
    /// <exception cref="UnauthorizedAccessException">Access to the settings location was denied.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<bool> SaveAsync(string printerId, CameraOverlayLayout layout, CancellationToken cancellationToken);
}

/// <summary>The layout of printer <see cref="PrinterId"/> that was written (<see cref="ICameraOverlayLayoutWriter.LayoutSaved"/>).</summary>
public sealed class CameraOverlayLayoutSavedEventArgs(string printerId, CameraOverlayLayout layout) : EventArgs
{
    /// <summary>The printer id as passed to <see cref="ICameraOverlayLayoutWriter.SaveAsync"/>; compare case-insensitively.</summary>
    public string PrinterId { get; } = printerId ?? throw new ArgumentNullException(nameof(printerId));

    public CameraOverlayLayout Layout { get; } = layout ?? throw new ArgumentNullException(nameof(layout));
}

/// <summary>The <see cref="ICameraOverlayLayoutWriter"/> over an <see cref="ISettingsStore"/>.</summary>
public sealed class CameraOverlayLayoutWriter : ICameraOverlayLayoutWriter
{
    private readonly ISettingsStore _store;

    public CameraOverlayLayoutWriter(ISettingsStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public event EventHandler<CameraOverlayLayoutSavedEventArgs>? LayoutSaved;

    public async Task<bool> SaveAsync(string printerId, CameraOverlayLayout layout, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerId);
        ArgumentNullException.ThrowIfNull(layout);
        if (!await _store.SetCameraOverlayLayoutAsync(printerId, layout, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        LayoutSaved?.Invoke(this, new CameraOverlayLayoutSavedEventArgs(printerId, layout));
        return true;
    }
}
