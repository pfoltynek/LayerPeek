using LayerPeek.Core.Cameras.Recording;

namespace LayerPeek.Core.Cameras.Timelapse;

/// <summary>
/// Starts timelapses of a printer's camera (T-158, variant D of the spike T-149): key frames of the shared camera connection
/// (<see cref="CameraStreamHub"/>), taken on request, are written one after the other at a constant
/// <see cref="FramesPerSecond"/> into a fragmented MP4 file, without decoding or encoding anything (the same writer as
/// recordings, T-144). When to take a frame, and when and where to start, is decided by the caller (T-160).
/// </summary>
public interface ICameraTimelapseRecorder
{
    /// <summary>Frame rate of every timelapse video (user decision 2026-11-04): one taken frame lasts 1/30 s.</summary>
    const int FramesPerSecond = 30;

    /// <summary>Whether this build can write timelapses (the same MP4 muxer as recordings).</summary>
    CameraRecorderAvailability Availability { get; }

    /// <summary>
    /// Starts a timelapse of <paramref name="printerId"/>'s camera into <paramref name="filePath"/> (absolute; the folder
    /// must exist). No frame is taken until <see cref="ICameraTimelapse.RequestFrame"/>. Never throws for runtime
    /// conditions: a timelapse that cannot run (no muxer, no camera) is returned already
    /// <see cref="CameraRecordingState.Failed"/> or fails by itself. When a new camera connection has other codec parameters
    /// (SPS/PPS) or image size, the next file gets the suffix <c>-2</c>, <c>-3</c> … before the extension; an existing file
    /// is never overwritten.
    /// </summary>
    /// <param name="changed">
    /// Called on the timelapse's thread when <see cref="ICameraTimelapse.State"/> or <see cref="ICameraTimelapse.Files"/>
    /// changed (not for every frame); must return quickly and never block. Exceptions are logged and ignored.
    /// </param>
    /// <exception cref="ArgumentException">The arguments are invalid (empty id, relative path, no file name).</exception>
    ICameraTimelapse Start(string printerId, string filePath, Action<ICameraTimelapse>? changed = null);
}

/// <summary>A running or ended timelapse of one printer's camera; thread-safe.</summary>
public interface ICameraTimelapse : IAsyncDisposable
{
    string PrinterId { get; }

    /// <summary>
    /// <see cref="CameraRecordingState.Starting"/> until the first frame is written, then
    /// <see cref="CameraRecordingState.Recording"/>, finally <see cref="CameraRecordingState.Completed"/> or
    /// <see cref="CameraRecordingState.Failed"/>.
    /// </summary>
    CameraRecordingState State { get; }

    CameraRecordingFailure Failure { get; }

    /// <summary>Why it failed (English, without paths); <see langword="null"/> unless <see cref="CameraRecordingState.Failed"/>.</summary>
    string? FailureMessage { get; }

    /// <summary>The files created so far, in order (full paths).</summary>
    IReadOnlyList<string> Files { get; }

    /// <summary>Bytes written to all files so far.</summary>
    long BytesWritten { get; }

    /// <summary>Frames written to all files so far.</summary>
    long FrameCount { get; }

    /// <summary>Video time of all files so far: <see cref="FrameCount"/> / <see cref="ICameraTimelapseRecorder.FramesPerSecond"/>.</summary>
    TimeSpan Duration { get; }

    /// <summary>Completes (never faults) when the timelapse ended and every file is closed.</summary>
    Task Completion { get; }

    /// <summary>
    /// Asks for one frame: the camera's next key frame is written (on the Buddy3D within ~2.4 s). Requests before that key
    /// frame make one frame together. Never blocks, never throws; any thread.
    /// </summary>
    /// <returns><see langword="false"/> when the timelapse has ended (nothing will be written).</returns>
    bool RequestFrame();

    /// <summary>
    /// Withdraws a pending <see cref="RequestFrame"/> (capturing paused): the next key frame is not written unless a frame is
    /// requested again. A key frame the reader had already taken for the request is still written (at most that one frame,
    /// already being written at the call); no other. Never blocks, never throws, allocates nothing; any thread.
    /// </summary>
    void CancelFrameRequest();

    /// <summary>Stops and completes the current file; idempotent, never throws. Same as disposing.</summary>
    Task StopAsync();
}
