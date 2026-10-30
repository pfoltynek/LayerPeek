using LayerPeek.App.Resources;

namespace LayerPeek.App.Settings;

/// <summary>
/// T-112 (design "Návrh" 1, 4, 5): the state of each validated field of the printer for the window. <c>Has…Error</c> is
/// true exactly while the error text of the field is shown (after an edit or a save attempt, like the texts); the field then
/// gets the class <c>invalid</c> (a 2 DIP border in <c>SettingsErrorBrush</c>). <c>…HelpText</c> is what a screen reader
/// gets for the field (<c>AutomationProperties.HelpText</c>): its error while it has one, else its visible hint (or
/// nothing). The cool-down thresholds share one error text, but each field is marked on its own.
/// </summary>
public sealed partial class PrinterEditorViewModel
{
    public bool HasNameError => NameError is not null;

    public bool HasAddressError => AddressError is not null;

    public bool HasUserNameError => UserNameError is not null;

    public bool HasPasswordError => PasswordError is not null;

    public bool HasCameraUrlError => CameraUrlError is not null;

    public bool HasNormalSizeError => NormalSizeError is not null;

    public bool HasMiniSizeError => MiniSizeError is not null;

    /// <summary>The nozzle threshold is the invalid one of the pair (the bed one may be valid).</summary>
    public bool HasNozzleThresholdError => _showErrors && ValidThreshold(_cooldownNozzleThreshold) is null;

    /// <summary>The bed threshold is the invalid one of the pair (the nozzle one may be valid).</summary>
    public bool HasBedThresholdError => _showErrors && ValidThreshold(_cooldownBedThreshold) is null;

    /// <summary>The name has no hint: only its error.</summary>
    public string? NameHelpText => NameError;

    public string? AddressHelpText => AddressError ?? (ShowsAddressHint ? Strings.SettingsAddressHint : null);

    public string? UserNameHelpText => UserNameError ?? (ShowsUserNameHint ? UserNameHint : null);

    public string? PasswordHelpText => PasswordError ?? (ShowsPasswordHint ? PasswordHint : null);

    public string? CameraUrlHelpText => CameraUrlError ?? (ShowsCameraUrlHint ? Strings.SettingsCameraUrlHint : null);

    public string? NormalSizeHelpText => NormalSizeError;

    public string? MiniSizeHelpText => MiniSizeError;

    /// <summary>The common error only at the invalid threshold; the other one keeps the hint (as before T-112).</summary>
    public string? NozzleThresholdHelpText => HasNozzleThresholdError ? CooldownThresholdError : Strings.SettingsCooldownThresholdHint;

    public string? BedThresholdHelpText => HasBedThresholdError ? CooldownThresholdError : Strings.SettingsCooldownThresholdHint;

    /// <summary>
    /// The first field of <paramref name="printerTab"/> that shows an error, in reading (Tab) order (design 4); <see langword="null"/>
    /// when none does.
    /// </summary>
    internal string? FirstInvalidField(int printerTab) => printerTab switch
    {
        SettingsViewModel.ConnectionPrinterTab =>
            HasNameError ? SettingsFieldIds.PrinterName
            : HasAddressError ? SettingsFieldIds.PrinterAddress
            : HasUserNameError ? SettingsFieldIds.PrinterUserName
            : HasPasswordError ? SettingsFieldIds.PrinterPassword
            : null,
        SettingsViewModel.CameraPrinterTab => HasCameraUrlError ? SettingsFieldIds.PrinterCameraUrl : null,
        SettingsViewModel.CameraWindowPrinterTab =>
            HasNormalSizeError ? SettingsFieldIds.PrinterCameraWindowNormalSize
            : HasMiniSizeError ? SettingsFieldIds.PrinterCameraWindowMiniSize
            : null,
        _ => null,
    };

    /// <summary>The first cool-down threshold that shows an error (nozzle, then bed); <see langword="null"/> when none does.</summary>
    internal string? FirstInvalidCooldownField() =>
        HasNozzleThresholdError ? SettingsFieldIds.WidgetCooldownNozzleThreshold
        : HasBedThresholdError ? SettingsFieldIds.WidgetCooldownBedThreshold
        : null;

    /// <summary>The errors, the hints or the language changed: every field state is read again.</summary>
    private void OnFieldStatesChanged()
    {
        OnPropertyChanged(nameof(HasNameError));
        OnPropertyChanged(nameof(HasAddressError));
        OnPropertyChanged(nameof(HasUserNameError));
        OnPropertyChanged(nameof(HasPasswordError));
        OnPropertyChanged(nameof(HasCameraUrlError));
        OnPropertyChanged(nameof(HasNormalSizeError));
        OnPropertyChanged(nameof(HasMiniSizeError));
        OnPropertyChanged(nameof(HasNozzleThresholdError));
        OnPropertyChanged(nameof(HasBedThresholdError));
        OnPropertyChanged(nameof(NameHelpText));
        OnPropertyChanged(nameof(AddressHelpText));
        OnPropertyChanged(nameof(UserNameHelpText));
        OnPropertyChanged(nameof(PasswordHelpText));
        OnPropertyChanged(nameof(CameraUrlHelpText));
        OnPropertyChanged(nameof(NormalSizeHelpText));
        OnPropertyChanged(nameof(MiniSizeHelpText));
        OnPropertyChanged(nameof(NozzleThresholdHelpText));
        OnPropertyChanged(nameof(BedThresholdHelpText));
    }
}
