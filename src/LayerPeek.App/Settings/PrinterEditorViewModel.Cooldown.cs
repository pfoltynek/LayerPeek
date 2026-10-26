using CommunityToolkit.Mvvm.Input;

using LayerPeek.App.Resources;
using LayerPeek.Core.Cooldown;
using LayerPeek.Core.Printers;

namespace LayerPeek.App.Settings;

/// <summary>
/// The cool-down threshold of the printer (T-079, logic T-080): whole degrees Celsius within
/// <see cref="CooldownThreshold.MinCelsius"/>–<see cref="CooldownThreshold.MaxCelsius"/>. An invalid value is never saved:
/// it blocks Save like the other fields, with the error in place of the hint and the "!" on the Cool-down widget, the
/// printer and the Widgets tab. Not tied to the camera (the notification needs it without one), so it stays editable while
/// the camera is off. The field is in the panel of the Cool-down widget on the Widgets tab (T-092, user decision 2026-10-25),
/// which only shows these values (<see cref="Widgets.CooldownWidgetSettings"/>).
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
    public string? CooldownThresholdError => _showErrors && HasCooldownThresholdError ? CooldownThresholdErrorText : null;

    /// <summary>The hint under the threshold gives way to its error (T-033 pattern).</summary>
    public bool ShowsCooldownThresholdHint => CooldownThresholdError is null;

    /// <summary>The threshold blocks saving (shown or not yet).</summary>
    public bool HasCooldownThresholdError => ValidCooldownThreshold is null;

    /// <summary>
    /// A value shown on the Widgets tab (the settings of a widget, today the cool-down threshold) blocks saving, shown or not
    /// yet. The values belong to the printer, also those of a widget that is not registered (yet).
    /// </summary>
    public bool HasWidgetErrors => HasCooldownThresholdError;

    /// <summary>The "!" of the printer on the Widgets tab and on the tab header; same rule as the list marker.</summary>
    public bool ShowsWidgetErrorMarker => _showErrors && HasWidgetErrors;

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
        OnPropertyChanged(nameof(HasCooldownThresholdError));
        OnPropertyChanged(nameof(HasWidgetErrors));
        OnPropertyChanged(nameof(ShowsWidgetErrorMarker));
    }
}
