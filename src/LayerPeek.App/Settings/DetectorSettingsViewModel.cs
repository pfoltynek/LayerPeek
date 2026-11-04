using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;

using LayerPeek.App.Detection;
using LayerPeek.App.Resources;
using LayerPeek.Core.Detection;

namespace LayerPeek.App.Settings;

/// <summary>
/// One detector kind of a printer on its Detection sub-tab (T-154): on / off and the sensitivity 0–100 (T-153), with its
/// name, what it detects and a known limitation (<see cref="DetectorTexts"/>). One item per registered detector, in the order
/// of the registration (<see cref="PrintFailureDetectionService.Detectors"/>), so a new detector appears without changing
/// the sub-tab. UI thread only.
/// </summary>
public sealed class DetectorSettingsViewModel : ObservableObject
{
    private readonly Action _changed;
    private bool _enabled;
    private int _sensitivity;

    internal DetectorSettingsViewModel(string id, DetectorSettings settings, Action changed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(changed);
        Id = id;
        _enabled = settings.Enabled;
        _sensitivity = settings.Sensitivity;
        _changed = changed;
    }

    public string Id { get; }

    public static double MinSensitivity => DetectorSettings.MinSensitivity;

    public static double MaxSensitivity => DetectorSettings.MaxSensitivity;

    public string Name => DetectorTexts.Name(Id);

    /// <summary>What it detects and what not; empty for a detector without texts.</summary>
    public string Description => DetectorTexts.Description(Id) ?? string.Empty;

    public bool HasDescription => DetectorTexts.Description(Id) is not null;

    /// <summary>"Known limitation: …", shown marked; empty when it has none.</summary>
    public string Limitation => DetectorTexts.Limitation(Id) ?? string.Empty;

    public bool HasLimitation => DetectorTexts.Limitation(Id) is not null;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (SetProperty(ref _enabled, value))
            {
                _changed();
            }
        }
    }

    /// <summary>The slider's value; whole numbers within 0–100 (a slider may report fractions).</summary>
    public double Sensitivity
    {
        get => _sensitivity;
        set
        {
            int whole = double.IsFinite(value) ? (int)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), DetectorSettings.MinSensitivity, DetectorSettings.MaxSensitivity) : _sensitivity;
            if (SetProperty(ref _sensitivity, whole))
            {
                OnPropertyChanged(nameof(SensitivityText));
                _changed();
            }
        }
    }

    /// <summary>The value next to the slider.</summary>
    public string SensitivityText => _sensitivity.ToString(CultureInfo.CurrentCulture);

    /// <summary>For screen readers: "Sensitivity – &lt;name&gt;".</summary>
    public string SensitivityName => Format(Strings.SettingsDetectionSensitivityName, Name);

    /// <summary>The settings as edited.</summary>
    internal DetectorSettings ToSettings() => new() { Enabled = _enabled, Sensitivity = _sensitivity };

    /// <summary>UI thread: the language changed.</summary>
    internal void RefreshTexts()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(Limitation));
        OnPropertyChanged(nameof(SensitivityText));
        OnPropertyChanged(nameof(SensitivityName));
    }

    private static string Format(string format, object? arg0) => string.Format(CultureInfo.CurrentCulture, format, arg0);
}
