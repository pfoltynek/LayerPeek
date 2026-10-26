using Avalonia.Media;
using Avalonia.Media.Immutable;

using LayerPeek.App.Tray.Icons;
using LayerPeek.Core.Printers;

namespace LayerPeek.App.Cameras.Overlay;

/// <summary>
/// The colour of the state dot (T-076 1.3): the tray icon's state colours (<see cref="ColorTrayIconPalette"/>, T-007), not
/// copies of them. The dot only adds to the state name next to it, the information is never colour alone. Immutable
/// brushes, created once; any thread.
/// </summary>
public static class OverlayStateBrushes
{
    /// <summary>Printing, busy.</summary>
    public static readonly IImmutableSolidColorBrush Active = new ImmutableSolidColorBrush(ColorTrayIconPalette.Blue);

    /// <summary>Paused.</summary>
    public static readonly IImmutableSolidColorBrush Paused = new ImmutableSolidColorBrush(ColorTrayIconPalette.Yellow);

    /// <summary>Finished.</summary>
    public static readonly IImmutableSolidColorBrush Finished = new ImmutableSolidColorBrush(ColorTrayIconPalette.Green);

    /// <summary>Error.</summary>
    public static readonly IImmutableSolidColorBrush Error = new ImmutableSolidColorBrush(ColorTrayIconPalette.Red);

    /// <summary>Needs attention, invalid credentials.</summary>
    public static readonly IImmutableSolidColorBrush Attention = new ImmutableSolidColorBrush(ColorTrayIconPalette.Amber);

    /// <summary>Ready, stopped.</summary>
    public static readonly IImmutableSolidColorBrush Idle = new ImmutableSolidColorBrush(ColorTrayIconPalette.Grey);

    /// <summary>Offline, unknown state, connecting (no data).</summary>
    public static readonly IImmutableSolidColorBrush NoData = new ImmutableSolidColorBrush(ColorTrayIconPalette.BadgeGrey);

    /// <summary>The dot of <paramref name="state"/>; <see langword="null"/> = before the first poll (connecting).</summary>
    public static IImmutableSolidColorBrush For(PrinterState? state) => state switch
    {
        PrinterState.Printing or PrinterState.Busy => Active,
        PrinterState.Paused => Paused,
        PrinterState.Finished => Finished,
        PrinterState.Error => Error,
        PrinterState.Attention or PrinterState.Unauthorized => Attention,
        PrinterState.Idle or PrinterState.Stopped => Idle,
        _ => NoData,
    };
}
