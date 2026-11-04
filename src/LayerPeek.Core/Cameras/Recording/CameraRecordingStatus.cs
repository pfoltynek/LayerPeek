namespace LayerPeek.Core.Cameras.Recording;

/// <summary>What the recording of one printer is doing (<see cref="CameraRecordingStatus.Activity"/>).</summary>
public enum CameraRecordingActivity
{
    /// <summary>Not recording.</summary>
    Idle,

    /// <summary>Started; the folder is being prepared or the camera's first key frame is awaited (no file yet).</summary>
    Starting,

    /// <summary>A file is being written.</summary>
    Recording,

    /// <summary>Stopping: the current file is being completed.</summary>
    Stopping,
}

/// <summary>Why a recording runs.</summary>
public enum CameraRecordingMode
{
    /// <summary>Started by the user; runs until the user stops it (or it fails).</summary>
    Manual,

    /// <summary>
    /// Started by a print of a printer with <see cref="CameraSettings.RecordPrints"/>; ends a minute after the print
    /// (user decision 2026-11-03).
    /// </summary>
    Automatic,

    /// <summary>The dashcam of a printer with <see cref="CameraSettings.Dashcam"/> (T-148): the last minutes, saved on an event.</summary>
    Dashcam,
}

/// <summary>What the dashcam of one printer is doing (<see cref="CameraRecordingStatus.Dashcam"/>, T-148).</summary>
public enum CameraDashcamActivity
{
    /// <summary>The printer has no dashcam (<see cref="CameraSettings.Dashcam"/> off, camera disabled, or this build cannot record).</summary>
    Off,

    /// <summary>
    /// The printer has a dashcam, but nothing is held now: it does not print, the whole print is recorded
    /// (<see cref="CameraRecordingStatus.IsActive"/>), or the dashcam failed during this print.
    /// </summary>
    Waiting,

    /// <summary>The last minutes are held in memory; "Save the last minutes" works.</summary>
    Buffering,

    /// <summary>The last minutes are being saved (and the stream after the event); another event extends the save.</summary>
    Saving,
}

/// <summary>Why a recording could not start or ended with an error (<see cref="CameraRecordingError.Problem"/>).</summary>
public enum CameraRecordingProblem
{
    /// <summary>This build cannot record (<see cref="ICameraRecorder.Availability"/>, e.g. no MP4 muxer).</summary>
    Unavailable = 1,

    /// <summary>The printer has no enabled camera (any more).</summary>
    CameraUnavailable,

    /// <summary>The recording folder cannot be created or written to (missing drive, no permission).</summary>
    FolderUnavailable,

    /// <summary>Less free space than the safety margin (5 GB, user decision 2026-11-03): not started, or stopped.</summary>
    DiskSpaceLow,

    /// <summary>The disk ran full while writing.</summary>
    DiskFull,

    /// <summary>A file could not be created or written (I/O error, disk removed).</summary>
    WriteFailed,

    /// <summary>Writing did not keep up with the camera.</summary>
    TooSlow,

    /// <summary>The camera stream failed unexpectedly or cannot be stored.</summary>
    StreamFailed,

    /// <summary>
    /// The running recording alone exceeds the size limit: every older recording was deleted already, so it was stopped.
    /// </summary>
    SizeLimitReached,
}

/// <summary>
/// A recording that could not start or ended with an error (T-145): the only recording event that is notified (user
/// decision 2026-11-03). The texts of the notification are localized by the app from <see cref="Problem"/>.
/// </summary>
/// <param name="PrinterId">The printer (id as configured).</param>
/// <param name="Problem">What went wrong.</param>
/// <param name="Message">Technical English description for the log, never with a path.</param>
/// <param name="Timestamp">When it happened.</param>
/// <param name="Mode">Whether the recording was started by the user or by a print.</param>
public sealed record CameraRecordingError(string PrinterId, CameraRecordingProblem Problem, string Message, DateTimeOffset Timestamp, CameraRecordingMode Mode);

/// <summary>The recording state of one printer at one moment (<see cref="CameraRecordingService.GetStatus"/>).</summary>
public sealed record CameraRecordingStatus
{
    /// <summary>A printer that is not configured.</summary>
    public static CameraRecordingStatus None { get; } = new();

    public CameraRecordingActivity Activity { get; init; }

    /// <summary>Starting, recording or stopping: the REC indicator shows (T-146) and Start does nothing.</summary>
    public bool IsActive => Activity != CameraRecordingActivity.Idle;

    /// <summary>Why it runs; <see langword="null"/> while <see cref="CameraRecordingActivity.Idle"/>.</summary>
    public CameraRecordingMode? Mode { get; init; }

    /// <summary>When the running recording was started; <see langword="null"/> while idle.</summary>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>The file being written (full path); <see langword="null"/> before the first key frame and while idle.</summary>
    public string? CurrentFile { get; init; }

    /// <summary>Bytes written by the running recording so far (all its files).</summary>
    public long BytesWritten { get; init; }

    /// <summary>Recorded video time of the running recording so far.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// The newest completed recording of the printer (full path), also from earlier runs of the app; <see langword="null"/>
    /// when none is known (or it was deleted by the rotation).
    /// </summary>
    public string? LastFile { get; init; }

    /// <summary>The last error of this run; cleared by the next recording that starts successfully.</summary>
    public CameraRecordingError? LastError { get; init; }

    /// <summary>The printer records its prints automatically (<see cref="CameraSettings.RecordPrints"/> with an enabled camera).</summary>
    public bool RecordsPrints { get; init; }

    /// <summary>The printer has an enabled camera, so that it can be recorded at all.</summary>
    public bool CanRecord { get; init; }

    /// <summary>What the dashcam does (T-148).</summary>
    public CameraDashcamActivity Dashcam { get; init; }

    /// <summary>Minutes the dashcam holds (<see cref="CameraRecordingSettings.DashcamMinutes"/>), e.g. for "Save the last 2 min".</summary>
    public int DashcamMinutes { get; init; } = CameraRecordingSettings.DefaultDashcamMinutes;

    /// <summary>Time the dashcam holds in memory now; zero while it does not buffer.</summary>
    public TimeSpan DashcamBuffered { get; init; }
}
