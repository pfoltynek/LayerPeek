using System.Globalization;

using LayerPeek.Core.Cameras;
using LayerPeek.Core.History;
using LayerPeek.Core.Printers;

namespace LayerPeek.App.Cameras.Overlay;

/// <summary>
/// What an overlay widget computes its content from (T-081). A value, built once per snapshot for all widgets of a
/// window (no allocation); a widget reads only what it shows.
/// </summary>
/// <param name="Snapshot">The latest snapshot of the printer; <see langword="null"/> before the first poll.</param>
/// <param name="Printer">The printer as configured (name, camera, cool-down threshold…); follows saved settings.</param>
/// <param name="Mode">The current mode of the camera window (cards are drawn in the normal window and on full screen).</param>
/// <param name="Now">The current time of the window's <see cref="TimeProvider"/>.</param>
/// <param name="TimeZone">The local time zone of the window's <see cref="TimeProvider"/> (finish times).</param>
/// <param name="Culture">The region (<c>CurrentCulture</c>) of numbers and times; words follow the app language.</param>
public readonly record struct OverlayWidgetInput(
    PrinterSnapshot? Snapshot,
    PrinterDefinition Printer,
    CameraWindowMode Mode,
    DateTimeOffset Now,
    TimeZoneInfo TimeZone,
    CultureInfo Culture);

/// <summary>
/// What a widget gets when it is created for one camera window (T-081): the printer and the services it may need besides
/// the snapshots. Later widgets add what they need here.
/// </summary>
/// <param name="PrinterId">The printer of the window.</param>
/// <param name="Time">The clock of the window.</param>
/// <param name="History">The telemetry history (T-084: the temperature chart); <see langword="null"/> = none, charts show "no data".</param>
public sealed record OverlayWidgetContext(string PrinterId, TimeProvider Time, TelemetryHistory? History = null);
