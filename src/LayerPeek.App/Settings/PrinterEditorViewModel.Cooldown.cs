using CommunityToolkit.Mvvm.Input;

using LayerPeek.App.Resources;
using LayerPeek.Core.Cooldown;
using LayerPeek.Core.Printers;

namespace LayerPeek.App.Settings;

/// <summary>
/// The cool-down thresholds of the printer (T-079, logic T-080; nozzle and bed since T-109): whole degrees Celsius within
/// <see cref="CooldownThreshold.MinCelsius"/>–<see cref="CooldownThreshold.MaxCelsius"/> each. An invalid value is never
/// saved: it blocks Save like the other fields, with one error in place of the hint and the "!" on the Cool-down widget,
/// the printer and the Widgets tab. Not tied to the camera (the notification needs them without one), so they stay
/// editable while the camera is off. The fields are in the panel of the Cool-down widget on the Widgets tab (T-092, user
/// decision 2026-10-25; per printer with "Use thresholds for all printers", T-109), which only shows these values
/// (<see cref="Widgets.CooldownWidgetSettings"/>).
/// </summary>
public sealed partial class PrinterEditorViewModel
{
    private decimal? _cooldownNozzleThreshold;
    private decimal? _cooldownBedThreshold;

    public static decimal MinCooldownThresholdCelsius => CooldownThreshold.MinCelsius;

    public static decimal MaxCooldownThresholdCelsius => CooldownThreshold.MaxCelsius;

    /// <summary>The nozzle threshold as entered (a number box: empty is <see langword="null"/>).</summary>
    public decimal? CooldownNozzleThresholdCelsius
    {
        get => _cooldownNozzleThreshold;
        set => SetCooldownThreshold(ref _cooldownNozzleThreshold, value);
    }

    /// <summary>The bed threshold as entered (a number box: empty is <see langword="null"/>).</summary>
    public decimal? CooldownBedThresholdCelsius
    {
        get => _cooldownBedThreshold;
        set => SetCooldownThreshold(ref _cooldownBedThreshold, value);
    }

    /// <summary>Why the thresholds cannot be saved (one text for both); shown under the fields instead of the hint.</summary>
    public string? CooldownThresholdError => _showErrors && HasCooldownThresholdError ? CooldownThresholdErrorText : null;

    /// <summary>The hint under the thresholds gives way to their error (T-033 pattern).</summary>
    public bool ShowsCooldownThresholdHint => CooldownThresholdError is null;

    /// <summary>A threshold blocks saving (shown or not yet).</summary>
    public bool HasCooldownThresholdError => ValidCooldownThresholds is null;

    /// <summary>
    /// A value shown on the Widgets tab (the settings of a widget, today the cool-down thresholds) blocks saving, shown or
    /// not yet. The values belong to the printer, also those of a widget that is not registered (yet).
    /// </summary>
    public bool HasWidgetErrors => HasCooldownThresholdError;

    /// <summary>The "!" of the printer on the Widgets tab and on the tab header; same rule as the list marker.</summary>
    public bool ShowsWidgetErrorMarker => _showErrors && HasWidgetErrors;

    /// <summary>
    /// "Use thresholds for all printers": copies both valid thresholds to every other printer of the window (also unsaved
    /// ones); saved with the other changes. Needs at least two printers.
    /// </summary>
    public IRelayCommand ApplyCooldownThresholdsToAllCommand { get; private set; } = null!;

    /// <summary>Tooltip of the disabled "Use … for all printers" button: why it is disabled; <see langword="null"/> while it is enabled.</summary>
    public string? ApplyToAllUnavailableReason => _owner.Printers.Count < 2 ? Strings.SettingsOverlayApplyAllSingle : null;

    /// <summary>The thresholds to save; <see langword="null"/> while either entered value is not a valid one.</summary>
    internal CooldownThresholds? ValidCooldownThresholds =>
        ValidThreshold(_cooldownNozzleThreshold) is { } nozzle && ValidThreshold(_cooldownBedThreshold) is { } bed
            ? new CooldownThresholds(nozzle, bed)
            : null;

    private static string CooldownThresholdErrorText =>
        Format(Strings.SettingsCooldownThresholdError, CooldownThreshold.MinCelsius, CooldownThreshold.MaxCelsius);

    /// <summary>From the constructor: the saved thresholds (the defaults for a new printer) and the command.</summary>
    private void InitializeCooldown(PrinterDefinition? saved)
    {
        CooldownThresholds thresholds = saved?.CooldownThresholds ?? CooldownThresholds.Default;
        _cooldownNozzleThreshold = thresholds.NozzleCelsius;
        _cooldownBedThreshold = thresholds.BedCelsius;
        ApplyCooldownThresholdsToAllCommand = new RelayCommand(
            () => _owner.ApplyCooldownThresholdsToAll(this),
            () => _owner.IsEditable && _owner.Printers.Count > 1);
    }

    /// <summary>"Use thresholds for all printers" of another printer: both values at once (a valid pair).</summary>
    internal void SetCooldownThresholds(CooldownThresholds thresholds)
    {
        CooldownNozzleThresholdCelsius = thresholds.NozzleCelsius;
        CooldownBedThresholdCelsius = thresholds.BedCelsius;
    }

    /// <summary>The number of printers changed: "Use … for all printers" needs at least two.</summary>
    internal void OnPrinterCountChanged()
    {
        ApplyCooldownThresholdsToAllCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ApplyToAllUnavailableReason));
    }

    private static int? ValidThreshold(decimal? entered) =>
        entered is { } value
        && value >= CooldownThreshold.MinCelsius
        && value <= CooldownThreshold.MaxCelsius
        && value == decimal.Truncate(value)
            ? (int)value
            : null;

    private void SetCooldownThreshold(ref decimal? field, decimal? value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (SetProperty(ref field, value, propertyName))
        {
            RevealErrors();
            OnErrorsChanged();
            _owner.OnPrinterEdited(nameChanged: false);
        }
    }

    private void OnCooldownErrorsChanged()
    {
        OnPropertyChanged(nameof(CooldownThresholdError));
        OnPropertyChanged(nameof(ShowsCooldownThresholdHint));
        OnPropertyChanged(nameof(HasCooldownThresholdError));
        OnPropertyChanged(nameof(HasWidgetErrors));
        OnPropertyChanged(nameof(ShowsWidgetErrorMarker));
    }
}
