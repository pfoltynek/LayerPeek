using LayerPeek.App.Localization;
using LayerPeek.App.Resources;
using LayerPeek.Core.Cameras;

namespace LayerPeek.App.Settings;

/// <summary>An item of "Hardware decoding" (T-204).</summary>
public sealed class HardwareDecodingOption(CameraHardwareDecoding value, Func<string> text) : LocalizedOption(text)
{
    public CameraHardwareDecoding Value { get; } = value;
}

/// <summary>
/// "Hardware decoding" on the Camera tab (T-204, user decisions 2026-11-12): Automatic (the default) or Off, one value for
/// every camera (<see cref="Core.Settings.AppSettings.CameraHardwareDecoding"/>). Below it a short description of the
/// selected option and, where Automatic still means software (Windows until T-206), a note saying so. Applied right after
/// saving: running camera windows switch at their next key frame (T-203, <c>CameraHardwareDecodingObserver</c>).
/// </summary>
public sealed partial class SettingsViewModel
{
    private CameraHardwareDecoding _hardwareDecoding = CameraHardwareDecoding.Automatic;

    /// <summary>The options in the order of the selection (the index is the value).</summary>
    public IReadOnlyList<HardwareDecodingOption> HardwareDecodingOptions { get; } =
    [
        new(CameraHardwareDecoding.Automatic, static () => Strings.SettingsHardwareDecodingAutomatic),
        new(CameraHardwareDecoding.Off, static () => Strings.SettingsHardwareDecodingOff),
    ];

    /// <summary>The selected option; a list control writing <see langword="null"/> while it is rebuilt is ignored.</summary>
    public HardwareDecodingOption? SelectedHardwareDecoding
    {
        get => HardwareDecodingOptions[(int)_hardwareDecoding];
        set
        {
            if (value is null || value.Value == _hardwareDecoding)
            {
                return;
            }

            _hardwareDecoding = value.Value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HardwareDecodingDescription));
            UpdateDirty();
        }
    }

    /// <summary>What the selected option does.</summary>
    public string HardwareDecodingDescription => _hardwareDecoding == CameraHardwareDecoding.Off
        ? Strings.SettingsHardwareDecodingOffHint
        : Strings.SettingsHardwareDecodingAutomaticHint;

    /// <summary>
    /// "Automatic" still decodes in software on this OS (Windows until T-206): the tab says so (user decision 2026-11-12,
    /// question 1 a). Set by the composition root from the decoder's OS rule.
    /// </summary>
    public bool ShowsHardwareDecodingSoftwareNote { get; internal init; }

    private void RefreshHardwareDecodingTexts()
    {
        foreach (HardwareDecodingOption option in HardwareDecodingOptions)
        {
            option.RefreshText();
        }

        OnPropertyChanged(nameof(HardwareDecodingDescription));
    }
}
