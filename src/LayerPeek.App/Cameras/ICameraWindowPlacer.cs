using LayerPeek.Core.Printers;

namespace LayerPeek.App.Cameras;

/// <summary>
/// Shows the camera window of a printer by its saved settings (T-040, the "Apply" button of the settings window). UI
/// thread only.
/// </summary>
internal interface ICameraWindowPlacer
{
    /// <summary>
    /// The camera window of <paramref name="printer"/> (as just saved) takes its window settings right away: mode (by the
    /// start mode; "same as last time" keeps the current or remembered one), monitor, size, position and "always on top",
    /// ignoring where it was. A closed window opens (user decision 2026-10-21) from <paramref name="printer"/> itself, since
    /// monitoring hands the saved settings to the app only after it restarted.
    /// </summary>
    /// <returns>Whether a window of the printer is open now (not when it has no enabled camera).</returns>
    bool ApplySettings(PrinterDefinition printer);
}
