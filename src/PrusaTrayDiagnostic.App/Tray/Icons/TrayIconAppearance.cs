using Avalonia.Media;

namespace PrusaTrayDiagnostic.App.Tray.Icons;

/// <summary>State badge in the top-right corner; each has its own symbol, so the state never relies on colour alone.</summary>
internal enum TrayBadge
{
    /// <summary>No badge (idle; printing, where the filling layers are the cue).</summary>
    None,

    /// <summary>Three dots.</summary>
    Busy,

    /// <summary>Two bars (❚❚).</summary>
    Paused,

    /// <summary>Check mark (✓).</summary>
    Finished,

    /// <summary>Exclamation mark (!).</summary>
    Attention,

    /// <summary>Cross (×).</summary>
    Error,

    /// <summary>Minus (−).</summary>
    Offline,
}

/// <param name="Fill">Disc colour.</param>
/// <param name="Outline">Darker ring that keeps the badge visible on light and dark taskbars.</param>
/// <param name="Symbol">Colour of the symbol inside the disc.</param>
internal readonly record struct TrayBadgeColors(Color Fill, Color Outline, Color Symbol);

/// <summary>
/// Platform-independent description of one tray icon of design B "Layers" (T-025): a hotend above three layers (bars)
/// that fill left to right, bottom layer first, plus an optional state badge. Drawn by <see cref="TrayIconRenderer"/>.
/// </summary>
/// <param name="Hotend">Colour of the hotend glyph.</param>
/// <param name="Track">Colour of the unfilled part of the layers (semi-transparent, so it suits light and dark taskbars).</param>
/// <param name="Fill">Colour of the filled part of the layers.</param>
/// <param name="FilledFraction">Filled part of all three layers together (0–1).</param>
/// <param name="Badge">State badge; <see cref="TrayBadge.None"/> draws none and ignores <paramref name="BadgeColors"/>.</param>
/// <param name="BadgeColors">Colours of the badge.</param>
internal readonly record struct TrayIconAppearance(
    Color Hotend,
    Color Track,
    Color Fill,
    double FilledFraction,
    TrayBadge Badge,
    TrayBadgeColors BadgeColors);
