using LayerPeek.Core.Printers;

namespace LayerPeek.Core.Cameras.Timelapse;

/// <summary>
/// Decides when a running timelapse takes its frames (T-160): a strategy that the
/// <see cref="Recording.CameraRecordingService"/> creates for every timelapse it starts and disposes when the timelapse
/// ends, so that nothing of it exists while no timelapse runs. T-160 has the time trigger (a frame every interval); the
/// layer trigger of T-159 plugs in next to it and reads the layer change from <see cref="OnSnapshot"/>. A trigger asks for
/// frames with <see cref="ICameraTimelapse.RequestFrame"/> (never blocks, any thread).
/// </summary>
/// <remarks>
/// The service calls every member under its lock, one call at a time: they must be quick, must not block and must not call
/// back into the service, and they never throw. <see cref="IDisposable.Dispose"/> ends the trigger for good (it requests
/// nothing afterwards; a request racing with it is harmless).
/// </remarks>
public interface ICameraTimelapseTrigger : IDisposable
{
    /// <summary>
    /// Whether frames are taken now: <see langword="false"/> while the printer is paused or waits for the user
    /// (<see cref="PrinterState.Paused"/>, <see cref="PrinterState.Attention"/>; user decision 2026-11-04),
    /// <see langword="true"/> otherwise. A new trigger is not capturing; the service sets it right after creating it and on
    /// every snapshot (also with an unchanged value). On a change to <see langword="false"/> the trigger requests nothing more
    /// and withdraws a request still waiting for its key frame (<see cref="ICameraTimelapse.CancelFrameRequest"/>), so no
    /// frame shows the paused print.
    /// </summary>
    void SetCapturing(bool capturing);

    /// <summary>Every snapshot of the printer while the timelapse runs, before <see cref="SetCapturing"/> of the same snapshot.</summary>
    void OnSnapshot(PrinterSnapshot snapshot);
}
