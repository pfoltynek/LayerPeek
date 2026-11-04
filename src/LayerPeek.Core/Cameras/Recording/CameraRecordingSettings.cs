namespace LayerPeek.Core.Cameras.Recording;

/// <summary>
/// Global settings of the camera recordings (T-145, user decision 2026-11-03: folder and limit for all printers; whether a
/// printer records its prints automatically is <see cref="CameraSettings.RecordPrints"/>) and of the dashcam (T-148, user
/// decision 2026-11-04: its length and triggers for all printers; whether a printer has one is
/// <see cref="CameraSettings.Dashcam"/>). Persisted as the <c>recording</c> section of <c>settings.json</c>, written only
/// while something differs from the defaults.
/// </summary>
public sealed record CameraRecordingSettings
{
    /// <summary>Default of <see cref="SizeLimitGigabytes"/> (user decision 2026-11-03).</summary>
    public const int DefaultSizeLimitGigabytes = 50;

    public const int MinSizeLimitGigabytes = 1;

    /// <summary>Upper bound of <see cref="SizeLimitGigabytes"/> (100 TB): beyond any disk, so that the bytes never overflow.</summary>
    public const int MaxSizeLimitGigabytes = 100_000;

    /// <summary>Longest accepted <see cref="Folder"/>; recordings go into subfolders and get long names of their own.</summary>
    public const int MaxFolderLength = 200;

    /// <summary>Default of <see cref="DashcamMinutes"/> (user decision 2026-11-04: ~16 MB of memory per printer).</summary>
    public const int DefaultDashcamMinutes = 2;

    public const int MinDashcamMinutes = 1;

    public const int MaxDashcamMinutes = 10;

    public static CameraRecordingSettings Default { get; } = new();

    /// <summary>
    /// The folder chosen by the user (absolute, trimmed); <see langword="null"/> (the default): the system's video folder
    /// (<see cref="CameraRecordingFolders.DefaultRoot"/>). Every printer gets a subfolder of its own in it.
    /// </summary>
    /// <exception cref="ArgumentException">Not a valid absolute folder path (see <see cref="IsValidFolder"/>).</exception>
    public string? Folder
    {
        get;
        init
        {
            if (value is not null && !IsValidFolder(value))
            {
                // The path is not echoed: it may contain the user name.
                throw new ArgumentException("The recording folder must be an absolute path.", nameof(value));
            }

            field = value?.Trim();
        }
    }

    /// <summary>
    /// Total size of all recordings in whole gigabytes (10^9 bytes): above it the oldest recordings are deleted (rotation).
    /// Within <see cref="MinSizeLimitGigabytes"/>–<see cref="MaxSizeLimitGigabytes"/>.
    /// </summary>
    public int SizeLimitGigabytes
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, MinSizeLimitGigabytes);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxSizeLimitGigabytes);
            field = value;
        }
    } = DefaultSizeLimitGigabytes;

    /// <summary><see cref="SizeLimitGigabytes"/> in bytes.</summary>
    public long SizeLimitBytes => SizeLimitGigabytes * 1_000_000_000L;

    /// <summary>
    /// How many minutes of the stream the dashcam holds in memory during a print (T-148), within
    /// <see cref="MinDashcamMinutes"/>–<see cref="MaxDashcamMinutes"/>.
    /// </summary>
    public int DashcamMinutes
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, MinDashcamMinutes);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxDashcamMinutes);
            field = value;
        }
    } = DefaultDashcamMinutes;

    /// <summary>The dashcam saves when the printer reports an error during a print (default on).</summary>
    public bool DashcamOnError { get; init; } = true;

    /// <summary>The dashcam saves when the printer waits for the user during a print (default on).</summary>
    public bool DashcamOnAttention { get; init; } = true;

    /// <summary>The dashcam saves when the print is stopped (default on).</summary>
    public bool DashcamOnStopped { get; init; } = true;

    /// <summary>The dashcam saves when the print failure detection reports a failure (T-155; default on).</summary>
    public bool DashcamOnFailureDetection { get; init; } = true;

    /// <summary>Whether <paramref name="trigger"/> saves the dashcam; <see cref="CameraDashcamTrigger.Manual"/> always does.</summary>
    public bool SavesDashcamOn(CameraDashcamTrigger trigger) => trigger switch
    {
        CameraDashcamTrigger.Error => DashcamOnError,
        CameraDashcamTrigger.Attention => DashcamOnAttention,
        CameraDashcamTrigger.Stopped => DashcamOnStopped,
        CameraDashcamTrigger.FailureDetected => DashcamOnFailureDetection,
        _ => true,
    };

    /// <summary>
    /// Whether <paramref name="folder"/> can be <see cref="Folder"/>: a fully qualified path (after trimming) without
    /// characters that are invalid in paths, at most <see cref="MaxFolderLength"/> characters. Whether it exists or can be
    /// written to is checked when a recording starts.
    /// </summary>
    public static bool IsValidFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return false;
        }

        string trimmed = folder.Trim();
        return trimmed.Length <= MaxFolderLength
            && trimmed.AsSpan().IndexOfAny(Path.GetInvalidPathChars()) < 0
            && Path.IsPathFullyQualified(trimmed);
    }
}
