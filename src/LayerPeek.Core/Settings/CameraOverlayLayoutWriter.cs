using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
    /// handlers marshal to their own thread. Not raised when nothing was written. A failing handler is logged and does not
    /// affect the other handlers or the result of the save.
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
    /// <summary>The printer id passed to <see cref="ICameraOverlayLayoutWriter.SaveAsync"/>, trimmed; compare case-insensitively.</summary>
    public string PrinterId { get; } = printerId ?? throw new ArgumentNullException(nameof(printerId));

    public CameraOverlayLayout Layout { get; } = layout ?? throw new ArgumentNullException(nameof(layout));
}

/// <summary>The <see cref="ICameraOverlayLayoutWriter"/> over an <see cref="ISettingsStore"/>.</summary>
/// <remarks>
/// Each <see cref="LayoutSaved"/> handler is isolated: an exception is logged and the other handlers still run, and the
/// save, already completed, still reports success.
/// </remarks>
public sealed partial class CameraOverlayLayoutWriter : ICameraOverlayLayoutWriter
{
    private readonly ISettingsStore _store;
    private readonly ILogger<CameraOverlayLayoutWriter> _logger;

    /// <param name="store">The settings the layout is written to.</param>
    /// <param name="logger">Receives failures of <see cref="LayoutSaved"/> handlers; <see langword="null"/> = discarded.</param>
    public CameraOverlayLayoutWriter(ISettingsStore store, ILogger<CameraOverlayLayoutWriter>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _logger = logger ?? NullLogger<CameraOverlayLayoutWriter>.Instance;
    }

    public event EventHandler<CameraOverlayLayoutSavedEventArgs>? LayoutSaved;

    public async Task<bool> SaveAsync(string printerId, CameraOverlayLayout layout, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerId);
        ArgumentNullException.ThrowIfNull(layout);

        // Trimmed like the store matches it and like the loaded settings hold it, so that the announced id finds the
        // printer (AppSettings.WithCameraOverlayLayout compares without trimming).
        string id = printerId.Trim();
        if (!await _store.SetCameraOverlayLayoutAsync(id, layout, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        RaiseLayoutSaved(new CameraOverlayLayoutSavedEventArgs(id, layout));
        return true;
    }

    private void RaiseLayoutSaved(CameraOverlayLayoutSavedEventArgs saved)
    {
        EventHandler<CameraOverlayLayoutSavedEventArgs>? handlers = LayoutSaved;
        if (handlers is null)
        {
            return;
        }

        // One by one, so that a failing subscriber neither starves the others nor turns the completed save into a failure.
        foreach (EventHandler<CameraOverlayLayoutSavedEventArgs> handler in Delegate.EnumerateInvocationList(handlers))
        {
            try
            {
                handler(this, saved);
            }
            catch (Exception ex)
            {
                LogSubscriberFailed(ex, saved.PrinterId);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "A LayoutSaved handler failed for the camera overlay layout of printer '{PrinterId}'.")]
    private partial void LogSubscriberFailed(Exception exception, string printerId);
}
