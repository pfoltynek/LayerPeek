using LayerPeek.Core.Cameras;
using LayerPeek.Core.Settings;

using static LayerPeek.Core.Tests.Settings.SettingsMapperCameraOverlayTests;

namespace LayerPeek.Core.Tests.Settings;

/// <summary>
/// T-185: the frame rate limit of the mini preview, <see cref="AppSettings.MiniPreviewFrameRate"/> (global, 10 fps by
/// default, 1–30 fps or no limit), its profiles and <c>miniPreviewFrameRate</c> in the settings file (0 = no limit).
/// </summary>
public class MiniPreviewFrameRateSettingsTests
{
    [Fact]
    public void BalancedTenFpsIsTheDefaultAndPartOfEquality()
    {
        Assert.Equal(10, AppSettings.Default.MiniPreviewFrameRate);
        Assert.Equal(MiniPreviewFrameRateProfile.Balanced, MiniPreviewFrameRate.ProfileOf(AppSettings.Default.MiniPreviewFrameRate));
        Assert.NotEqual(new AppSettings(), new AppSettings { MiniPreviewFrameRate = null });
        Assert.Equal(new AppSettings { MiniPreviewFrameRate = 7 }, new AppSettings { MiniPreviewFrameRate = 7 });
        Assert.NotEqual(new AppSettings().GetHashCode(), new AppSettings { MiniPreviewFrameRate = 5 }.GetHashCode());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(-5)]
    public void ALimitOutsideOneToThirtyIsRejected(int rate) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new AppSettings { MiniPreviewFrameRate = rate });

    [Theory]
    [InlineData(5, MiniPreviewFrameRateProfile.Economy)]
    [InlineData(10, MiniPreviewFrameRateProfile.Balanced)]
    [InlineData(null, MiniPreviewFrameRateProfile.Smooth)]
    [InlineData(1, MiniPreviewFrameRateProfile.Custom)]
    [InlineData(6, MiniPreviewFrameRateProfile.Custom)]
    [InlineData(30, MiniPreviewFrameRateProfile.Custom)]
    public void EveryLimitBelongsToItsProfile(int? rate, MiniPreviewFrameRateProfile profile)
    {
        Assert.Equal(profile, MiniPreviewFrameRate.ProfileOf(rate));
        Assert.Equal(rate, MiniPreviewFrameRate.RateOf(profile, rate));
    }

    [Fact]
    public void TheProfilesHaveTheirLimits()
    {
        Assert.Equal(5, MiniPreviewFrameRate.RateOf(MiniPreviewFrameRateProfile.Economy, custom: 17));
        Assert.Equal(10, MiniPreviewFrameRate.RateOf(MiniPreviewFrameRateProfile.Balanced, custom: 17));
        Assert.Null(MiniPreviewFrameRate.RateOf(MiniPreviewFrameRateProfile.Smooth, custom: 17));
        Assert.Equal(17, MiniPreviewFrameRate.RateOf(MiniPreviewFrameRateProfile.Custom, custom: 17));
    }

    [Fact]
    public void TheDefaultIsNotWritten() =>
        Assert.DoesNotContain("miniPreviewFrameRate", Write(new AppSettings()), StringComparison.OrdinalIgnoreCase);

    [Theory]
    [InlineData(5, "5")]
    [InlineData(1, "1")]
    [InlineData(30, "30")]
    [InlineData(null, "0")]
    public void ALimitOrNoLimitIsWrittenAndRoundTrips(int? rate, string written)
    {
        AppSettings settings = new() { MiniPreviewFrameRate = rate };

        Assert.Contains($"\"miniPreviewFrameRate\": {written}", Write(settings), StringComparison.Ordinal);
        AppSettings loaded = RoundTrip(settings, out List<string> warnings);
        Assert.Empty(warnings);
        Assert.Equal(rate, loaded.MiniPreviewFrameRate);
        Assert.Equal(settings, loaded);
    }

    [Theory]
    [InlineData("""{ }""")]
    [InlineData("""{ "version": 2, "pollingIntervalSeconds": 5, "printers": [] }""")]
    [InlineData("""{ "miniPreviewFrameRate": null }""")]
    public void AMissingLimitIsTheDefault(string json)
    {
        AppSettings settings = Load(json, out List<string> warnings, out _);

        Assert.Empty(warnings);
        Assert.Equal(MiniPreviewFrameRate.Default, settings.MiniPreviewFrameRate);
    }

    [Theory]
    [InlineData("31")]
    [InlineData("-1")]
    [InlineData("7.5")]
    [InlineData("1e9")]
    public void AnInvalidLimitIsTheDefaultWithAWarning(string value)
    {
        AppSettings settings = Load($$"""{ "miniPreviewFrameRate": {{value}} }""", out List<string> warnings, out _);

        Assert.Equal(MiniPreviewFrameRate.Default, settings.MiniPreviewFrameRate);
        string warning = Assert.Single(warnings);
        Assert.Contains("Mini preview frame rate", warning, StringComparison.Ordinal);
    }
}
