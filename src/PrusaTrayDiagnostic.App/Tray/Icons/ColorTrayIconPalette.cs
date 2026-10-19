using Avalonia.Media;

namespace PrusaTrayDiagnostic.App.Tray.Icons;

/// <summary>
/// Coloured, trademark-free icon look (Windows notification area), design B "Layers" approved 2026-10-19 (T-025): an
/// orange hotend above three layers. Printing fills the layers in blue, paused in yellow; finished turns them green,
/// error red. Every other state keeps grey layers and shows a badge with a symbol. State colours are those of T-007.
/// </summary>
internal static class ColorTrayIconPalette
{
    /// <summary>Prusa orange (prusa3d.com brand primary, 2026); a colour, not the logo.</summary>
    public static readonly Color Orange = Color.FromRgb(0xFD, 0x50, 0x00);

    /// <summary>Dark symbol colour (pause bars, exclamation mark, minus).</summary>
    public static readonly Color Dark = Color.FromRgb(0x1E, 0x1F, 0x24);

    /// <summary>Hotend of the offline icon and the base of the layer track.</summary>
    public static readonly Color Grey = Color.FromRgb(0x8A, 0x8D, 0x96);

    /// <summary>Unfilled layers: <see cref="Grey"/> at 60 % opacity.</summary>
    public static readonly Color Track = Color.FromArgb(153, 0x8A, 0x8D, 0x96);

    /// <summary>Unfilled layers when there is no data: <see cref="Grey"/> at 35 % opacity.</summary>
    public static readonly Color OfflineTrack = Color.FromArgb(89, 0x8A, 0x8D, 0x96);

    private static readonly Color White = Color.FromRgb(0xFF, 0xFF, 0xFF);
    private static readonly Color BadgeGrey = Color.FromRgb(0x9E, 0x9E, 0x9E);
    private static readonly Color BadgeGreyOutline = Color.FromRgb(0x61, 0x61, 0x61);
    private static readonly Color Blue = Color.FromRgb(0x1E, 0x88, 0xE5);
    private static readonly Color BlueOutline = Color.FromRgb(0x0D, 0x47, 0xA1);
    private static readonly Color Yellow = Color.FromRgb(0xFD, 0xD8, 0x35);
    private static readonly Color YellowOutline = Color.FromRgb(0xF5, 0x7F, 0x17);
    private static readonly Color Green = Color.FromRgb(0x43, 0xA0, 0x47);
    private static readonly Color GreenOutline = Color.FromRgb(0x1B, 0x5E, 0x20);
    private static readonly Color Red = Color.FromRgb(0xE5, 0x39, 0x35);
    private static readonly Color RedOutline = Color.FromRgb(0xB7, 0x1C, 0x1C);
    private static readonly Color Amber = Color.FromRgb(0xFB, 0x8C, 0x00);
    private static readonly Color AmberOutline = Color.FromRgb(0xBF, 0x36, 0x0C);

    public static TrayIconAppearance GetAppearance(TrayIconKey key) => key.Status switch
    {
        TrayStatus.Printing => Layers(Blue, key.Progress / 100.0, TrayBadge.None, default),
        TrayStatus.Paused => Layers(Yellow, key.Progress / 100.0, TrayBadge.Paused, new(Yellow, YellowOutline, Dark)),
        TrayStatus.Finished => Layers(Green, 1, TrayBadge.Finished, new(Green, GreenOutline, White)),
        TrayStatus.Error => Layers(Red, 1, TrayBadge.Error, new(Red, RedOutline, White)),
        TrayStatus.Busy => Layers(Track, 0, TrayBadge.Busy, new(Blue, BlueOutline, White)),
        TrayStatus.Attention => Layers(Track, 0, TrayBadge.Attention, new(Amber, AmberOutline, Dark)),
        TrayStatus.Idle => Layers(Track, 0, TrayBadge.None, default),
        _ => new(Grey, OfflineTrack, OfflineTrack, 0, TrayBadge.Offline, new(BadgeGrey, BadgeGreyOutline, Dark)),
    };

    private static TrayIconAppearance Layers(Color fill, double filled, TrayBadge badge, TrayBadgeColors badgeColors) =>
        new(Orange, Track, fill, Math.Clamp(filled, 0, 1), badge, badgeColors);
}
