using LayerPeek.Core.Printers;

namespace LayerPeek.Core.Cameras.Timelapse;

/// <summary>
/// The layer trigger (T-159, experimental): a frame when capturing first starts, one at every <c>N</c>-th layer change that
/// <see cref="LayerChangeDetector"/> estimates from the printer's Z axis (user decision 2026-11-04: <c>N</c> configurable,
/// 1 by default), and one when the print ends (<see cref="PrinterState.Finished"/> or <see cref="PrinterState.Stopped"/>:
/// the finished part, the end of the print lifts the head away). Without Z data (no PrusaLink) only those two frames.
/// No timer: it works only on the snapshots the service hands over, so a sample allocates nothing.
/// </summary>
/// <remarks>
/// Not capturing (a paused print or a printer waiting for the user) asks for nothing and withdraws a request still waiting
/// for its key frame (<see cref="ICameraTimelapse.CancelFrameRequest"/>). Unlike the time trigger, resuming takes no frame
/// of its own: the layer printed before the pause goes on, and its change takes the next frame. A frame is the camera's next
/// key frame (up to ~2.4 s later on the Buddy3D, T-158), so the head is somewhere on the new layer, not parked.
/// </remarks>
internal sealed class CameraTimelapseLayerTrigger : ICameraTimelapseTrigger
{
    private readonly ICameraTimelapse _timelapse;
    private readonly int _everyNthLayer;
    private readonly LayerChangeDetector _detector = new();

    private bool _capturing;
    private bool _started;
    private bool _disposed;
    private PrinterState _lastState = PrinterState.Unknown;

    public CameraTimelapseLayerTrigger(ICameraTimelapse timelapse, int everyNthLayer)
    {
        ArgumentNullException.ThrowIfNull(timelapse);
        ArgumentOutOfRangeException.ThrowIfLessThan(everyNthLayer, 1);
        _timelapse = timelapse;
        _everyNthLayer = everyNthLayer;
    }

    /// <summary>A frame every this many layer changes.</summary>
    public int EveryNthLayer => _everyNthLayer;

    public void SetCapturing(bool capturing)
    {
        if (_disposed || capturing == _capturing)
        {
            return;
        }

        _capturing = capturing;
        if (!capturing)
        {
            _timelapse.CancelFrameRequest();
        }
        else if (!_started)
        {
            // The first frame of the timelapse: the bed before the print (or the print so far, started by hand).
            _started = true;
            _timelapse.RequestFrame();
        }
    }

    public void OnSnapshot(PrinterSnapshot snapshot)
    {
        if (_disposed)
        {
            return;
        }

        PrinterState previous = _lastState;
        _lastState = snapshot.State;
        if (_detector.Observe(snapshot))
        {
            if (_capturing && _detector.LayerChanges % _everyNthLayer == 0)
            {
                _timelapse.RequestFrame();
            }

            return;
        }

        // The last layer has no change of its own: the end of the print takes the frame of the finished part.
        if (snapshot.State is PrinterState.Finished or PrinterState.Stopped && previous.HasActiveJob())
        {
            _timelapse.RequestFrame();
        }
    }

    public void Dispose() => _disposed = true;
}
