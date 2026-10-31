using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.Core.Cameras;

namespace LayerPeek.Core.Settings;

/// <summary>
/// Saves the "always on top" option of one printer's camera window outside the settings window (the pin of the camera
/// window, T-117) and announces it, like <see cref="ICameraOverlayLayoutWriter"/> does for the overlay layout: the running
/// settings and an open settings window take over only that value and keep their other unsaved changes.
/// </summary>
public interface ICameraWindowTopmostWriter
{
    /// <summary>
    /// Raised after an option was written to the settings file, or found there already (the confirmation of the layout
    /// writer, <see cref="ICameraOverlayLayoutWriter.ConfirmAsync"/>: e.g. a save of the settings window wrote it before the
    /// running settings took it over, or the file was edited by hand; the running settings and an open settings window
    /// catch up). On the thread that completed the call (not the UI thread); handlers marshal to their own thread. Not
    /// raised when the file was left alone. A failing handler is logged and does not affect the other handlers or the result.
    /// </summary>
    event EventHandler<CameraWindowTopmostSavedEventArgs>? TopmostSaved;

    /// <summary>
    /// Writes <paramref name="topmost"/> as the option of <paramref name="mode"/> of printer <paramref name="printerId"/> and
    /// nothing else (<see cref="ISettingsStore.SetCameraWindowTopmostAsync"/>), then raises <see cref="TopmostSaved"/>.
    /// </summary>
    /// <returns><see langword="true"/> when the file holds the option now (written or already there) and it was announced.</returns>
    /// <exception cref="IOException">The settings could not be read or written.</exception>
    /// <exception cref="UnauthorizedAccessException">Access to the settings location was denied.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is full screen, which has no such option.</exception>
    Task<bool> SaveAsync(string printerId, CameraWindowMode mode, bool topmost, CancellationToken cancellationToken);
}

/// <summary>The "always on top" option of printer <see cref="PrinterId"/> in <see cref="Mode"/> that the settings file holds now.</summary>
public sealed class CameraWindowTopmostSavedEventArgs : EventArgs
{
    public CameraWindowTopmostSavedEventArgs(string printerId, CameraWindowMode mode, bool topmost)
    {
        ArgumentNullException.ThrowIfNull(printerId);
        if (mode is not (CameraWindowMode.Normal or CameraWindowMode.Mini))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Only the normal window and the mini preview have an always-on-top option.");
        }

        PrinterId = printerId;
        Mode = mode;
        Topmost = topmost;
    }

    /// <summary>The printer id passed to the writer, trimmed; compare case-insensitively.</summary>
    public string PrinterId { get; }

    /// <summary><see cref="CameraWindowMode.Normal"/> or <see cref="CameraWindowMode.Mini"/>.</summary>
    public CameraWindowMode Mode { get; }

    public bool Topmost { get; }
}

/// <summary>The <see cref="ICameraWindowTopmostWriter"/> over an <see cref="ISettingsStore"/>.</summary>
/// <remarks>
/// Each <see cref="TopmostSaved"/> handler is isolated: an exception is logged and the other handlers still run, and the
/// save, already completed, still reports success.
/// </remarks>
public sealed partial class CameraWindowTopmostWriter : ICameraWindowTopmostWriter
{
    private readonly ISettingsStore _store;
    private readonly ILogger<CameraWindowTopmostWriter> _logger;

    /// <param name="store">The settings the option is written to.</param>
    /// <param name="logger">Receives failures of <see cref="TopmostSaved"/> handlers; <see langword="null"/> = discarded.</param>
    public CameraWindowTopmostWriter(ISettingsStore store, ILogger<CameraWindowTopmostWriter>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _logger = logger ?? NullLogger<CameraWindowTopmostWriter>.Instance;
    }

    public event EventHandler<CameraWindowTopmostSavedEventArgs>? TopmostSaved;

    public async Task<bool> SaveAsync(string printerId, CameraWindowMode mode, bool topmost, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerId);

        // Trimmed like the store matches it and like the loaded settings hold it (see CameraOverlayLayoutWriter).
        string id = printerId.Trim();
        CameraWindowTopmostSavedEventArgs saved = new(id, mode, topmost);
        // Written or already there: either way the file holds what the user chose, so the running settings follow.
        if (await _store.SetCameraWindowTopmostAsync(id, mode, topmost, cancellationToken).ConfigureAwait(false) == CameraWindowTopmostStoreResult.LeftAlone)
        {
            return false;
        }

        if (TopmostSaved is { } handlers)
        {
            // One by one, so that a failing subscriber neither starves the others nor turns the completed save into a failure.
            foreach (EventHandler<CameraWindowTopmostSavedEventArgs> handler in Delegate.EnumerateInvocationList(handlers))
            {
                try
                {
                    handler(this, saved);
                }
                catch (Exception ex)
                {
                    LogSubscriberFailed(ex, id);
                }
            }
        }

        return true;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "A TopmostSaved handler failed for the camera window of printer '{PrinterId}'.")]
    private partial void LogSubscriberFailed(Exception exception, string printerId);
}
