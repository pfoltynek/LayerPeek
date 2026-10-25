using Microsoft.Extensions.Logging;

using LayerPeek.Core.Cameras.Overlay;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.Cameras.Layout;

/// <summary>
/// The app's <see cref="ICameraOverlayLayoutWriter"/> (T-091): a <see cref="CameraOverlayLayoutWriter"/> over the settings
/// store, created on the first save. Creating the store may fail (no per-user data folder); that save then throws an
/// <see cref="InvalidOperationException"/> and the layout mode shows its error. Handlers subscribed before the first save
/// are handed to the inner writer, which isolates and logs failing ones.
/// </summary>
internal sealed class DeferredCameraOverlayLayoutWriter : ICameraOverlayLayoutWriter
{
    private readonly Func<ISettingsStore> _store;
    private readonly ILogger<CameraOverlayLayoutWriter> _logger;
    private readonly Lock _gate = new();
    private readonly List<EventHandler<CameraOverlayLayoutSavedEventArgs>> _handlers = [];
    private CameraOverlayLayoutWriter? _writer;

    /// <param name="store">The settings store; called on the first save (on the caller's thread), may throw.</param>
    /// <param name="logger">Receives failures of <see cref="LayoutSaved"/> handlers.</param>
    public DeferredCameraOverlayLayoutWriter(Func<ISettingsStore> store, ILogger<CameraOverlayLayoutWriter> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(logger);
        _store = store;
        _logger = logger;
    }

    /// <inheritdoc/>
    public event EventHandler<CameraOverlayLayoutSavedEventArgs>? LayoutSaved
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
                    writer.LayoutSaved += value;
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
                    writer.LayoutSaved -= value;
                }
            }
        }
    }

    /// <inheritdoc/>
    public Task<bool> SaveAsync(string printerId, CameraOverlayLayout layout, CancellationToken cancellationToken) =>
        Writer().SaveAsync(printerId, layout, cancellationToken);

    private CameraOverlayLayoutWriter Writer()
    {
        lock (_gate)
        {
            if (_writer is null)
            {
                CameraOverlayLayoutWriter writer = new(_store(), _logger);
                foreach (EventHandler<CameraOverlayLayoutSavedEventArgs> handler in _handlers)
                {
                    writer.LayoutSaved += handler;
                }

                _writer = writer;
            }

            return _writer;
        }
    }
}
