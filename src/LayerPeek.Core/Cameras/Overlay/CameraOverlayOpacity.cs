namespace LayerPeek.Core.Cameras.Overlay;

/// <summary>
/// Opacity of the background of the overlay widget cards in whole percent (T-077, user decision 2026-10-24): one global
/// value for every printer (<see cref="Settings.AppSettings.CameraOverlayOpacityPercent"/>), within
/// <see cref="MinPercent"/>–<see cref="MaxPercent"/>, <see cref="DefaultPercent"/> by default.
/// </summary>
public static class CameraOverlayOpacity
{
    public const int MinPercent = 30;

    public const int MaxPercent = 95;

    public const int DefaultPercent = 72;

    public static bool IsValid(int percent) => percent is >= MinPercent and <= MaxPercent;

    /// <exception cref="ArgumentOutOfRangeException"><paramref name="percent"/> is not <see cref="IsValid">valid</see>.</exception>
    public static void Validate(int percent, string? paramName = null)
    {
        if (!IsValid(percent))
        {
            throw new ArgumentOutOfRangeException(paramName ?? nameof(percent), percent, $"The overlay card opacity must be within {MinPercent}–{MaxPercent} %.");
        }
    }
}
