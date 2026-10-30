using System.Text.Json;

using LayerPeek.Core.Cameras;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;
using LayerPeek.Core.Settings.Storage;

namespace LayerPeek.Core.Tests.Settings;

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

    [Fact]
    public void UserNameRoundTrips()
    {
        AppSettings settings = new()
        {
            Printers =
            [
                new PrinterDefinition { Id = "mk4", Name = "MK4", ConnectionType = ConnectionType.PrusaLink, Address = "10.0.0.5", UserName = "workshop" },
                new PrinterDefinition { Id = "xl", Name = "XL", ConnectionType = ConnectionType.PrusaLink, Address = "10.0.0.6" },
            ],
        };

        string json = JsonSerializer.Serialize(SettingsMapper.ToDocument(settings), SettingsJsonContext.Default.SettingsDocument);
        AppSettings loaded = RoundTrip(settings, out List<string> warnings);

        Assert.Contains("\"userName\": \"workshop\"", json, StringComparison.Ordinal);
        Assert.Contains("\"userName\": \"maker\"", json, StringComparison.Ordinal);
        Assert.Empty(warnings);
        Assert.Equal(settings, loaded);
    }

    [Theory]
    [InlineData("""{ "printers": [ { "id": "mk4", "connectionType": "PrusaLink", "address": "10.0.0.5" } ] }""")]
    [InlineData("""{ "printers": [ { "id": "mk4", "connectionType": "PrusaLink", "address": "10.0.0.5", "userName": null } ] }""")]
    public void MissingUserNameUsesMakerWithoutWarning(string json)
    {
        AppSettings settings = Load(json, out List<string> warnings);

        Assert.Empty(warnings);
        Assert.Equal("maker", Assert.Single(settings.Printers).UserName);
    }

    [Theory]
    [InlineData(" workshop ", "workshop")]
    [InlineData("Maker", "Maker")]
    public void UserNameIsLoadedTrimmedAndCaseSensitive(string value, string expected)
    {
        AppSettings settings = Load($$"""{ "printers": [ { "id": "mk4", "connectionType": "PrusaLink", "address": "10.0.0.5", "userName": "{{value}}" } ] }""", out List<string> warnings);

        Assert.Empty(warnings);
        Assert.Equal(expected, Assert.Single(settings.Printers).UserName);
    }

    [Theory]
    [InlineData("\"\"")]
    [InlineData("\"   \"")]
    [InlineData("\"john:doe\"")]
    [InlineData("\"john\\ndoe\"")]
    [InlineData("\"john\\\"doe\"")]
    public void InvalidUserNameUsesMakerWithWarningAndKeepsPrinter(string value)
    {
        AppSettings settings = Load($$"""{ "printers": [ { "id": "mk4", "name": "MK4", "connectionType": "PrusaLink", "address": "10.0.0.5", "userName": {{value}} } ] }""", out List<string> warnings);

        PrinterDefinition printer = Assert.Single(settings.Printers);
        Assert.Equal("maker", printer.UserName);
        Assert.Equal("MK4", printer.Name);
        string warning = Assert.Single(warnings);
        Assert.Contains("user name", warning, StringComparison.Ordinal);
        Assert.DoesNotContain("john", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void TooLongUserNameUsesMakerWithShortWarning()
    {
        string value = new('u', 10_000);

        AppSettings settings = Load($$"""{ "printers": [ { "id": "mk4", "connectionType": "PrusaLink", "address": "10.0.0.5", "userName": "{{value}}" } ] }""", out List<string> warnings);

        Assert.Equal("maker", Assert.Single(settings.Printers).UserName);
        Assert.InRange(Assert.Single(warnings).Length, 1, 200);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("cs")]
    [InlineData("sk")]
    [InlineData("de")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("it")]
    [InlineData("pl")]
    public void LanguageRoundTripsAsCode(string code)
    {
        AppSettings settings = new() { Language = code };

        string json = JsonSerializer.Serialize(SettingsMapper.ToDocument(settings), SettingsJsonContext.Default.SettingsDocument);
        AppSettings loaded = RoundTrip(settings, out List<string> warnings);

        Assert.Contains($"\"language\": \"{code}\"", json, StringComparison.Ordinal);
        Assert.Empty(warnings);
        Assert.Equal(settings, loaded);
    }

    [Fact]
    public void SystemLanguageIsNotWritten()
    {
        string json = JsonSerializer.Serialize(SettingsMapper.ToDocument(new AppSettings()), SettingsJsonContext.Default.SettingsDocument);
        AppSettings loaded = RoundTrip(new AppSettings(), out List<string> warnings);

        Assert.DoesNotContain("language", json, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(warnings);
        Assert.Null(loaded.Language);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DebugLoggingRoundTrips(bool enabled)
    {
        AppSettings settings = new() { DebugLogging = enabled };

        string json = JsonSerializer.Serialize(SettingsMapper.ToDocument(settings), SettingsJsonContext.Default.SettingsDocument);
        AppSettings loaded = RoundTrip(settings, out List<string> warnings);

        Assert.Contains($"\"debugLogging\": {(enabled ? "true" : "false")}", json, StringComparison.Ordinal);
        Assert.Empty(warnings);
        Assert.Equal(enabled, loaded.DebugLogging);
        Assert.Equal(settings, loaded);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExplicitPrereleaseChoiceRoundTrips(bool offer)
    {
        AppSettings settings = new() { OfferPrereleaseUpdates = offer };

        string json = JsonSerializer.Serialize(SettingsMapper.ToDocument(settings), SettingsJsonContext.Default.SettingsDocument);
        AppSettings loaded = RoundTrip(settings, out List<string> warnings);

        Assert.Contains($"\"offerPrereleaseUpdates\": {(offer ? "true" : "false")}", json, StringComparison.Ordinal);
        Assert.Empty(warnings);
        Assert.Equal(offer, loaded.OfferPrereleaseUpdates);
        Assert.Equal(settings, loaded);
    }

    /// <summary>Never chosen: not written, so the default keeps following the running version (T-017).</summary>
    [Fact]
    public void UnsetPrereleaseChoiceIsNotWritten()
    {
        string json = JsonSerializer.Serialize(SettingsMapper.ToDocument(new AppSettings()), SettingsJsonContext.Default.SettingsDocument);

        Assert.DoesNotContain("offerPrereleaseUpdates", json, StringComparison.OrdinalIgnoreCase);
        Assert.Null(RoundTrip(new AppSettings(), out _).OfferPrereleaseUpdates);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheAutomaticUpdateCheckRoundTrips(bool check)
    {
        AppSettings settings = new() { CheckForUpdates = check };

        string json = JsonSerializer.Serialize(SettingsMapper.ToDocument(settings), SettingsJsonContext.Default.SettingsDocument);
        AppSettings loaded = RoundTrip(settings, out List<string> warnings);

        Assert.Contains($"\"checkForUpdates\": {(check ? "true" : "false")}", json, StringComparison.Ordinal);
        Assert.Empty(warnings);
        Assert.Equal(check, loaded.CheckForUpdates);
        Assert.Equal(settings, loaded);
    }

    /// <summary>T-058 (Q2 a): files of older versions have no such value; the check is on by default.</summary>
    [Theory]
    [InlineData("""{ "version": 1, "pollingIntervalSeconds": 5, "printers": [] }""")]
    [InlineData("""{ "checkForUpdates": null }""")]
    public void FileWithoutTheUpdateCheckLoadsWithItOn(string json)
    {
        AppSettings settings = Load(json, out List<string> warnings);

        Assert.Empty(warnings);
        Assert.True(settings.CheckForUpdates);
        Assert.True(AppSettings.Default.CheckForUpdates);
    }

    [Theory]
    [InlineData("""{ "version": 1, "pollingIntervalSeconds": 5, "printers": [] }""")]
    [InlineData("""{ "offerPrereleaseUpdates": null }""")]
    public void FileWithoutPrereleaseChoiceLoadsUnset(string json)
    {
        AppSettings settings = Load(json, out List<string> warnings);

        Assert.Empty(warnings);
        Assert.Null(settings.OfferPrereleaseUpdates);
    }

    [Theory]
    [InlineData("""{ "version": 1, "pollingIntervalSeconds": 5, "printers": [] }""")]
    [InlineData("""{ "debugLogging": null }""")]
    public void FileWithoutDebugLoggingLoadsWithItOff(string json)
    {
        AppSettings settings = Load(json, out List<string> warnings);

        Assert.Empty(warnings);
        Assert.False(settings.DebugLogging);
    }

    [Theory]
    [InlineData("""{ }""")]
    [InlineData("""{ "language": null }""")]
    public void MissingLanguageMeansSystem(string json)
    {
        AppSettings settings = Load(json, out List<string> warnings);

        Assert.Empty(warnings);
        Assert.Null(settings.Language);
    }

    [Theory]
    [InlineData(" DE ", "de")]
    [InlineData("Cs", "cs")]
    public void LanguageCodeIsCaseInsensitive(string value, string expected)
    {
        AppSettings settings = Load($$"""{ "language": "{{value}}" }""", out List<string> warnings);

        Assert.Empty(warnings);
        Assert.Equal(expected, settings.Language);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ja")]
    [InlineData("de-AT")]
    [InlineData("system")]
    [InlineData("Deutsch")]
    public void UnknownLanguageMeansSystemWithWarning(string value)
    {
        AppSettings settings = Load($$"""{ "language": "{{value}}", "pollingIntervalSeconds": 9 }""", out List<string> warnings);

        Assert.Null(settings.Language);
        Assert.Contains("language", Assert.Single(warnings), StringComparison.OrdinalIgnoreCase);
        // The rest of the settings is still loaded.
        Assert.Equal(TimeSpan.FromSeconds(9), settings.PollingInterval);
    }

    [Fact]
    public void HugeUnknownLanguageIsShortenedInWarning()
    {
        Load($$"""{ "language": "{{new string('x', 10_000)}}" }""", out List<string> warnings);

        Assert.InRange(Assert.Single(warnings).Length, 1, 200);
    }

    [Theory]
    [InlineData("")]
    [InlineData(""", "camera": null""")]
    [InlineData(""", "camera": { }""")]
    [InlineData(""", "camera": { "enabled": false }""")]
    [InlineData(""", "camera": { "enabled": null, "streamUrl": null }""")]
    public void MissingCameraValuesMeanDisabledCamera(string camera)
    {
        AppSettings settings = Load(PrinterJson(camera), out List<string> warnings);

        Assert.Empty(warnings);
        Assert.Equal(CameraSettings.Disabled, Assert.Single(settings.Printers).Camera);
    }

    [Theory]
    [InlineData(true, "rtsp://192.168.0.227/live")]
    [InlineData(false, "rtsp://192.168.0.227:8554/live")]
    [InlineData(false, null)]
    public void CameraRoundTrips(bool enabled, string? url)
    {
        AppSettings settings = WithCamera(new CameraSettings(enabled, url is null ? null : new Uri(url)));

        AppSettings loaded = RoundTrip(settings, out List<string> warnings);

        Assert.Empty(warnings);
        Assert.Equal(settings, loaded);
    }

    [Fact]
    public void CameraIsWrittenAsEnabledFlagAndUrl()
    {
        AppSettings settings = WithCamera(new CameraSettings(enabled: true, new Uri("rtsp://192.168.0.227/live")));

        string json = JsonSerializer.Serialize(SettingsMapper.ToDocument(settings), SettingsJsonContext.Default.SettingsDocument);

        Assert.Contains("\"enabled\": true", json, StringComparison.Ordinal);
        Assert.Contains("\"streamUrl\": \"rtsp://192.168.0.227/live\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void CameraUrlIsTrimmedOnLoad()
    {
        AppSettings settings = Load(PrinterJson(""", "camera": { "enabled": true, "streamUrl": " rtsp://192.168.0.227/live " }"""), out List<string> warnings);

        Assert.Empty(warnings);
        Assert.Equal("rtsp://192.168.0.227/live", Assert.Single(settings.Printers).Camera.StreamUri!.OriginalString);
    }

    [Theory]
    [InlineData(true, "http://192.168.0.227/live")]
    [InlineData(true, "not an address")]
    [InlineData(true, "rtsp:live")]
    [InlineData(false, "rtsps://192.168.0.227/live")]
    public void InvalidCameraUrlDisablesCameraKeepsUrlAndWarns(bool enabled, string url)
    {
        string camera = $$""", "camera": { "enabled": {{(enabled ? "true" : "false")}}, "streamUrl": "{{url}}" }""";

        AppSettings settings = Load(PrinterJson(camera), out List<string> warnings);

        PrinterDefinition printer = Assert.Single(settings.Printers);
        Assert.False(printer.Camera.Enabled);
        Assert.Null(printer.Camera.StreamUri);
        Assert.Equal(url, printer.Camera.InvalidStreamUrl);
        Assert.NotEqual(CameraUrlProblem.None, printer.Camera.StreamUrlProblem);
        string warning = Assert.Single(warnings);
        Assert.Contains("camera", warning, StringComparison.OrdinalIgnoreCase);
        // Warnings are shown in the UI; the address may contain credentials and is never echoed.
        Assert.DoesNotContain("secret", warning, StringComparison.Ordinal);
        Assert.DoesNotContain("192.168.0.227", warning, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://192.168.0.227/live")]
    [InlineData("  not an address  ")]
    public void InvalidCameraUrlIsWrittenBackUnchanged(string url)
    {
        AppSettings settings = WithCamera(CameraSettings.FromInvalidStreamUrl(url));

        string json = JsonSerializer.Serialize(SettingsMapper.ToDocument(settings), SettingsJsonContext.Default.SettingsDocument);
        AppSettings loaded = RoundTrip(settings, out List<string> warnings);

        Assert.Equal(url, JsonSerializer.Deserialize(json, SettingsJsonContext.Default.SettingsDocument)!.Printers![0]!.Camera!.StreamUrl);
        Assert.Contains("\"enabled\": false", json, StringComparison.Ordinal);
        Assert.Equal(settings, loaded);
        // Still invalid after the round trip, so it is reported again.
        Assert.Single(warnings);
    }

    [Theory]
    [InlineData("rtsp://admin:secret@192.168.0.227/live", "rtsp://192.168.0.227/live", null)]
    [InlineData("http://admin:secret@192.168.0.227/live", null, "http://192.168.0.227/live")]
    [InlineData("rtsp://admin:se cret@[broken/live", null, "rtsp://[broken/live")]
    // Only credentials: nothing is left, but the flag must survive (the camera becomes CameraSettings.Disabled).
    [InlineData("admin:secret@", null, null)]
    [InlineData("rtsp://admin:1234/x@192.168.0.227/live", "rtsp://192.168.0.227/live", null)]
    public void CredentialsAreRemovedFromStoredCameraUrl(string url, string? expectedUri, string? expectedInvalid)
    {
        AppSettings settings = Load(PrinterJson($$""", "camera": { "enabled": true, "streamUrl": "{{url}}" }"""), out List<string> warnings);

        CameraSettings camera = Assert.Single(settings.Printers).Camera;
        Assert.True(camera.CredentialsRemoved);
        Assert.False(camera.Enabled);
        Assert.Equal(expectedUri, camera.StreamUri?.OriginalString);
        Assert.Equal(expectedInvalid, camera.InvalidStreamUrl);
        string warning = Assert.Single(warnings);
        Assert.Contains("credentials", warning, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", warning, StringComparison.Ordinal);
        Assert.DoesNotContain("192.168.0.227", warning, StringComparison.Ordinal);

        string json = JsonSerializer.Serialize(SettingsMapper.ToDocument(settings), SettingsJsonContext.Default.SettingsDocument);
        Assert.DoesNotContain("secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("admin", json, StringComparison.Ordinal);
    }

    [Fact]
    public void EnabledCameraWithoutUrlIsDisabledWithWarning()
    {
        AppSettings settings = Load(PrinterJson(""", "camera": { "enabled": true }"""), out List<string> warnings);

        Assert.Equal(CameraSettings.Disabled, Assert.Single(settings.Printers).Camera);
        Assert.Contains("camera", Assert.Single(warnings), StringComparison.OrdinalIgnoreCase);
    }

    private static string PrinterJson(string camera) =>
        $$"""{ "printers": [ { "id": "mk4", "name": "MK4", "connectionType": "PrusaLink", "address": "192.168.0.226"{{camera}} } ] }""";

    private static AppSettings WithCamera(CameraSettings camera) => new()
    {
        Printers = [new PrinterDefinition { Id = "mk4", Name = "MK4", ConnectionType = ConnectionType.PrusaLink, Address = "192.168.0.226", Camera = camera }],
    };

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
