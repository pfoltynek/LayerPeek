using System.Text.Json;

using LayerPeek.Core.Settings;

using static LayerPeek.Core.Tests.Settings.SettingsMapperCameraOverlayTests;

namespace LayerPeek.Core.Tests.Settings;

/// <summary>The "printer cooled down" switch, <c>notifications.cooledDown</c> (T-080), through the source-generated JSON context.</summary>
public class SettingsMapperCooledDownNotificationTests
{
    [Theory]
    [InlineData("""{ }""")]
    [InlineData("""{ "notifications": null }""")]
    [InlineData("""{ "notifications": { } }""")]
    [InlineData("""{ "notifications": { "cooledDown": null } }""")]
    [InlineData("""{ "version": 1, "notifications": { "enabled": true, "printStarted": false, "remainingTimeMinutes": 10, "sound": "All" } }""")]
    public void MissingSwitchIsOff(string json)
    {
        AppSettings settings = Load(json, out List<string> warnings, out _);

        Assert.Empty(warnings);
        Assert.False(settings.Notifications.CooledDown);
    }

    [Fact]
    public void OldFileKeepsItsOtherNotificationSwitches()
    {
        AppSettings settings = Load("""{ "version": 1, "notifications": { "enabled": true, "printStarted": false, "offline": false, "sound": "All" } }""", out _, out _);

        Assert.False(settings.Notifications.CooledDown);
        Assert.False(settings.Notifications.PrintStarted);
        Assert.False(settings.Notifications.Offline);
        Assert.True(settings.Notifications.PrintFinished);
    }

    [Fact]
    public void DefaultOffIsNotWritten()
    {
        string json = Write(new AppSettings());

        Assert.DoesNotContain("cooledDown", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SwitchOnIsWrittenAndRoundTrips()
    {
        AppSettings settings = new() { Notifications = new NotificationSettings { CooledDown = true } };
        string json = Write(settings);

        using JsonDocument document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.GetProperty("notifications").GetProperty("cooledDown").GetBoolean());
        AppSettings loaded = Load(json, out List<string> warnings, out _);
        Assert.Empty(warnings);
        Assert.True(loaded.Notifications.CooledDown);
        Assert.Equal(settings, loaded);
    }

    [Fact]
    public void ExplicitFalseIsLoaded()
    {
        AppSettings settings = Load("""{ "notifications": { "cooledDown": false } }""", out List<string> warnings, out _);

        Assert.Empty(warnings);
        Assert.False(settings.Notifications.CooledDown);
    }

    [Fact]
    public void PropertyNameIsCaseInsensitiveLikeTheOthers()
    {
        AppSettings settings = Load("""{ "Notifications": { "CooledDown": true } }""", out _, out _);

        Assert.True(settings.Notifications.CooledDown);
    }

    [Fact]
    public void WrongTypeFailsLikeTheOtherSwitches()
    {
        // Any value of the wrong JSON type makes the whole file count as corrupted (see SettingsDocument).
        Assert.ThrowsAny<JsonException>(() => Load("""{ "notifications": { "cooledDown": "yes" } }""", out _, out _));
        Assert.ThrowsAny<JsonException>(() => Load("""{ "notifications": { "printStarted": "yes" } }""", out _, out _));
    }
}
