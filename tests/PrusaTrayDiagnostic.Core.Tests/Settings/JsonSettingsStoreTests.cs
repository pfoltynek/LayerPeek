using System.Text;

using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Settings;

namespace PrusaTrayDiagnostic.Core.Tests.Settings;

public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly string _directory = TestDirectories.NewDirectoryPath();
    private readonly JsonSettingsStore _store;

    public JsonSettingsStoreTests()
    {
        _store = new JsonSettingsStore(_directory);
    }

    private string FilePath => Path.Combine(_directory, JsonSettingsStore.FileName);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _store.Dispose();
        TestDirectories.Delete(_directory);
    }

    [Fact]
    public async Task LoadMissingDirectoryReturnsDefaults()
    {
        SettingsLoadResult result = await _store.LoadAsync(Ct);

        Assert.Equal(SettingsLoadStatus.NotFound, result.Status);
        Assert.Equal(AppSettings.Default.PollingInterval, result.Settings.PollingInterval);
        Assert.Empty(result.Settings.Printers);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task LoadMissingFileReturnsDefaults()
    {
        Directory.CreateDirectory(_directory);

        SettingsLoadResult result = await _store.LoadAsync(Ct);

        Assert.Equal(SettingsLoadStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task SaveThenLoadRoundTripsAllValues()
    {
        AppSettings settings = new()
        {
            PollingInterval = TimeSpan.FromSeconds(12.5),
            Language = "de",
            Notifications = new NotificationSettings { Enabled = true, PrintStarted = false, Offline = false },
            Printers =
            [
                new PrinterDefinition { Id = "mk4", Name = "Workshop MK4", ConnectionType = ConnectionType.PrusaLink, Address = "192.168.1.20" },
                new PrinterDefinition { Id = "xl", Name = "XL", ConnectionType = ConnectionType.PrusaConnect, Address = "c0ffee-uuid" },
            ],
        };

        await _store.SaveAsync(settings, Ct);
        SettingsLoadResult result = await _store.LoadAsync(Ct);

        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        Assert.Empty(result.Warnings);
        Assert.Equal(settings, result.Settings);
    }

    [Fact]
    public async Task SaveCreatesDirectoryAndLeavesNoTemporaryFiles()
    {
        await _store.SaveAsync(AppSettings.Default, Ct);
        await _store.SaveAsync(AppSettings.Default, Ct);

        Assert.Equal([FilePath], Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task SaveOverwritesExistingFile()
    {
        await _store.SaveAsync(new AppSettings { PollingInterval = TimeSpan.FromSeconds(30) }, Ct);
        await _store.SaveAsync(new AppSettings { PollingInterval = TimeSpan.FromSeconds(7) }, Ct);

        SettingsLoadResult result = await _store.LoadAsync(Ct);

        Assert.Equal(TimeSpan.FromSeconds(7), result.Settings.PollingInterval);
    }

    [Fact]
    public async Task SaveWhenCancelledKeepsOriginalFile()
    {
        await _store.SaveAsync(new AppSettings { PollingInterval = TimeSpan.FromSeconds(30) }, Ct);
        string original = await File.ReadAllTextAsync(FilePath, Ct);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _store.SaveAsync(new AppSettings { PollingInterval = TimeSpan.FromSeconds(7) }, new CancellationToken(canceled: true)));

        Assert.Equal(original, await File.ReadAllTextAsync(FilePath, Ct));
        Assert.Equal([FilePath], Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task SaveWhenReplaceFailsThrowsAndRemovesTemporaryFile()
    {
        // A directory in place of the settings file makes the final replace step fail.
        Directory.CreateDirectory(FilePath);

        await Assert.ThrowsAnyAsync<Exception>(() => _store.SaveAsync(AppSettings.Default, Ct));

        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Theory]
    [InlineData(1.001)]
    [InlineData(1.0000002)]
    [InlineData(299.999)]
    [InlineData(17.3)]
    public async Task SaveThenLoadPreservesPollingIntervalExactly(double seconds)
    {
        AppSettings settings = new() { PollingInterval = TimeSpan.FromSeconds(seconds) };

        await _store.SaveAsync(settings, Ct);
        SettingsLoadResult result = await _store.LoadAsync(Ct);

        Assert.Equal(settings.PollingInterval, result.Settings.PollingInterval);
        Assert.Equal(settings, result.Settings);
    }

    [Theory]
    [InlineData("""{ "printers": [ { "id": "a", "connectionType": "GARBAGE", "address": "h" } ] }""")]
    [InlineData("""
        { "printers": [
            { "id": "GARBAGE", "connectionType": "PrusaLink", "address": "h" },
            { "id": "GARBAGE", "connectionType": "PrusaLink", "address": "h" } ] }
        """)]
    public async Task LoadHugeInvalidValueIsShortenedInWarning(string template)
    {
        await WriteSettingsFileAsync(template.Replace("GARBAGE", new string('x', 10_000), StringComparison.Ordinal));

        SettingsLoadResult result = await _store.LoadAsync(Ct);

        Assert.InRange(Assert.Single(result.Warnings).Length, 1, 200);
    }

    [Fact]
    public async Task ConcurrentSavesProduceValidFile()
    {
        Task[] saves = [.. Enumerable.Range(1, 20).Select(i =>
            _store.SaveAsync(new AppSettings { PollingInterval = TimeSpan.FromSeconds(i) }, Ct))];

        await Task.WhenAll(saves);
        SettingsLoadResult result = await _store.LoadAsync(Ct);

        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        Assert.InRange(result.Settings.PollingInterval.TotalSeconds, 1, 20);
        Assert.Equal([FilePath], Directory.GetFiles(_directory));
    }

    [Theory]
    [InlineData("{ this is not json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("{\"pollingIntervalSeconds\": \"fast\"}")]
    [InlineData("{\"printers\": {}}")]
    public async Task LoadCorruptedFileReturnsDefaultsWithErrorAndKeepsFile(string content)
    {
        await WriteSettingsFileAsync(content);

        SettingsLoadResult result = await _store.LoadAsync(Ct);

        Assert.Equal(SettingsLoadStatus.Corrupted, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
        Assert.Empty(result.Settings.Printers);
        Assert.Equal(AppSettings.Default.PollingInterval, result.Settings.PollingInterval);
        Assert.Equal(content, await File.ReadAllTextAsync(FilePath, Ct));
    }

    [Fact]
    public async Task LoadOversizedFileIsReportedAsCorrupted()
    {
        await WriteSettingsFileAsync("{\"x\":\"" + new string('a', 2 * 1024 * 1024) + "\"}");

        SettingsLoadResult result = await _store.LoadAsync(Ct);

        Assert.Equal(SettingsLoadStatus.Corrupted, result.Status);
    }

    [Fact]
    public async Task LoadExclusivelyLockedFileIsReportedAsUnreadable()
    {
        await _store.SaveAsync(AppSettings.Default, Ct);
        await using FileStream locked = new(FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        SettingsLoadResult result = await _store.LoadAsync(Ct);

        Assert.Equal(SettingsLoadStatus.Unreadable, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    [Fact]
    public async Task LoadIsLenientToCommentsTrailingCommasAndUnknownProperties()
    {
        await WriteSettingsFileAsync("""
            {
              // hand-edited
              "version": 1,
              "futureSetting": { "nested": true },
              "pollingIntervalSeconds": 10,
              "printers": [
                { "id": "mk4", "name": "MK4", "connectionType": "prusalink", "address": "10.0.0.5", "extra": 1 },
              ],
            }
            """);

        SettingsLoadResult result = await _store.LoadAsync(Ct);

        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        Assert.Empty(result.Warnings);
        Assert.Equal(TimeSpan.FromSeconds(10), result.Settings.PollingInterval);
        PrinterDefinition printer = Assert.Single(result.Settings.Printers);
        Assert.Equal(ConnectionType.PrusaLink, printer.ConnectionType);
    }

    [Fact]
    public async Task LoadInvalidPrinterEntriesAreSkippedWithWarnings()
    {
        await WriteSettingsFileAsync("""
            {
              "printers": [
                null,
                { "id": "ok", "name": "OK", "connectionType": "PrusaLink", "address": "10.0.0.5" },
                { "name": "no id", "connectionType": "PrusaLink", "address": "10.0.0.6" },
                { "id": "no-address", "name": "x", "connectionType": "PrusaLink" },
                { "id": "bad-type", "name": "x", "connectionType": "Serial", "address": "COM3" },
                { "id": "numeric-type", "name": "x", "connectionType": "1", "address": "10.0.0.7" },
                { "id": "OK", "name": "duplicate", "connectionType": "PrusaLink", "address": "10.0.0.8" }
              ]
            }
            """);

        SettingsLoadResult result = await _store.LoadAsync(Ct);

        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        PrinterDefinition printer = Assert.Single(result.Settings.Printers);
        Assert.Equal("ok", printer.Id);
        Assert.Equal(6, result.Warnings.Count);
    }

    [Fact]
    public async Task LoadQuotedNumbersAreAccepted()
    {
        await WriteSettingsFileAsync("""{ "version": "1", "pollingIntervalSeconds": "10" }""");

        SettingsLoadResult result = await _store.LoadAsync(Ct);

        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        Assert.Equal(TimeSpan.FromSeconds(10), result.Settings.PollingInterval);
    }

    [Fact]
    public async Task SavedFileWritesNumbersAsNumbers()
    {
        await _store.SaveAsync(new AppSettings { PollingInterval = TimeSpan.FromSeconds(10) }, Ct);

        Assert.Contains("\"pollingIntervalSeconds\": 10", await File.ReadAllTextAsync(FilePath, Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadNewerFormatVersionLoadsWithWarning()
    {
        await WriteSettingsFileAsync("""{ "version": 99, "pollingIntervalSeconds": 9 }""");

        SettingsLoadResult result = await _store.LoadAsync(Ct);

        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        Assert.Equal(TimeSpan.FromSeconds(9), result.Settings.PollingInterval);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public async Task LoadPrinterWithoutNameUsesAddressAsName()
    {
        await WriteSettingsFileAsync("""
            { "printers": [ { "id": "mk4", "connectionType": "PrusaLink", "address": "10.0.0.5" } ] }
            """);

        SettingsLoadResult result = await _store.LoadAsync(Ct);

        Assert.Equal("10.0.0.5", Assert.Single(result.Settings.Printers).Name);
    }

    [Theory]
    [InlineData("0.2", 1)]
    [InlineData("-5", 1)]
    [InlineData("99999", 300)]
    public async Task LoadOutOfRangePollingIntervalIsClampedWithWarning(string seconds, double expectedSeconds)
    {
        await WriteSettingsFileAsync($$"""{ "pollingIntervalSeconds": {{seconds}} }""");

        SettingsLoadResult result = await _store.LoadAsync(Ct);

        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), result.Settings.PollingInterval);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public async Task LoadMissingValuesUseDefaults()
    {
        await WriteSettingsFileAsync("""{ "notifications": { "printFinished": false } }""");

        SettingsLoadResult result = await _store.LoadAsync(Ct);

        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        Assert.Equal(AppSettings.Default.PollingInterval, result.Settings.PollingInterval);
        Assert.False(result.Settings.Notifications.PrintFinished);
        Assert.True(result.Settings.Notifications.PrintStarted);
        Assert.True(result.Settings.Notifications.Enabled);
        Assert.Null(result.Settings.Language);
    }

    [Fact]
    public async Task LoadUnknownLanguageUsesSystemWithWarning()
    {
        await WriteSettingsFileAsync("""{ "language": "tlh", "pollingIntervalSeconds": 9 }""");

        SettingsLoadResult result = await _store.LoadAsync(Ct);

        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        Assert.Null(result.Settings.Language);
        Assert.Equal(TimeSpan.FromSeconds(9), result.Settings.PollingInterval);
        Assert.Contains("language", Assert.Single(result.Warnings), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SavedFileStoresLanguageCode()
    {
        await _store.SaveAsync(new AppSettings { Language = "pl" }, Ct);

        Assert.Contains("\"language\": \"pl\"", await File.ReadAllTextAsync(FilePath, Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SavedFileContainsNoSecretLikeProperties()
    {
        AppSettings settings = new()
        {
            Printers = [new PrinterDefinition { Id = "mk4", Name = "MK4", ConnectionType = ConnectionType.PrusaLink, Address = "10.0.0.5" }],
        };

        await _store.SaveAsync(settings, Ct);
        string json = await File.ReadAllTextAsync(FilePath, Ct);

        foreach (string forbidden in new[] { "password", "apikey", "api_key", "token", "secret", "credential" })
        {
            Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task SavedFileIsHumanReadableUtf8WithoutBom()
    {
        await _store.SaveAsync(new AppSettings { PollingInterval = TimeSpan.FromSeconds(5) }, Ct);
        byte[] bytes = await File.ReadAllBytesAsync(FilePath, Ct);
        string json = Encoding.UTF8.GetString(bytes);

        Assert.NotEqual(0xEF, bytes[0]);
        Assert.Contains("\"pollingIntervalSeconds\": 5", json, StringComparison.Ordinal);
        Assert.Contains("\"PrusaLink\"", await SaveAndReadPrinterJsonAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public void ConstructorRejectsEmptyDirectory()
    {
        Assert.ThrowsAny<ArgumentException>(() => new JsonSettingsStore(" "));
    }

    [Fact]
    public async Task SaveRejectsNull()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _store.SaveAsync(null!, Ct));
    }

    [Fact]
    public async Task LoadFileWithoutUserNameUsesMaker()
    {
        Directory.CreateDirectory(_directory);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "settings-v1-without-username.json"), FilePath);

        SettingsLoadResult result = await _store.LoadAsync(Ct);

        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        Assert.Empty(result.Warnings);
        Assert.Equal(["core-one", "xl"], result.Settings.Printers.Select(static p => p.Id));
        Assert.All(result.Settings.Printers, static p => Assert.Equal("maker", p.UserName));
    }

    [Fact]
    public async Task SaveThenLoadPreservesUserName()
    {
        AppSettings settings = new()
        {
            Printers = [new PrinterDefinition { Id = "mk4", Name = "MK4", ConnectionType = ConnectionType.PrusaLink, Address = "10.0.0.5", UserName = "workshop" }],
        };

        await _store.SaveAsync(settings, Ct);
        SettingsLoadResult result = await _store.LoadAsync(Ct);

        Assert.Equal("workshop", Assert.Single(result.Settings.Printers).UserName);
        Assert.Contains("\"userName\": \"workshop\"", await File.ReadAllTextAsync(FilePath, Ct), StringComparison.Ordinal);
    }

    private async Task<string> SaveAndReadPrinterJsonAsync()
    {
        await _store.SaveAsync(
            new AppSettings { Printers = [new PrinterDefinition { Id = "a", Name = "a", ConnectionType = ConnectionType.PrusaLink, Address = "h" }] },
            Ct);
        return await File.ReadAllTextAsync(FilePath, Ct);
    }

    private async Task WriteSettingsFileAsync(string content)
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(FilePath, content, Ct);
    }
}
