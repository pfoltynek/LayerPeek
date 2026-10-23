using System.Globalization;
using System.Text.Json.Nodes;

namespace PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;

/// <summary>
/// Where the camera window should open (T-035): its visible frame 16 DIP from the edges of the chosen monitor's work
/// area in a corner, or centred; the DIP of the monitor's effective DPI. Tolerances of T-044.
/// </summary>
internal static class CameraPlacement
{
    public const int MarginDip = 16;

    /// <summary>Pixels a corner may be off (rounding of DIP to pixels, T-044).</summary>
    public const int CornerTolerance = 2;

    /// <summary>Pixels the centre may be off (odd sizes, T-044).</summary>
    public const int CenterTolerance = 4;

    /// <summary>The placements of <c>CameraWindowCorner</c> as <c>settings.json</c> writes them.</summary>
    public static IReadOnlyList<string> Corners { get; } = ["TopLeft", "TopRight", "BottomLeft", "BottomRight", "Center"];

    /// <summary>The 16 DIP margin in pixels of a monitor with <paramref name="dpi"/> (rounded away from zero like the app).</summary>
    public static int MarginPx(uint dpi) => (int)Math.Round(MarginDip * dpi / 96.0, MidpointRounding.AwayFromZero);

    /// <summary>
    /// What is wrong with the visible <paramref name="frame"/> for <paramref name="corner"/> of <paramref name="work"/>,
    /// or <see langword="null"/> when it is in place.
    /// </summary>
    public static string? Problem(Native.Rect frame, Native.Rect work, string corner, uint dpi)
    {
        int margin = MarginPx(dpi);
        (int dx, int dy, int tolerance) = corner switch
        {
            "TopLeft" => (frame.Left - (work.Left + margin), frame.Top - (work.Top + margin), CornerTolerance),
            "TopRight" => (work.Right - margin - frame.Right, frame.Top - (work.Top + margin), CornerTolerance),
            "BottomLeft" => (frame.Left - (work.Left + margin), work.Bottom - margin - frame.Bottom, CornerTolerance),
            "BottomRight" => (work.Right - margin - frame.Right, work.Bottom - margin - frame.Bottom, CornerTolerance),
            "Center" => ((frame.Left + frame.Right - work.Left - work.Right) / 2, (frame.Top + frame.Bottom - work.Top - work.Bottom) / 2, CenterTolerance),
            _ => throw new ArgumentOutOfRangeException(nameof(corner), corner, "Not a camera window placement."),
        };
        return Math.Abs(dx) <= tolerance && Math.Abs(dy) <= tolerance
            ? null
            : string.Create(CultureInfo.InvariantCulture, $"frame {frame} is off {corner} of the work area {work} (margin {margin} px at {dpi} dpi) by ({dx}, {dy}) px, tolerance {tolerance}");
    }

    /// <summary>
    /// A <c>camera.window</c> section: the chosen monitor (<see langword="null"/> = primary), the start mode, both
    /// placements, both always-on-top options and the normal size; remembering is off, so every open uses exactly these
    /// defaults.
    /// </summary>
    public static JsonObject Window(
        JsonObject? monitor,
        string startMode,
        string normalCorner = "Center",
        string miniCorner = "BottomRight",
        bool topmostNormal = false,
        bool topmostMini = true,
        int normalWidth = 960,
        int normalHeight = 540)
    {
        JsonObject window = new()
        {
            ["rememberLastPlacement"] = false,
            ["startMode"] = startMode,
            ["normal"] = new JsonObject { ["width"] = normalWidth, ["height"] = normalHeight, ["corner"] = normalCorner },
            ["mini"] = new JsonObject { ["width"] = 320, ["height"] = 180, ["corner"] = miniCorner },
            ["topmostNormal"] = topmostNormal,
            ["topmostMini"] = topmostMini,
        };
        if (monitor is not null)
        {
            window["monitor"] = monitor.DeepClone();
        }

        return window;
    }
}
