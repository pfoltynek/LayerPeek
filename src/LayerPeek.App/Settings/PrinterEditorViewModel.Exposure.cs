using CommunityToolkit.Mvvm.Input;

using LayerPeek.App.Resources;
using LayerPeek.Core.Network;

namespace LayerPeek.App.Settings;

/// <summary>
/// T-182 (user decisions 2026-11-11): a warning under the printer address and under the camera address when the address is
/// unencrypted and its host may be outside the local network (<see cref="AddressExposureClassifier"/>, by the text only, no
/// DNS). Informative only: saving stays allowed. The warning takes the place of the hint (an error takes the place of both,
/// like T-033), the field gets the class <c>warning</c> (amber outline, T-112 style) and its help text for screen readers is
/// the warning. Classified from the address already parsed by the validation of each keystroke, so it costs no extra parsing.
/// </summary>
public sealed partial class PrinterEditorViewModel
{
    private bool _addressUntrusted;
    private bool _cameraUrlUntrusted;
    private IRelayCommand? _openSecurityHelpCommand;

    /// <summary>The warning under the printer address; <see langword="null"/> while there is none or an error is shown.</summary>
    public string? AddressWarning => _addressUntrusted && AddressError is null ? Strings.SettingsAddressUntrustedNetwork : null;

    /// <summary>The warning under the camera address; <see langword="null"/> while there is none or an error is shown.</summary>
    public string? CameraUrlWarning => _cameraUrlUntrusted && CameraUrlError is null ? Strings.SettingsCameraUrlUntrustedNetwork : null;

    public bool HasAddressWarning => AddressWarning is not null;

    public bool HasCameraUrlWarning => CameraUrlWarning is not null;

    /// <summary>The "More information" link next to a warning (T-183 chapter of the manual).</summary>
    public bool ShowsSecurityHelp => _owner.HasSecurityHelp;

    /// <summary>Opens the chapter about securing the access to PrusaLink in the manual.</summary>
    public IRelayCommand OpenSecurityHelpCommand => _openSecurityHelpCommand ??= new RelayCommand(_owner.OpenSecurityHelp);

    private static bool IsUntrusted(Uri? address) =>
        address is not null && AddressExposureClassifier.Classify(address) == AddressExposure.UntrustedNetwork;

    private void SetAddressUntrusted(bool value)
    {
        if (_addressUntrusted != value)
        {
            _addressUntrusted = value;
            OnWarningsChanged();
        }
    }

    private void SetCameraUrlUntrusted(bool value)
    {
        if (_cameraUrlUntrusted != value)
        {
            _cameraUrlUntrusted = value;
            OnWarningsChanged();
        }
    }

    /// <summary>A warning, an error or the language changed: the warnings, the hints and the help texts are read again.</summary>
    private void OnWarningsChanged()
    {
        OnPropertyChanged(nameof(AddressWarning));
        OnPropertyChanged(nameof(CameraUrlWarning));
        OnPropertyChanged(nameof(HasAddressWarning));
        OnPropertyChanged(nameof(HasCameraUrlWarning));
        OnPropertyChanged(nameof(ShowsAddressHint));
        OnPropertyChanged(nameof(ShowsCameraUrlHint));
        OnPropertyChanged(nameof(ShowsSecurityHelp));
        OnPropertyChanged(nameof(AddressHelpText));
        OnPropertyChanged(nameof(CameraUrlHelpText));
    }
}
