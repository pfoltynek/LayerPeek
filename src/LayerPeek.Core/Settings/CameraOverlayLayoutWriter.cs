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
    /// Raised by <see cref="ConfirmAsync"/> when the confirmed layout was not written (the file holds it already), on the
    /// thread that completed the call; handlers marshal to their own thread. An open settings window takes the layout over
    /// like a saved one and drops its unsaved layout of the printer: what the user confirmed in the camera window applies to
    /// the settings window too (T-092 follow-up, user decision 2026-10-25). Kept apart from <see cref="LayoutSaved"/>, which
    /// stays raised only for a write. A failing handler is logged and does not affect the other handlers or the result.
    /// </summary>
    event EventHandler<CameraOverlayLayoutSavedEventArgs>? LayoutConfirmed;

    /// <summary>
    /// Writes <paramref name="layout"/> as the overlay of printer <paramref name="printerId"/> and nothing else
    /// (<see cref="ISettingsStore.SetCameraOverlayLayoutAsync"/>), then raises <see cref="LayoutSaved"/>.
    /// </summary>
    /// <returns><see langword="true"/> when the file was written and the layout announced.</returns>
    /// <exception cref="IOException">The settings could not be read or written.</exception>
    /// <exception cref="UnauthorizedAccessException">Access to the settings location was denied.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<bool> SaveAsync(string printerId, CameraOverlayLayout layout, CancellationToken cancellationToken);

    /// <summary>
    /// "Done" of the layout mode with a layout equal to the saved one as far as the caller knows (nothing to write, e.g. the
    /// saved layout restored): the user confirmed <paramref name="layout"/> for printer <paramref name="printerId"/>. Like
    /// <see cref="SaveAsync"/>, the layout is written when the file holds another one (a settings save meanwhile) and
    /// <see cref="LayoutSaved"/> is raised; otherwise nothing is written and <see cref="LayoutConfirmed"/> is raised — also
    /// when the file cannot take the layout (no file, no such printer camera, a newer format), as the caller had nothing to
    /// write anyway.
    /// </summary>
    /// <returns><see langword="true"/> when the file was written (announced by <see cref="LayoutSaved"/>).</returns>
    /// <exception cref="IOException">The settings could not be read or written.</exception>
    /// <exception cref="UnauthorizedAccessException">Access to the settings location was denied.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<bool> ConfirmAsync(string printerId, CameraOverlayLayout layout, CancellationToken cancellationToken);
}

/// <summary>
/// The layout of printer <see cref="PrinterId"/> that was written (<see cref="ICameraOverlayLayoutWriter.LayoutSaved"/>) or
/// confirmed without a write (<see cref="ICameraOverlayLayoutWriter.LayoutConfirmed"/>).
/// </summary>
public sealed class CameraOverlayLayoutSavedEventArgs(string printerId, CameraOverlayLayout layout) : EventArgs
{
    /// <summary>The printer id passed to the writer, trimmed; compare case-insensitively.</summary>
    public string PrinterId { get; } = printerId ?? throw new ArgumentNullException(nameof(printerId));

    public CameraOverlayLayout Layout { get; } = layout ?? throw new ArgumentNullException(nameof(layout));
}

/// <summary>The <see cref="ICameraOverlayLayoutWriter"/> over an <see cref="ISettingsStore"/>.</summary>
/// <remarks>
/// Each <see cref="LayoutSaved"/> and <see cref="LayoutConfirmed"/> handler is isolated: an exception is logged and the other handlers still run, and the
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

    public event EventHandler<CameraOverlayLayoutSavedEventArgs>? LayoutConfirmed;

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

        Raise(LayoutSaved, nameof(LayoutSaved), new CameraOverlayLayoutSavedEventArgs(id, layout));
        return true;
    }

    public async Task<bool> ConfirmAsync(string printerId, CameraOverlayLayout layout, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerId);
        ArgumentNullException.ThrowIfNull(layout);

        // The store writes only when the file holds another layout; written or not, the user confirmed this one.
        string id = printerId.Trim();
        bool written = await _store.SetCameraOverlayLayoutAsync(id, layout, cancellationToken).ConfigureAwait(false);
        CameraOverlayLayoutSavedEventArgs confirmed = new(id, layout);
        if (written)
        {
            Raise(LayoutSaved, nameof(LayoutSaved), confirmed);
        }
        else
        {
            Raise(LayoutConfirmed, nameof(LayoutConfirmed), confirmed);
        }

        return written;
    }

    private void Raise(EventHandler<CameraOverlayLayoutSavedEventArgs>? handlers, string eventName, CameraOverlayLayoutSavedEventArgs saved)
    {
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
                LogSubscriberFailed(ex, eventName, saved.PrinterId);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "A {EventName} handler failed for the camera overlay layout of printer '{PrinterId}'.")]
    private partial void LogSubscriberFailed(Exception exception, string eventName, string printerId);
}
