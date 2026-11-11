using LayerPeek.App.Localization;
using LayerPeek.App.Resources;
using LayerPeek.Core.Cameras;

namespace LayerPeek.App.Settings;

/// <summary>An item of the frame rate profiles of the mini preview (T-185).</summary>
public sealed class MiniFrameRateProfileOption(MiniPreviewFrameRateProfile value, Func<string> text) : LocalizedOption(text)
{
    public MiniPreviewFrameRateProfile Value { get; } = value;
}

/// <summary>
/// The frame rate of the mini preview on the Camera sub-tab (T-185, user decision 2026-11-11): one global value for every
/// camera, so outside the fields of the printer. The profiles Economy / Balanced / Smooth (5 / 10 fps / no limit) and a
/// slider of 1–30 fps; moving the slider off a profile's value selects "Custom", choosing a profile moves the slider to its
/// value ("Smooth", which has none, to the right end). Below them a short description of the selected profile's CPU and
/// memory load. Applied to the open mini previews right after saving, like the other settings.
/// </summary>
public sealed partial class SettingsViewModel
{
    private int? _miniFrameRate = MiniPreviewFrameRate.Default;
    private MiniPreviewFrameRateProfile _miniFrameRateProfile = MiniPreviewFrameRate.ProfileOf(MiniPreviewFrameRate.Default);
    private int _miniFrameRateSlider = MiniPreviewFrameRate.Default;

    public static double MinMiniFrameRate => MiniPreviewFrameRate.Min;

    public static double MaxMiniFrameRate => MiniPreviewFrameRate.Max;

    /// <summary>The profiles in the order of the selection.</summary>
    public IReadOnlyList<MiniFrameRateProfileOption> MiniFrameRateProfiles { get; } =
    [
        new(MiniPreviewFrameRateProfile.Economy, static () => Strings.SettingsMiniFrameRateEconomy),
        new(MiniPreviewFrameRateProfile.Balanced, static () => Strings.SettingsMiniFrameRateBalanced),
        new(MiniPreviewFrameRateProfile.Smooth, static () => Strings.SettingsMiniFrameRateSmooth),
        new(MiniPreviewFrameRateProfile.Custom, static () => Strings.SettingsMiniFrameRateCustom),
    ];

    /// <summary>
    /// The selected profile. A profile sets its limit and moves the slider there; "Custom" keeps the slider and takes its
    /// value as the limit. A list control writing <see langword="null"/> while it is rebuilt is ignored.
    /// </summary>
    public MiniFrameRateProfileOption? SelectedMiniFrameRateProfile
    {
        get => MiniFrameRateProfiles[(int)_miniFrameRateProfile];
        set
        {
            if (value is null || value.Value == _miniFrameRateProfile)
            {
                return;
            }

            int? rate = MiniPreviewFrameRate.RateOf(value.Value, _miniFrameRateSlider);
            SetMiniFrameRate(value.Value, rate, rate ?? MiniPreviewFrameRate.Max);
        }
    }

    /// <summary>
    /// The slider, whole frames per second within 1–30 (a value is rounded and clamped): the limit of the matching profile,
    /// or "Custom".
    /// </summary>
    public double MiniFrameRateSlider
    {
        get => _miniFrameRateSlider;
        set
        {
            if (double.IsNaN(value))
            {
                return;
            }

            int rate = (int)Math.Round(Math.Clamp(value, MiniPreviewFrameRate.Min, MiniPreviewFrameRate.Max), MidpointRounding.AwayFromZero);
            if (rate != _miniFrameRateSlider)
            {
                SetMiniFrameRate(MiniPreviewFrameRate.ProfileOf(rate), rate, rate);
            }
        }
    }

    /// <summary>The limit right of the slider: "10 fps", or "No limit" for "Smooth".</summary>
    public string MiniFrameRateText => _miniFrameRate is { } rate
        ? Format(Strings.SettingsMiniFrameRateValue, rate)
        : Strings.SettingsMiniFrameRateNoLimit;

    /// <summary>What the selected profile costs (CPU and memory load).</summary>
    public string MiniFrameRateDescription => _miniFrameRateProfile switch
    {
        MiniPreviewFrameRateProfile.Economy => Strings.SettingsMiniFrameRateEconomyHint,
        MiniPreviewFrameRateProfile.Balanced => Strings.SettingsMiniFrameRateBalancedHint,
        MiniPreviewFrameRateProfile.Smooth => Strings.SettingsMiniFrameRateSmoothHint,
        _ => Strings.SettingsMiniFrameRateCustomHint,
    };

    /// <summary>Loading: the saved limit, its profile and its slider position, without notifications.</summary>
    private void PopulateMiniFrameRate(int? rate)
    {
        _miniFrameRate = rate;
        _miniFrameRateProfile = MiniPreviewFrameRate.ProfileOf(rate);
        _miniFrameRateSlider = rate ?? MiniPreviewFrameRate.Max;
    }

    private void SetMiniFrameRate(MiniPreviewFrameRateProfile profile, int? rate, int slider)
    {
        _miniFrameRateProfile = profile;
        _miniFrameRate = rate;
        // The slider first: when it echoes its new value back, nothing changes any more.
        _miniFrameRateSlider = slider;
        OnPropertyChanged(nameof(MiniFrameRateSlider));
        OnPropertyChanged(nameof(SelectedMiniFrameRateProfile));
        OnPropertyChanged(nameof(MiniFrameRateText));
        OnPropertyChanged(nameof(MiniFrameRateDescription));
        UpdateDirty();
    }

    private void RefreshMiniFrameRateTexts()
    {
        foreach (MiniFrameRateProfileOption option in MiniFrameRateProfiles)
        {
            option.RefreshText();
        }

        OnPropertyChanged(nameof(MiniFrameRateText));
        OnPropertyChanged(nameof(MiniFrameRateDescription));
    }
}
