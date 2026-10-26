using LayerPeek.App.Cameras.Overlay;
using LayerPeek.App.Composition;
using LayerPeek.App.Tests.Cameras.Overlay;
using LayerPeek.Core.Cameras.Overlay;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.Tests.Cameras.Layout;

/// <summary>
/// Records the layouts the layout mode saves (T-091) and raises <see cref="LayoutSaved"/> like the real writer after a
/// successful write. A save can be held open (<see cref="Hold"/>), made to report "not written" or to throw. Confirmations
/// ("Done" with the saved layout) are recorded apart (<see cref="Confirms"/>) and raise <see cref="LayoutConfirmed"/> unless
/// <see cref="ConfirmWrites"/>. Nothing on disk.
/// </summary>
internal sealed class FakeLayoutWriter : ICameraOverlayLayoutWriter
{
    private readonly Lock _gate = new();
    private readonly List<(string PrinterId, CameraOverlayLayout Layout)> _saves = [];
    private readonly List<(string PrinterId, CameraOverlayLayout Layout)> _confirms = [];
    private TaskCompletionSource<bool>? _held;

    public event EventHandler<CameraOverlayLayoutSavedEventArgs>? LayoutSaved;

    public event EventHandler<CameraOverlayLayoutSavedEventArgs>? LayoutConfirmed;

    /// <summary>What a save returns (unless held or failing).</summary>
    public bool Result { get; set; } = true;

    /// <summary>A confirmation finds another layout in the file and writes (announced as a save).</summary>
    public bool ConfirmWrites { get; set; }

    public IReadOnlyList<(string PrinterId, CameraOverlayLayout Layout)> Confirms
    {
        get
        {
            lock (_gate)
            {
                return [.. _confirms];
            }
        }
    }

    public int Confirmed { get; private set; }

    /// <summary>Thrown by the next saves (until reset to <see langword="null"/>).</summary>
    public Exception? Failure { get; set; }

    public IReadOnlyList<(string PrinterId, CameraOverlayLayout Layout)> Saves
    {
        get
        {
            lock (_gate)
            {
                return [.. _saves];
            }
        }
    }

    public int Announced { get; private set; }

    /// <summary>The next saves wait until <see cref="Release"/>.</summary>
    public void Hold()
    {
        lock (_gate)
        {
            _held = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    /// <summary>Completes the held saves with <paramref name="result"/>.</summary>
    public void Release(bool result)
    {
        TaskCompletionSource<bool>? held;
        lock (_gate)
        {
            held = _held;
            _held = null;
        }

        held?.TrySetResult(result);
    }

    public async Task<bool> SaveAsync(string printerId, CameraOverlayLayout layout, CancellationToken cancellationToken)
    {
        TaskCompletionSource<bool>? held;
        lock (_gate)
        {
            _saves.Add((printerId, layout));
            held = _held;
        }

        if (Failure is { } failure)
        {
            throw failure;
        }

        bool written = held is null ? Result : await held.Task.ConfigureAwait(false);
        if (written)
        {
            Announced++;
            LayoutSaved?.Invoke(this, new CameraOverlayLayoutSavedEventArgs(printerId, layout));
        }

        return written;
    }

    public async Task<bool> ConfirmAsync(string printerId, CameraOverlayLayout layout, CancellationToken cancellationToken)
    {
        TaskCompletionSource<bool>? held;
        lock (_gate)
        {
            _confirms.Add((printerId, layout));
            held = _held;
        }

        if (Failure is { } failure)
        {
            throw failure;
        }

        if (held is not null)
        {
            await held.Task.ConfigureAwait(false);
        }

        if (ConfirmWrites)
        {
            Announced++;
            LayoutSaved?.Invoke(this, new CameraOverlayLayoutSavedEventArgs(printerId, layout));
            return true;
        }

        Confirmed++;
        LayoutConfirmed?.Invoke(this, new CameraOverlayLayoutSavedEventArgs(printerId, layout));
        return false;
    }
}

/// <summary>Test widgets with the ids and names of the real widgets of v1 (T-091 tests need the default layout drawn).</summary>
internal static class LayoutTestWidgets
{
    /// <summary>The five widgets of v1 as <see cref="TestOverlayWidget"/>s; <paramref name="sizes"/> per id (default: all three).</summary>
    public static OverlayWidgetRegistry Registry(IReadOnlyDictionary<string, OverlayWidgetSize[]>? sizes = null, params string[] ids)
    {
        string[] registered = ids.Length == 0 ? [.. OverlayWidgetIds.Known] : ids;
        return new([.. registered.Select(id => Definition(id, sizes?.GetValueOrDefault(id)))]);
    }

    public static OverlayWidgetDefinition Definition(string id, IReadOnlyList<OverlayWidgetSize>? sizes = null) =>
        new(id, OverlayWidgetNames.KeyFor(id) ?? OverlayWidgetNames.Status, 0, TestOverlayWidgets.Template, static _ => new TestOverlayWidget(), new OverlayWidgetMinWidths(120, 180, 240), sizes);
}
