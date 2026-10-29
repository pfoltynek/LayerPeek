namespace LayerPeek.App.Tray.Icons;

/// <summary>State symbol of a macOS menu bar icon (T-018): a bare shape in the top-right corner, no disc.</summary>
internal enum MenuBarSymbol
{
    /// <summary>No symbol (idle).</summary>
    None,

    /// <summary>Three dots (⋯).</summary>
    Busy,

    /// <summary>"Play" triangle (▶), user decision 2026-10-29.</summary>
    Printing,

    /// <summary>Two bars (❚❚).</summary>
    Paused,

    /// <summary>Check mark (✓).</summary>
    Finished,

    /// <summary>Exclamation mark (!).</summary>
    Attention,

    /// <summary>Cross (×), user decision 2026-10-29.</summary>
    Error,

    /// <summary>Slash across the whole dimmed motif (the macOS <c>*.slash</c> idiom), user decision 2026-10-29.</summary>
    Slash,
}

/// <summary>
/// One macOS menu bar template icon (T-018, <c>docs/design/macos-tray-icons.md</c>): design B "Layers" in black with
/// alpha only, so that macOS recolours it for light, dark and highlighted menu bars. Drawn by <see cref="MenuBarIconRenderer"/>.
/// </summary>
/// <param name="Ink">Alpha of the hotend and of the filled part of the layers.</param>
/// <param name="Track">Alpha of the unfilled part of the layers.</param>
/// <param name="FilledFraction">Filled part of all three layers together (0–1).</param>
/// <param name="Symbol">State symbol; always drawn fully opaque.</param>
internal readonly record struct MenuBarIconAppearance(byte Ink, byte Track, double FilledFraction, MenuBarSymbol Symbol)
{
    /// <summary>Full ink (alpha 1.0).</summary>
    public const byte Opaque = 255;

    /// <summary>Unfilled layers and the dimmed offline hotend: alpha 0.35 (Apple's "disabled").</summary>
    public const byte Dimmed = 89;

    /// <summary>Unfilled layers of the dimmed offline icon: alpha 0.2.</summary>
    public const byte Faint = 51;

    /// <summary>The look of <paramref name="key"/>; the same states and progress steps as the Windows set (T-007).</summary>
    public static MenuBarIconAppearance For(TrayIconKey key) => key.Status switch
    {
        TrayStatus.Idle => Layers(0, MenuBarSymbol.None),
        TrayStatus.Busy => Layers(0, MenuBarSymbol.Busy),
        TrayStatus.Printing => Layers(key.Progress / 100.0, MenuBarSymbol.Printing),
        TrayStatus.Paused => Layers(key.Progress / 100.0, MenuBarSymbol.Paused),
        TrayStatus.Finished => Layers(1, MenuBarSymbol.Finished),
        TrayStatus.Attention => Layers(0, MenuBarSymbol.Attention),
        TrayStatus.Error => Layers(1, MenuBarSymbol.Error),
        _ => new(Dimmed, Faint, 0, MenuBarSymbol.Slash),
    };

    private static MenuBarIconAppearance Layers(double filled, MenuBarSymbol symbol) =>
        new(Opaque, Dimmed, Math.Clamp(filled, 0, 1), symbol);
}
