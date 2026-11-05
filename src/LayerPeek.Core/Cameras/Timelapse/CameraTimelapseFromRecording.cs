namespace LayerPeek.Core.Cameras.Timelapse;

/// <summary>How making a timelapse from a finished recording (T-164) ended.</summary>
/// <param name="PrinterId">The printer whose menu started it; <see langword="null"/> when none was named.</param>
/// <param name="SourceFile">The recording that was chosen (full path).</param>
/// <param name="OutputFiles">
/// The timelapse files written (full paths, in order; more than one only when the camera's stream parameters changed between
/// the recording's segments). Empty when it failed, except when giving a later file its final name failed: then the files
/// already published (they stay on disk) and still a failure, as not every frame was kept.
/// </param>
/// <param name="Frames">Frames written (all of them when it succeeded).</param>
/// <param name="Problem">Why it did not succeed; <see cref="CameraTimelapseExportProblem.None"/> when it did.</param>
public sealed record CameraTimelapseFromRecordingResult(
    string? PrinterId,
    string SourceFile,
    IReadOnlyList<string> OutputFiles,
    int Frames,
    CameraTimelapseExportProblem Problem)
{
    public bool Succeeded => Problem == CameraTimelapseExportProblem.None;
}

/// <summary>What making a timelapse from a recording (T-164) does now.</summary>
/// <param name="IsRunning">A timelapse is being made.</param>
/// <param name="PrinterId">The printer whose menu started it, if any.</param>
/// <param name="SourceFile">The recording it reads (full path, as chosen).</param>
/// <param name="Frames">Frames written so far.</param>
/// <param name="Percent">Read so far, in whole percent of the recording's size (0–100).</param>
public sealed record CameraTimelapseFromRecordingStatus(bool IsRunning, string? PrinterId, string? SourceFile, int Frames, int Percent)
{
    public static CameraTimelapseFromRecordingStatus Idle { get; } = new(false, null, null, 0, 0);
}
