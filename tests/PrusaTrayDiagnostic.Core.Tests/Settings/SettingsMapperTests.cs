using System.Text.Json;

using PrusaTrayDiagnostic.Core.Settings;
using PrusaTrayDiagnostic.Core.Settings.Storage;

namespace PrusaTrayDiagnostic.Core.Tests.Settings;

/// <summary>In-memory tests of the persisted shape; file handling is covered by <see cref="JsonSettingsStoreTests"/>.</summary>
public class SettingsMapperTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(NotificationSettings.MaxRemainingTimeMinutes)]
    public void RemainingTimeRoundTrips(int minutes)
    {
        AppSettings settings = new() { Notifications = new NotificationSettings { RemainingTimeMinutes = minutes } };

        AppSettings loaded = RoundTrip(settings, out List<string> warnings);

        Assert.Empty(warnings);
        Assert.Equal(settings, loaded);
    }

    [Fact]
    public void RemainingTimeIsWrittenAsWholeMinutes()
    {
        AppSettings settings = new() { Notifications = new NotificationSettings { RemainingTimeMinutes = 15 } };

        string json = JsonSerializer.Serialize(SettingsMapper.ToDocument(settings), SettingsJsonContext.Default.SettingsDocument);

        Assert.Contains("\"remainingTimeMinutes\": 15", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ }""")]
    [InlineData("""{ "notifications": { } }""")]
    [InlineData("""{ "notifications": { "remainingTimeMinutes": null } }""")]
    public void MissingRemainingTimeUsesDefault(string json)
    {
        AppSettings settings = Load(json, out List<string> warnings);

        Assert.Empty(warnings);
        Assert.Equal(NotificationSettings.DefaultRemainingTimeMinutes, settings.Notifications.RemainingTimeMinutes);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("25", 25)]
    [InlineData("\"25\"", 25)]
    public void ValidRemainingTimeIsLoaded(string value, int expected)
    {
        AppSettings settings = Load($$"""{ "notifications": { "remainingTimeMinutes": {{value}} } }""", out List<string> warnings);

        Assert.Empty(warnings);
        Assert.Equal(expected, settings.Notifications.RemainingTimeMinutes);
    }

    [Theory]
    [InlineData("-5", 0)]
    [InlineData("99999", NotificationSettings.MaxRemainingTimeMinutes)]
    [InlineData("1e300", NotificationSettings.MaxRemainingTimeMinutes)]
    [InlineData("7.6", 8)]
    [InlineData("0.4", 0)]
    public void InvalidRemainingTimeIsCorrectedWithWarning(string value, int expected)
    {
        AppSettings settings = Load($$"""{ "notifications": { "remainingTimeMinutes": {{value}}, "printStarted": false } }""", out List<string> warnings);

        Assert.Equal(expected, settings.Notifications.RemainingTimeMinutes);
        Assert.Contains("remaining", Assert.Single(warnings), StringComparison.OrdinalIgnoreCase);
        // The rest of the notification settings is still loaded.
        Assert.False(settings.Notifications.PrintStarted);
    }

    [Theory]
    [InlineData(NotificationSound.Off, "Off")]
    [InlineData(NotificationSound.ImportantOnly, "ImportantOnly")]
    [InlineData(NotificationSound.All, "All")]
    public void SoundRoundTripsAsName(NotificationSound sound, string name)
    {
        AppSettings settings = new() { Notifications = new NotificationSettings { Sound = sound } };

        string json = JsonSerializer.Serialize(SettingsMapper.ToDocument(settings), SettingsJsonContext.Default.SettingsDocument);
        AppSettings loaded = RoundTrip(settings, out List<string> warnings);

        Assert.Contains($"\"sound\": \"{name}\"", json, StringComparison.Ordinal);
        Assert.Empty(warnings);
        Assert.Equal(settings, loaded);
    }

    [Theory]
    [InlineData("""{ "notifications": { } }""")]
    [InlineData("""{ "notifications": { "sound": null } }""")]
    public void MissingSoundUsesDefault(string json)
    {
        AppSettings settings = Load(json, out List<string> warnings);

        Assert.Empty(warnings);
        Assert.Equal(NotificationSound.ImportantOnly, settings.Notifications.Sound);
    }

    [Theory]
    [InlineData(" all ", NotificationSound.All)]
    [InlineData("OFF", NotificationSound.Off)]
    [InlineData("importantonly", NotificationSound.ImportantOnly)]
    public void SoundNameIsCaseInsensitive(string value, NotificationSound expected)
    {
        AppSettings settings = Load($$"""{ "notifications": { "sound": "{{value}}" } }""", out List<string> warnings);

        Assert.Empty(warnings);
        Assert.Equal(expected, settings.Notifications.Sound);
    }

    [Theory]
    [InlineData("loud")]
    [InlineData("2")]
    [InlineData("")]
    public void UnknownSoundUsesDefaultWithWarning(string value)
    {
        AppSettings settings = Load($$"""{ "notifications": { "sound": "{{value}}", "error": false } }""", out List<string> warnings);

        Assert.Equal(NotificationSound.ImportantOnly, settings.Notifications.Sound);
        Assert.Contains("sound", Assert.Single(warnings), StringComparison.OrdinalIgnoreCase);
        Assert.False(settings.Notifications.Error);
    }

    private static AppSettings RoundTrip(AppSettings settings, out List<string> warnings)
    {
        string json = JsonSerializer.Serialize(SettingsMapper.ToDocument(settings), SettingsJsonContext.Default.SettingsDocument);
        return Load(json, out warnings);
    }

    private static AppSettings Load(string json, out List<string> warnings)
    {
        SettingsDocument document = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.SettingsDocument)!;
        warnings = [];
        return SettingsMapper.ToSettings(document, warnings);
    }
}
