using Microsoft.Extensions.Logging;

using LayerPeek.App.Settings;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.Cameras;

/// <summary>
/// The app's <see cref="ICameraWindowTopmostWriter"/> (T-117), like <see cref="Layout.DeferredCameraOverlayLayoutWriter"/>: a
/// <see cref="CameraWindowTopmostWriter"/> over the settings store, created on the first save. Creating the store may fail
/// (no per-user data folder); that save then throws an <see cref="InvalidOperationException"/> and the camera window
/// switches back. Handlers subscribed before the first save are handed to the inner writer, which isolates and logs failing
/// ones. A save (the write and its announcement) holds the <see cref="SettingsWriteGate"/> shared with the settings
/// window's saves, so that a save of the settings window either reads the announced option or is written before it.
/// </summary>
internal sealed class DeferredCameraWindowTopmostWriter : ICameraWindowTopmostWriter
{
    private readonly Func<ISettingsStore> _store;
    private readonly ILogger<CameraWindowTopmostWriter> _logger;
    private readonly SettingsWriteGate? _writeGate;
    private readonly Lock _gate = new();
    private readonly List<EventHandler<CameraWindowTopmostSavedEventArgs>> _handlers = [];
    private CameraWindowTopmostWriter? _writer;

    /// <param name="store">The settings store; called on the first save (on the caller's thread), may throw.</param>
    /// <param name="logger">Receives failures of <see cref="TopmostSaved"/> handlers.</param>
    /// <param name="writeGate">Held while an option is written and announced; <see langword="null"/>: none.</param>
    public DeferredCameraWindowTopmostWriter(Func<ISettingsStore> store, ILogger<CameraWindowTopmostWriter> logger, SettingsWriteGate? writeGate = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(logger);
        _store = store;
        _logger = logger;
        _writeGate = writeGate;
    }

    /// <inheritdoc/>
    public event EventHandler<CameraWindowTopmostSavedEventArgs>? TopmostSaved
    {
        add
        {
            if (value is null)
            {
                return;
            }

            lock (_gate)
            {
                _handlers.Add(value);
                if (_writer is { } writer)
                {
                    writer.TopmostSaved += value;
                }
            }
        }

        remove
        {
            if (value is null)
            {
                return;
            }

            lock (_gate)
            {
                _handlers.Remove(value);
                if (_writer is { } writer)
                {
                    writer.TopmostSaved -= value;
                }
            }
        }
    }

    /// <inheritdoc/>
    public async Task<bool> SaveAsync(string printerId, CameraWindowMode mode, bool topmost, CancellationToken cancellationToken)
    {
        CameraWindowTopmostWriter writer = Writer();
        if (_writeGate is null)
        {
            return await writer.SaveAsync(printerId, mode, topmost, cancellationToken).ConfigureAwait(false);
        }

        await _writeGate.EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await writer.SaveAsync(printerId, mode, topmost, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Exit();
        }
    }

    private CameraWindowTopmostWriter Writer()
    {
        lock (_gate)
        {
            if (_writer is null)
            {
                CameraWindowTopmostWriter writer = new(_store(), _logger);
                foreach (EventHandler<CameraWindowTopmostSavedEventArgs> handler in _handlers)
                {
                    writer.TopmostSaved += handler;
                }

                _writer = writer;
            }

            return _writer;
        }
    }
}
