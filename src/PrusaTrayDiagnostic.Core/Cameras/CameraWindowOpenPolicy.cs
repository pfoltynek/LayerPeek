namespace PrusaTrayDiagnostic.Core.Cameras;

/// <summary>How a camera window opens (T-035), decided by <see cref="CameraWindowOpenPolicy.Decide"/>.</summary>
/// <param name="Mode">Mode to open in.</param>
/// <param name="UseRememberedNormal">
/// Use the remembered placement of the normal window (the caller still checks that it is reachable on the connected
/// monitors); otherwise place it from <see cref="CameraWindowDefaults.Normal"/>.
/// </param>
/// <param name="UseRememberedMini">Same for the mini preview and <see cref="CameraWindowDefaults.Mini"/>.</param>
public sealed record CameraWindowOpenPlan(CameraWindowMode Mode, bool UseRememberedNormal, bool UseRememberedMini);

/// <summary>Remembered state (<c>camera-windows.json</c>) versus the defaults when a camera window opens (T-035).</summary>
public static class CameraWindowOpenPolicy
{
    /// <summary>
    /// Remembered placements are used only with <see cref="CameraWindowDefaults.RememberLastPlacement"/> and only where
    /// there is one (normal window and mini preview separately). The mode is the fixed
    /// <see cref="CameraWindowDefaults.StartMode"/>, or for <see cref="CameraWindowStartMode.Last"/> the remembered mode
    /// (<see cref="CameraWindowMode.Normal"/> when none is remembered). The mode does not depend on
    /// <see cref="CameraWindowDefaults.RememberLastPlacement"/>, which is about position and size.
    /// </summary>
    /// <param name="defaults">Window defaults of the printer.</param>
    /// <param name="rememberedMode">Remembered mode; <see langword="null"/> when nothing is remembered for the printer.</param>
    /// <param name="hasRememberedNormal">A placement of the normal window is remembered.</param>
    /// <param name="hasRememberedMini">A placement of the mini preview is remembered.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rememberedMode"/> is not a defined value.</exception>
    public static CameraWindowOpenPlan Decide(CameraWindowDefaults defaults, CameraWindowMode? rememberedMode, bool hasRememberedNormal, bool hasRememberedMini)
    {
        ArgumentNullException.ThrowIfNull(defaults);
        if (rememberedMode is { } remembered && !Enum.IsDefined(remembered))
        {
            throw new ArgumentOutOfRangeException(nameof(rememberedMode), remembered, "Unknown camera window mode.");
        }

        CameraWindowMode mode = defaults.StartMode switch
        {
            CameraWindowStartMode.Last => rememberedMode ?? CameraWindowMode.Normal,
            CameraWindowStartMode.Normal => CameraWindowMode.Normal,
            CameraWindowStartMode.Mini => CameraWindowMode.Mini,
            CameraWindowStartMode.FullScreen => CameraWindowMode.FullScreen,
            _ => throw new ArgumentOutOfRangeException(nameof(defaults), defaults.StartMode, "Unknown camera window start mode."),
        };

        bool remember = defaults.RememberLastPlacement;
        return new CameraWindowOpenPlan(mode, remember && hasRememberedNormal, remember && hasRememberedMini);
    }
}
