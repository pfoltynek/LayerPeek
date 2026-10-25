using Microsoft.Extensions.Logging;

using LayerPeek.App.Settings;
using LayerPeek.Core.Cameras.Overlay;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.Cameras.Layout;

/// <summary>
/// The app's <see cref="ICameraOverlayLayoutWriter"/> (T-091): a <see cref="CameraOverlayLayoutWriter"/> over the settings
/// store, created on the first save. Creating the store may fail (no per-user data folder); that save then throws an
/// <see cref="InvalidOperationException"/> and the layout mode shows its error. Handlers subscribed before the first save
/// are handed to the inner writer, which isolates and logs failing ones. A save or a confirmation (the write or comparison
/// and its announcement) holds the <see cref="SettingsWriteGate"/> shared with the settings window's saves (T-092 follow-up).
/// </summary>
internal sealed class DeferredCameraOverlayLayoutWriter : ICameraOverlayLayoutWriter
{
    private readonly Func<ISettingsStore> _store;
    private readonly ILogger<CameraOverlayLayoutWriter> _logger;
    private readonly SettingsWriteGate? _writeGate;
    private readonly Lock _gate = new();
    private readonly List<EventHandler<CameraOverlayLayoutSavedEventArgs>> _savedHandlers = [];
    private readonly List<EventHandler<CameraOverlayLayoutSavedEventArgs>> _confirmedHandlers = [];
    private CameraOverlayLayoutWriter? _writer;

    /// <param name="store">The settings store; called on the first save (on the caller's thread), may throw.</param>
    /// <param name="logger">Receives failures of <see cref="LayoutSaved"/> handlers.</param>
    /// <param name="writeGate">
    /// Held while a layout is written and announced, shared with the settings window's saves (T-092 follow-up);
    /// <see langword="null"/>: none.
    /// </param>
    public DeferredCameraOverlayLayoutWriter(Func<ISettingsStore> store, ILogger<CameraOverlayLayoutWriter> logger, SettingsWriteGate? writeGate = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(logger);
        _store = store;
        _logger = logger;
        _writeGate = writeGate;
    }

    /// <inheritdoc/>
    public event EventHandler<CameraOverlayLayoutSavedEventArgs>? LayoutSaved
    {
        add => Subscribe(_savedHandlers, value, static (writer, handler) => writer.LayoutSaved += handler);
        remove => Unsubscribe(_savedHandlers, value, static (writer, handler) => writer.LayoutSaved -= handler);
    }

    /// <inheritdoc/>
    public event EventHandler<CameraOverlayLayoutSavedEventArgs>? LayoutConfirmed
    {
        add => Subscribe(_confirmedHandlers, value, static (writer, handler) => writer.LayoutConfirmed += handler);
        remove => Unsubscribe(_confirmedHandlers, value, static (writer, handler) => writer.LayoutConfirmed -= handler);
    }

    /// <inheritdoc/>
    public Task<bool> SaveAsync(string printerId, CameraOverlayLayout layout, CancellationToken cancellationToken) =>
        WithGateAsync(static (writer, id, layout, token) => writer.SaveAsync(id, layout, token), printerId, layout, cancellationToken);

    /// <inheritdoc/>
    /// <remarks>
    /// Holds the write gate like <see cref="SaveAsync"/>: the file is compared after a save of the settings window has
    /// finished, so a layout that save wrote meanwhile is replaced by the confirmed one, never kept behind a confirmation.
    /// </remarks>
    public Task<bool> ConfirmAsync(string printerId, CameraOverlayLayout layout, CancellationToken cancellationToken) =>
        WithGateAsync(static (writer, id, layout, token) => writer.ConfirmAsync(id, layout, token), printerId, layout, cancellationToken);

    private async Task<bool> WithGateAsync(
        Func<CameraOverlayLayoutWriter, string, CameraOverlayLayout, CancellationToken, Task<bool>> call,
        string printerId,
        CameraOverlayLayout layout,
        CancellationToken cancellationToken)
    {
        CameraOverlayLayoutWriter writer = Writer();
        if (_writeGate is null)
        {
            return await call(writer, printerId, layout, cancellationToken).ConfigureAwait(false);
        }

        await _writeGate.EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await call(writer, printerId, layout, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Exit();
        }
    }

    private void Subscribe(
        List<EventHandler<CameraOverlayLayoutSavedEventArgs>> handlers,
        EventHandler<CameraOverlayLayoutSavedEventArgs>? handler,
        Action<CameraOverlayLayoutWriter, EventHandler<CameraOverlayLayoutSavedEventArgs>> add)
    {
        if (handler is null)
        {
            return;
        }

        lock (_gate)
        {
            handlers.Add(handler);
            if (_writer is { } writer)
            {
                add(writer, handler);
            }
        }
    }

    private void Unsubscribe(
        List<EventHandler<CameraOverlayLayoutSavedEventArgs>> handlers,
        EventHandler<CameraOverlayLayoutSavedEventArgs>? handler,
        Action<CameraOverlayLayoutWriter, EventHandler<CameraOverlayLayoutSavedEventArgs>> remove)
    {
        if (handler is null)
        {
            return;
        }

        lock (_gate)
        {
            handlers.Remove(handler);
            if (_writer is { } writer)
            {
                remove(writer, handler);
            }
        }
    }

    private CameraOverlayLayoutWriter Writer()
    {
        lock (_gate)
        {
            if (_writer is null)
            {
                CameraOverlayLayoutWriter writer = new(_store(), _logger);
                foreach (EventHandler<CameraOverlayLayoutSavedEventArgs> handler in _savedHandlers)
                {
                    writer.LayoutSaved += handler;
                }

                foreach (EventHandler<CameraOverlayLayoutSavedEventArgs> handler in _confirmedHandlers)
                {
                    writer.LayoutConfirmed += handler;
                }

                _writer = writer;
            }

            return _writer;
        }
    }
}
