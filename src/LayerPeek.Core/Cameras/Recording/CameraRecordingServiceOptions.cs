namespace LayerPeek.Core.Cameras.Recording;

/// <summary>Timing and limits of <see cref="CameraRecordingService"/>; the defaults are the user's decisions of 2026-11-03.</summary>
public sealed record CameraRecordingServiceOptions
{
    /// <summary>The defaults with the system's video folder as the default root.</summary>
    public static CameraRecordingServiceOptions Default { get; } = new();

    /// <summary>Root when the settings name no folder (<see cref="CameraRecordingSettings.Folder"/>).</summary>
    public string DefaultRoot
    {
        get;
        init
        {
            if (!CameraRecordingSettings.IsValidFolder(value))
            {
                throw new ArgumentException("The default recording folder must be an absolute path.", nameof(value));
            }

            field = value.Trim();
        }
    } = CameraRecordingFolders.DefaultRoot();

    /// <summary>An automatic recording goes on this long after the print ended (Finished, Stopped, Error, Idle…).</summary>
    public TimeSpan EndDelay { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How long a printing printer may stay unreachable (Offline, Unknown, rejected credentials) before its automatic
    /// recording ends, like the cool-down tolerance of T-079.
    /// </summary>
    public TimeSpan OfflineGrace { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>Below this many free bytes a recording does not start, and a running one stops with an error.</summary>
    public long MinFreeBytes { get; init; } = 5_000_000_000;

    /// <summary>How often free space and the size limit are checked while something records (never per packet).</summary>
    public TimeSpan DiskCheckInterval
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            field = value;
        }
    } = TimeSpan.FromSeconds(30);
}
