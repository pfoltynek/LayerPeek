using Avalonia.Media;

namespace PrusaTrayDiagnostic.App.Tray.Icons;

/// <summary>
/// Coloured, trademark-free icon look (Windows notification area): a coloured dot per status, a progress ring
/// while printing (blue) or paused (yellow) and a crossed-out ring when there is no data.
/// </summary>
internal static class ColorTrayIconPalette
{
    private static readonly Color Grey = Color.FromRgb(0x9E, 0x9E, 0x9E);
    private static readonly Color GreyOutline = Color.FromRgb(0x61, 0x61, 0x61);
    private static readonly Color Blue = Color.FromRgb(0x1E, 0x88, 0xE5);
    private static readonly Color BlueOutline = Color.FromRgb(0x0D, 0x47, 0xA1);
    private static readonly Color Yellow = Color.FromRgb(0xFD, 0xD8, 0x35);
    private static readonly Color YellowOutline = Color.FromRgb(0xF5, 0x7F, 0x17);
    private static readonly Color Green = Color.FromRgb(0x43, 0xA0, 0x47);
    private static readonly Color GreenOutline = Color.FromRgb(0x1B, 0x5E, 0x20);
    private static readonly Color Red = Color.FromRgb(0xE5, 0x39, 0x35);
    private static readonly Color RedOutline = Color.FromRgb(0xB7, 0x1C, 0x1C);
    private static readonly Color Orange = Color.FromRgb(0xFB, 0x8C, 0x00);
    private static readonly Color OrangeOutline = Color.FromRgb(0xBF, 0x36, 0x0C);

    public static TrayIconAppearance GetAppearance(TrayIconKey key) => key.Status switch
    {
        TrayStatus.Printing => new(TrayIconShape.ProgressRing, Blue, BlueOutline, key.Progress * 3.6),
        TrayStatus.Busy => Dot(Blue, BlueOutline),
        TrayStatus.Paused => new(TrayIconShape.ProgressRing, Yellow, YellowOutline, key.Progress * 3.6),
        TrayStatus.Finished => Dot(Green, GreenOutline),
        TrayStatus.Error => Dot(Red, RedOutline),
        TrayStatus.Attention => Dot(Orange, OrangeOutline),
        TrayStatus.Idle => Dot(Grey, GreyOutline),
        _ => new(TrayIconShape.CrossedRing, Grey, GreyOutline, 0),
    };

    private static TrayIconAppearance Dot(Color fill, Color outline) => new(TrayIconShape.Dot, fill, outline, 0);
}
