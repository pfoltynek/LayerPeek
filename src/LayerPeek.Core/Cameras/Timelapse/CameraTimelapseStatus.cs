using LayerPeek.Core.Cameras.Recording;

namespace LayerPeek.Core.Cameras.Timelapse;

/// <summary>The timelapse state of one printer at one moment (<see cref="CameraRecordingStatus.Timelapse"/>, T-160).</summary>
public sealed record CameraTimelapseStatus
{
    /// <summary>No timelapse runs or ran (a printer that is not configured).</summary>
    public static CameraTimelapseStatus None { get; } = new();

    /// <summary>
    /// <see cref="CameraRecordingActivity.Starting"/> (the folder is prepared or the camera's first key frame is awaited),
    /// <see cref="CameraRecordingActivity.Recording"/> (a file is written), <see cref="CameraRecordingActivity.Stopping"/>
    /// or <see cref="CameraRecordingActivity.Idle"/>.
    /// </summary>
    public CameraRecordingActivity Activity { get; init; }

    /// <summary>Starting, running or stopping: the timelapse indicator shows (T-161) and StartTimelapse does nothing.</summary>
    public bool IsActive => Activity != CameraRecordingActivity.Idle;

    /// <summary>
    /// <see cref="CameraRecordingMode.Manual"/> or <see cref="CameraRecordingMode.Automatic"/>; <see langword="null"/>
    /// while <see cref="CameraRecordingActivity.Idle"/>.
    /// </summary>
    public CameraRecordingMode? Mode { get; init; }

    /// <summary>
    /// Frames are taken now: starting or running, and the printer neither paused nor waiting for the user
    /// (<see cref="Printers.PrinterState.Paused"/>, <see cref="Printers.PrinterState.Attention"/>: the file stays open).
    /// </summary>
    public bool IsCapturing { get; init; }

    /// <summary>When the running timelapse was started; <see langword="null"/> while idle.</summary>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>The file being written (full path); <see langword="null"/> before the first frame and while idle.</summary>
    public string? CurrentFile { get; init; }

    /// <summary>Frames taken by the running timelapse so far (all its files).</summary>
    public long FrameCount { get; init; }

    /// <summary>Bytes written by the running timelapse so far (all its files).</summary>
    public long BytesWritten { get; init; }

    /// <summary>Video time of the running timelapse so far (<see cref="FrameCount"/> / 30 fps).</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// The newest completed timelapse of the printer (full path), also from earlier runs of the app; <see langword="null"/>
    /// when none is known (or it was deleted by the rotation).
    /// </summary>
    public string? LastFile { get; init; }

    /// <summary>The last timelapse error of this run; cleared by the next timelapse that writes its first frame.</summary>
    public CameraRecordingError? LastError { get; init; }

    /// <summary>The printer takes a timelapse of every print (<see cref="CameraSettings.TimelapsePrints"/> with an enabled camera).</summary>
    public bool TimelapsePrints { get; init; }

    /// <summary>The time between two frames (<see cref="CameraRecordingSettings.TimelapseIntervalSeconds"/>).</summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(CameraRecordingSettings.DefaultTimelapseIntervalSeconds);
}
