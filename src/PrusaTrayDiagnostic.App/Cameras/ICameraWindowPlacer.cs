using PrusaTrayDiagnostic.Core.Cameras;

namespace PrusaTrayDiagnostic.App.Cameras;

/// <summary>
/// Places an open camera window again by saved settings (T-040, the "Apply" button of the settings window). UI thread only.
/// </summary>
internal interface ICameraWindowPlacer
{
    /// <summary>
    /// The open camera window of <paramref name="printerId"/> takes <paramref name="window"/> right away: mode (by the start
    /// mode; "same as last time" keeps the current one), monitor, size, position and "always on top", ignoring where it was.
    /// </summary>
    /// <returns>Whether a window of the printer was open (nothing happens otherwise).</returns>
    bool ApplySettings(string printerId, CameraWindowDefaults window);
}
