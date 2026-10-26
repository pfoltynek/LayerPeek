using LayerPeek.App.Cameras.Layout;
using LayerPeek.Core.Cameras.Overlay;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.Tests.Settings;

/// <summary>Records the starts of the layout mode (T-091) by "Edit layout in camera window…". Linked into the UI tests.</summary>
internal sealed class FakeLayoutModeLauncher : ICameraLayoutModeLauncher
{
    public List<(string PrinterId, CameraOverlayLayout? Layout)> Starts { get; } = [];

    public bool Result { get; set; } = true;

    public bool Start(string printerId, CameraOverlayLayout? startingLayout)
    {
        Starts.Add((printerId, startingLayout));
        return Result;
    }
}

/// <summary>Announces layouts like the layout mode of a camera window would after "Done" (T-077 Q3).</summary>
internal sealed class FakeLayoutAnnouncer : ICameraOverlayLayoutWriter
{
    private EventHandler<CameraOverlayLayoutSavedEventArgs>? _layoutSaved;
    private EventHandler<CameraOverlayLayoutSavedEventArgs>? _layoutConfirmed;

    public event EventHandler<CameraOverlayLayoutSavedEventArgs>? LayoutSaved
    {
        add
        {
            _layoutSaved += value;
            Subscribers++;
        }

        remove
        {
            _layoutSaved -= value;
            Subscribers--;
        }
    }

    public event EventHandler<CameraOverlayLayoutSavedEventArgs>? LayoutConfirmed
    {
        add
        {
            _layoutConfirmed += value;
            ConfirmSubscribers++;
        }

        remove
        {
            _layoutConfirmed -= value;
            ConfirmSubscribers--;
        }
    }

    public int Subscribers { get; private set; }

    public int ConfirmSubscribers { get; private set; }

    /// <summary>Raises <see cref="LayoutSaved"/> on the calling thread (the writer's thread in the app).</summary>
    public void Announce(string printerId, CameraOverlayLayout layout) => _layoutSaved?.Invoke(this, new CameraOverlayLayoutSavedEventArgs(printerId, layout));

    /// <summary>Raises <see cref="LayoutConfirmed"/> on the calling thread: "Done" with the saved layout, nothing written.</summary>
    public void Confirm(string printerId, CameraOverlayLayout layout) => _layoutConfirmed?.Invoke(this, new CameraOverlayLayoutSavedEventArgs(printerId, layout));

    public Task<bool> SaveAsync(string printerId, CameraOverlayLayout layout, CancellationToken cancellationToken)
    {
        Announce(printerId, layout);
        return Task.FromResult(true);
    }

    public Task<bool> ConfirmAsync(string printerId, CameraOverlayLayout layout, CancellationToken cancellationToken)
    {
        Confirm(printerId, layout);
        return Task.FromResult(false);
    }
}
