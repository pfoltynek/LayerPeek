using LayerPeek.Core.Settings;

using static LayerPeek.Core.Tests.Settings.SettingsMapperCameraOverlayTests;

namespace LayerPeek.Core.Tests.Settings;

/// <summary>
/// T-251: "Show the frame rate in the mini preview", <see cref="AppSettings.ShowMiniPreviewFrameRate"/> (global, on by
/// default) and <c>showMiniPreviewFrameRate</c> in the settings file, written only when off.
/// </summary>
public class MiniPreviewFrameRateBadgeSettingsTests
{
    [Fact]
    public void OnIsTheDefaultAndPartOfEquality()
    {
        Assert.True(AppSettings.Default.ShowMiniPreviewFrameRate);
        Assert.NotEqual(new AppSettings(), new AppSettings { ShowMiniPreviewFrameRate = false });
        Assert.Equal(new AppSettings { ShowMiniPreviewFrameRate = false }, new AppSettings { ShowMiniPreviewFrameRate = false });
        Assert.NotEqual(new AppSettings().GetHashCode(), new AppSettings { ShowMiniPreviewFrameRate = false }.GetHashCode());
    }

    [Fact]
    public void TheDefaultIsNotWritten() =>
        Assert.DoesNotContain("showMiniPreviewFrameRate", Write(new AppSettings()), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void OffIsWrittenAndRoundTrips()
    {
        AppSettings settings = new() { ShowMiniPreviewFrameRate = false };

        Assert.Contains("\"showMiniPreviewFrameRate\": false", Write(settings), StringComparison.Ordinal);
        AppSettings loaded = RoundTrip(settings, out List<string> warnings);
        Assert.Empty(warnings);
        Assert.False(loaded.ShowMiniPreviewFrameRate);
        Assert.Equal(settings, loaded);
    }

    [Theory]
    [InlineData("""{ }""")]
    [InlineData("""{ "version": 2, "pollingIntervalSeconds": 5, "printers": [], "miniPreviewFrameRate": 5 }""")]
    [InlineData("""{ "showMiniPreviewFrameRate": null }""")]
    [InlineData("""{ "showMiniPreviewFrameRate": true }""")]
    public void AnOlderFileOrOnShowsTheBadge(string json)
    {
        AppSettings settings = Load(json, out List<string> warnings, out _);

        Assert.Empty(warnings);
        Assert.True(settings.ShowMiniPreviewFrameRate);
    }
}
