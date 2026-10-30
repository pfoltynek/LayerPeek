using LayerPeek.Core.Cameras;

namespace LayerPeek.App.Settings;

/// <summary>
/// What the info strip of the mini preview shows (T-110, design T-104 2), as edited in the "Mini preview" panel of the
/// Widgets tab: seven independent items of the printer (<see cref="CameraSettings.MiniStrip"/>). Saved with the other
/// changes ("Save"; "Cancel" drops them), kept while the camera is off, included in "Restore defaults" and "Use layout for
/// all printers" (Q10 A). The panel's check boxes are disabled while the camera is off (the values stay).
/// </summary>
public sealed partial class PrinterEditorViewModel
{
    private static readonly string[] MiniStripProperties =
    [
        nameof(MiniStrip),
        nameof(MiniStripProgressBar),
        nameof(MiniStripPercent),
        nameof(MiniStripRemaining),
        nameof(MiniStripFinish),
        nameof(MiniStripCooldown),
        nameof(MiniStripNameRow),
        nameof(MiniStripChartRow),
    ];

    private CameraMiniStripSettings _miniStrip = CameraMiniStripSettings.Default;

    /// <summary>The items of the strip as edited.</summary>
    public CameraMiniStripSettings MiniStrip => _miniStrip;

    public bool MiniStripProgressBar
    {
        get => _miniStrip.ProgressBar;
        set => SetMiniStrip(_miniStrip with { ProgressBar = value });
    }

    public bool MiniStripPercent
    {
        get => _miniStrip.Percent;
        set => SetMiniStrip(_miniStrip with { Percent = value });
    }

    public bool MiniStripRemaining
    {
        get => _miniStrip.Remaining;
        set => SetMiniStrip(_miniStrip with { Remaining = value });
    }

    public bool MiniStripFinish
    {
        get => _miniStrip.Finish;
        set => SetMiniStrip(_miniStrip with { Finish = value });
    }

    public bool MiniStripCooldown
    {
        get => _miniStrip.Cooldown;
        set => SetMiniStrip(_miniStrip with { Cooldown = value });
    }

    public bool MiniStripNameRow
    {
        get => _miniStrip.NameRow;
        set => SetMiniStrip(_miniStrip with { NameRow = value });
    }

    public bool MiniStripChartRow
    {
        get => _miniStrip.ChartRow;
        set => SetMiniStrip(_miniStrip with { ChartRow = value });
    }

    /// <summary>
    /// Replaces the edited items ("Restore defaults", "Use layout for all printers", a check box); saved with the other
    /// changes.
    /// </summary>
    internal void SetMiniStrip(CameraMiniStripSettings strip)
    {
        ArgumentNullException.ThrowIfNull(strip);
        if (strip == _miniStrip)
        {
            return;
        }

        _miniStrip = strip;
        foreach (string property in MiniStripProperties)
        {
            OnPropertyChanged(property);
        }

        RevealErrors();
        _owner.OnPrinterEdited(nameChanged: false);
    }

    /// <summary>From the constructor: the saved items (everything for a new printer).</summary>
    private void InitializeMiniStrip(CameraSettings camera) => _miniStrip = camera.MiniStrip;
}
