namespace LayerPeek.Core.Cameras.Timelapse;

/// <summary>
/// Why a JPEG export of a timelapse (T-165) ended without its folder, or a timelapse from a recording (T-164,
/// <see cref="CameraTimelapseFromRecordingMaker"/>) without its file.
/// </summary>
public enum CameraTimelapseExportProblem
{
    /// <summary>It succeeded.</summary>
    None,

    /// <summary>The user cancelled it; nothing was left behind.</summary>
    Cancelled,

    /// <summary>The chosen file is not a timelapse of LayerPeek (<see cref="Recording.CameraRecordingFolders.IsTimelapseFileName"/>).</summary>
    NotATimelapse,

    /// <summary>
    /// T-164: the chosen file is not a recording of LayerPeek (<see cref="Recording.CameraRecordingFolders.IsRecordingFileName"/>),
    /// or it is a timelapse already.
    /// </summary>
    NotARecording,

    /// <summary>The file is missing, damaged or not a video the decoder reads (H.264 in MP4).</summary>
    Unreadable,

    /// <summary>The video decoder (T-164: the demuxer or the MP4 muxer) is not available in this build (FFmpeg missing).</summary>
    Unavailable,

    /// <summary>The frames (T-164: the timelapse) could not be written next to the source (no permission, read-only drive…).</summary>
    WriteFailed,

    /// <summary>The drive of the source file is full.</summary>
    DiskFull,

    /// <summary>Anything else (the JPEG encoder failed…); details are in the log.</summary>
    Failed,
}

/// <summary>How a JPEG export of a timelapse (T-165) ended.</summary>
/// <param name="PrinterId">The printer whose menu started it; <see langword="null"/> when none was named.</param>
/// <param name="SourceFile">The timelapse (full path).</param>
/// <param name="OutputFolder">The folder with the frames (full path); <see langword="null"/> unless it succeeded.</param>
/// <param name="Frames">Frames written (all of them when it succeeded; a damaged end of the file gives fewer).</param>
/// <param name="Problem">Why it did not succeed; <see cref="CameraTimelapseExportProblem.None"/> when it did.</param>
public sealed record CameraTimelapseExportResult(
    string? PrinterId,
    string SourceFile,
    string? OutputFolder,
    int Frames,
    CameraTimelapseExportProblem Problem)
{
    public bool Succeeded => Problem == CameraTimelapseExportProblem.None;
}

/// <summary>What the JPEG export of a timelapse (T-165) does now.</summary>
/// <param name="IsRunning">An export runs (also while it counts the frames).</param>
/// <param name="PrinterId">The printer whose menu started it, if any.</param>
/// <param name="SourceFile">The timelapse it reads (full path).</param>
/// <param name="FramesDone">Frames written so far.</param>
/// <param name="FramesTotal">Frames of the timelapse; 0 while they are counted.</param>
public sealed record CameraTimelapseExportStatus(bool IsRunning, string? PrinterId, string? SourceFile, int FramesDone, int FramesTotal)
{
    public static CameraTimelapseExportStatus Idle { get; } = new(false, null, null, 0, 0);

    /// <summary>Done in whole percent, 0–100 (0 while the frames are counted).</summary>
    public int Percent => FramesTotal <= 0 ? 0 : (int)Math.Clamp(FramesDone * 100L / FramesTotal, 0, 100);
}
