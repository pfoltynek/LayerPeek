using CommunityToolkit.Mvvm.Input;

using LayerPeek.App.Resources;
using LayerPeek.Core.Cooldown;
using LayerPeek.Core.Printers;

namespace LayerPeek.App.Settings;

/// <summary>
/// The cool-down threshold of the printer (T-079, logic T-080): whole degrees Celsius within
/// <see cref="CooldownThreshold.MinCelsius"/>–<see cref="CooldownThreshold.MaxCelsius"/>. An invalid value is never saved:
/// it blocks Save like the other fields, with the error in place of the hint and the "!" on the sub-tab header. Not tied to
/// the camera (the notification needs it without one), so it stays editable while the camera is off.
/// <para>
/// The window has no field for it yet: it comes with the "Widgets" settings tab (T-092, user decision 2026-10-25). Until
/// then nothing in the window can make the value invalid (saved and default thresholds are valid, "Use threshold for all
/// printers" copies only a valid one), so the Save block and the Overlay sub-tab marker cannot be triggered; T-092 moves
/// them to wherever it puts the field.
/// </para>
/// </summary>
public sealed partial class PrinterEditorViewModel
{
    private decimal? _cooldownThreshold;

    public static decimal MinCooldownThresholdCelsius => CooldownThreshold.MinCelsius;

    public static decimal MaxCooldownThresholdCelsius => CooldownThreshold.MaxCelsius;

    /// <summary>The threshold as entered (a number box: empty is <see langword="null"/>).</summary>
    public decimal? CooldownThresholdCelsius
    {
        get => _cooldownThreshold;
        set
        {
            if (SetProperty(ref _cooldownThreshold, value))
            {
                RevealErrors();
                OnErrorsChanged();
                _owner.OnPrinterEdited(nameChanged: false);
            }
        }
    }

    /// <summary>Why the threshold cannot be saved; shown under the field instead of the hint.</summary>
    public string? CooldownThresholdError => _showErrors && HasOverlayErrors ? CooldownThresholdErrorText : null;

    /// <summary>The hint under the threshold gives way to its error (T-033 pattern).</summary>
    public bool ShowsCooldownThresholdHint => CooldownThresholdError is null;

    /// <summary>A field of the Overlay sub-tab (the threshold) blocks saving (shown or not yet).</summary>
    public bool HasOverlayErrors => ValidCooldownThreshold is null;

    /// <summary>The "!" on the Overlay sub-tab header; same rule as the list marker.</summary>
    public bool ShowsOverlayErrorMarker => _showErrors && HasOverlayErrors;

    /// <summary>Accessible name of the Overlay sub-tab header.</summary>
    public string OverlayTabName => TabName(Strings.SettingsPrinterTabOverlay, ShowsOverlayErrorMarker);

    /// <summary>
    /// "Use threshold for all printers": copies a valid threshold to every other printer of the window (also unsaved ones);
    /// saved with the other changes. Needs at least two printers.
    /// </summary>
    public IRelayCommand ApplyCooldownThresholdToAllCommand { get; private set; } = null!;

    /// <summary>Tooltip of the disabled "Use … for all printers" button: why it is disabled; <see langword="null"/> while it is enabled.</summary>
    public string? ApplyToAllUnavailableReason => _owner.Printers.Count < 2 ? Strings.SettingsOverlayApplyAllSingle : null;

    /// <summary>The threshold to save; <see langword="null"/> while the entered value is not a valid one.</summary>
    internal int? ValidCooldownThreshold =>
        _cooldownThreshold is { } value
        && value >= CooldownThreshold.MinCelsius
        && value <= CooldownThreshold.MaxCelsius
        && value == decimal.Truncate(value)
            ? (int)value
            : null;

    private static string CooldownThresholdErrorText =>
        Format(Strings.SettingsCooldownThresholdError, CooldownThreshold.MinCelsius, CooldownThreshold.MaxCelsius);

    /// <summary>From the constructor: the saved threshold (the default for a new printer) and the command.</summary>
    private void InitializeCooldown(PrinterDefinition? saved)
    {
        _cooldownThreshold = saved?.CooldownThresholdCelsius ?? CooldownThreshold.DefaultCelsius;
        ApplyCooldownThresholdToAllCommand = new RelayCommand(
            () => _owner.ApplyCooldownThresholdToAll(this),
            () => _owner.IsEditable && _owner.Printers.Count > 1);
    }

    /// <summary>The number of printers changed: "Use … for all printers" needs at least two.</summary>
    internal void OnPrinterCountChanged()
    {
        ApplyCooldownThresholdToAllCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ApplyToAllUnavailableReason));
    }

    private void OnCooldownErrorsChanged()
    {
        OnPropertyChanged(nameof(CooldownThresholdError));
        OnPropertyChanged(nameof(ShowsCooldownThresholdHint));
        OnPropertyChanged(nameof(HasOverlayErrors));
        OnPropertyChanged(nameof(ShowsOverlayErrorMarker));
        OnPropertyChanged(nameof(OverlayTabName));
    }
}
