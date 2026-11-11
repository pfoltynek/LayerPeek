namespace LayerPeek.Core.Cameras;

/// <summary>The recommended frame rate profiles of the mini preview (T-185, user decision 2026-11-11).</summary>
public enum MiniPreviewFrameRateProfile
{
    /// <summary>"Economy": <see cref="MiniPreviewFrameRate.Economy"/> frames per second.</summary>
    Economy,

    /// <summary>"Balanced" (the default): <see cref="MiniPreviewFrameRate.Balanced"/> frames per second.</summary>
    Balanced,

    /// <summary>"Smooth": no limit, the camera's own frame rate.</summary>
    Smooth,

    /// <summary>Any other limit chosen with the slider.</summary>
    Custom,
}

/// <summary>
/// The frame rate limit of the camera window's mini preview (T-185, user decisions 2026-11-10 and 2026-11-11): one global
/// value for every camera (<see cref="Settings.AppSettings.MiniPreviewFrameRate"/>), whole frames per second within
/// <see cref="Min"/>–<see cref="Max"/>, or <see langword="null"/> for no limit. The normal window and full screen always get
/// the camera's full frame rate; detection, recording and timelapse are never limited.
/// </summary>
public static class MiniPreviewFrameRate
{
    public const int Min = 1;

    public const int Max = 30;

    /// <summary><see cref="MiniPreviewFrameRateProfile.Economy"/>.</summary>
    public const int Economy = 5;

    /// <summary><see cref="MiniPreviewFrameRateProfile.Balanced"/>, the default.</summary>
    public const int Balanced = 10;

    public const int Default = Balanced;

    /// <summary>Whether <paramref name="rate"/> is a limit within <see cref="Min"/>–<see cref="Max"/> or no limit.</summary>
    public static bool IsValid(int? rate) => rate is null or (>= Min and <= Max);

    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rate"/> is not <see cref="IsValid">valid</see>.</exception>
    public static void Validate(int? rate, string? paramName = null)
    {
        if (!IsValid(rate))
        {
            throw new ArgumentOutOfRangeException(paramName ?? nameof(rate), rate, $"The mini preview frame rate must be within {Min}–{Max} fps, or no limit.");
        }
    }

    /// <summary>The profile <paramref name="rate"/> belongs to; any other valid limit is <see cref="MiniPreviewFrameRateProfile.Custom"/>.</summary>
    public static MiniPreviewFrameRateProfile ProfileOf(int? rate) => rate switch
    {
        null => MiniPreviewFrameRateProfile.Smooth,
        Economy => MiniPreviewFrameRateProfile.Economy,
        Balanced => MiniPreviewFrameRateProfile.Balanced,
        _ => MiniPreviewFrameRateProfile.Custom,
    };

    /// <summary>
    /// The limit of <paramref name="profile"/>; <paramref name="custom"/> for <see cref="MiniPreviewFrameRateProfile.Custom"/>
    /// (which has no value of its own).
    /// </summary>
    public static int? RateOf(MiniPreviewFrameRateProfile profile, int? custom) => profile switch
    {
        MiniPreviewFrameRateProfile.Economy => Economy,
        MiniPreviewFrameRateProfile.Balanced => Balanced,
        MiniPreviewFrameRateProfile.Smooth => null,
        MiniPreviewFrameRateProfile.Custom => custom,
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unknown frame rate profile."),
    };
}
