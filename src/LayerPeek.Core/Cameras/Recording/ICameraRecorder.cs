namespace LayerPeek.Core.Cameras.Recording;

/// <summary>Where a <see cref="ICameraRecording"/> is.</summary>
public enum CameraRecordingState
{
    /// <summary>Waiting for the camera stream and its first key frame; no file exists yet.</summary>
    Starting,

    /// <summary>A file is being written (after a stream restart: the next file starts with the next key frame).</summary>
    Recording,

    /// <summary>Stopped by its owner or because the application quits; every file is complete.</summary>
    Completed,

    /// <summary>Ended by <see cref="ICameraRecording.Failure"/>; the files written so far are complete or readable up to the failure.</summary>
    Failed,
}

/// <summary>Why a recording failed.</summary>
public enum CameraRecordingFailure
{
    None,

    /// <summary>This build cannot record (<see cref="ICameraRecorder.Availability"/>).</summary>
    Unavailable,

    /// <summary>The printer has no enabled camera (never had one, or it was switched off or removed while recording).</summary>
    CameraUnavailable,

    /// <summary>A file could not be created or written (folder missing, no permission, disk removed, I/O error).</summary>
    WriteFailed,

    /// <summary>The disk is full.</summary>
    DiskFull,

    /// <summary>Writing did not keep up with the camera (the packet queue overflowed); the live picture went on.</summary>
    TooSlow,

    /// <summary>The camera stream failed unexpectedly, or it cannot be stored (e.g. no codec parameters).</summary>
    StreamFailed,
}

/// <summary>
/// Starts camera recordings (T-144): the packets of a printer's shared camera connection (<see cref="CameraStreamHub"/>)
/// are written into fragmented MP4 files without re-encoding. When and where to record is decided by the caller (T-145).
/// </summary>
public interface ICameraRecorder
{
    CameraRecorderAvailability Availability { get; }

    /// <summary>
    /// Starts recording <paramref name="printerId"/>'s camera into <paramref name="filePath"/> (absolute; the folder must
    /// exist). Never throws for runtime conditions: a recording that cannot run (no muxer, no camera) is returned already
    /// <see cref="CameraRecordingState.Failed"/>. After a stream restart the next file gets the suffix <c>-2</c>, <c>-3</c>
    /// … before the extension; an existing file is never overwritten (the next free suffix is used).
    /// </summary>
    /// <param name="changed">
    /// Called on the recording's thread when <see cref="ICameraRecording.State"/> or <see cref="ICameraRecording.Files"/>
    /// changed (not for the growing size); must return quickly and never block. Exceptions are logged and ignored.
    /// </param>
    /// <exception cref="ArgumentException">The arguments are invalid (empty id, relative path, no file name).</exception>
    ICameraRecording Start(string printerId, string filePath, Action<ICameraRecording>? changed = null);

    /// <summary>
    /// Starts the dashcam of <paramref name="printerId"/>'s camera (T-148): the last minutes of its shared connection held in
    /// memory and saved on <see cref="ICameraDashcam.Save"/>. Never throws for runtime conditions: one that cannot run (no
    /// muxer, no camera) is returned already <see cref="CameraDashcamState.Ended"/> or ends by itself.
    /// </summary>
    /// <param name="changed">
    /// Called when <see cref="ICameraDashcam.State"/> changed (on the dashcam's thread, or on the thread of
    /// <see cref="ICameraDashcam.Save"/>); must return quickly and never block. Exceptions are logged and ignored.
    /// </param>
    /// <param name="saved">Called once per save when it ended (on the dashcam's thread); same rules.</param>
    /// <exception cref="ArgumentException"><paramref name="printerId"/> is empty.</exception>
    ICameraDashcam StartDashcam(string printerId, CameraDashcamOptions options, Action<ICameraDashcam>? changed = null, Action<CameraDashcamSave>? saved = null);
}

/// <summary>A running or ended recording of one printer's camera; thread-safe.</summary>
public interface ICameraRecording : IAsyncDisposable
{
    string PrinterId { get; }

    CameraRecordingState State { get; }

    CameraRecordingFailure Failure { get; }

    /// <summary>Why it failed (English, without paths); <see langword="null"/> unless <see cref="CameraRecordingState.Failed"/>.</summary>
    string? FailureMessage { get; }

    /// <summary>The files created so far, in order (full paths).</summary>
    IReadOnlyList<string> Files { get; }

    /// <summary>Bytes written to all files so far.</summary>
    long BytesWritten { get; }

    /// <summary>Recorded video time of all files so far (by the stream's timestamps).</summary>
    TimeSpan Duration { get; }

    /// <summary>Completes (never faults) when the recording ended and every file is closed.</summary>
    Task Completion { get; }

    /// <summary>Stops recording and completes the current file; idempotent, never throws. Same as disposing.</summary>
    Task StopAsync();
}
