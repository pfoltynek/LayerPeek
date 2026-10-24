using LayerPeek.App.Printers;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Settings.SettingsTestData;

namespace LayerPeek.App.Tests.Printers;

/// <summary>The stored custom images in memory (T-039) and their startup (orphan sweep only after a real load).</summary>
public sealed class CustomPrinterImagesTests
{
    private readonly FakeImageStore _store = new();
    private readonly CustomPrinterImages _images = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PrinterDefinition Custom(string id) => LinkPrinter(id, id.ToUpperInvariant()) with { Image = PrinterImageChoice.Custom };

    [Fact]
    public async Task LoadReadsOnlyPrintersThatUseACustomImage()
    {
        byte[] png = TestImages.Png();
        _store.Seed("a", png);
        _store.Seed("b", TestImages.Png(2));
        int changes = 0;
        _images.Changed += (_, _) => changes++;

        int loaded = await _images.LoadAsync(_store, [Custom("a"), LinkPrinter("b", "B")], Ct);

        Assert.Equal(1, loaded);
        Assert.Equal(["read a"], _store.Operations);
        Assert.Same(png, _images.Get("A"));
        Assert.Null(_images.Get("b"));
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task MissingOrUnreadableImagesAreSkipped()
    {
        _store.Seed("b", TestImages.Png());
        _store.FailRead.Add("b");

        int loaded = await _images.LoadAsync(_store, [Custom("a"), Custom("b")], Ct);

        Assert.Equal(0, loaded);
        Assert.Null(_images.Get("a"));
        Assert.Null(_images.Get("b"));
    }

    [Fact]
    public async Task ChangesMadeWhileLoadingWin()
    {
        _store.Seed("a", TestImages.Png(1));
        _store.Seed("b", TestImages.Png(2));
        _store.ReadGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<int> load = _images.LoadAsync(_store, [Custom("a"), Custom("b")], Ct);
        byte[] saved = TestImages.Png(9);

        _images.Set("a", saved);
        _images.Remove("b");
        _store.ReadGate.SetResult();
        await load.WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Same(saved, _images.Get("a"));
        Assert.Null(_images.Get("b"));
    }

    [Fact]
    public void SetAndRemoveRaiseChanged()
    {
        int changes = 0;
        _images.Changed += (_, _) => changes++;
        byte[] png = TestImages.Png();

        _images.Set("a", png);
        _images.Remove("A");
        _images.Remove("a");

        Assert.Equal(2, changes);
        Assert.Null(_images.Get("a"));
    }

    [Fact]
    public void PictureOfUsesTheImageOnlyWhenTheCustomImageIsChosen()
    {
        byte[] png = TestImages.Png();
        _images.Set("a", png);

        Assert.Same(png, _images.PictureOf(Custom("a")).CustomPng);
        Assert.Null(_images.PictureOf(LinkPrinter("a", "A")).CustomPng);
        Assert.False(_images.PictureOf(Custom("b")).IsCustom);
    }

    [Fact]
    public async Task StartupSweepsOrphansOnlyAfterARealLoad()
    {
        _store.Seed("a", TestImages.Png());
        _store.Seed("gone", TestImages.Png(2));
        AppSettings settings = AppSettings.Default with { Printers = [Custom("a"), LinkPrinter("b", "B")] };

        await PrinterImageStartup.RunAsync(() => _store, _images, settings, SettingsLoadStatus.Loaded, [], new ListLogger<CustomPrinterImages>(), Ct);

        Assert.Equal(["orphans a,b", "read a"], _store.Operations);
        Assert.False(_store.Images.ContainsKey("gone"));
        Assert.NotNull(_images.Get("a"));
    }

    [Fact]
    public async Task StartupKeepsImagesOfEntriesTheLoaderSkipped()
    {
        // "skipped" is in settings.json but invalid (e.g. unknown connection type): its image waits for the user's fix.
        _store.Seed("a", TestImages.Png());
        _store.Seed("skipped", TestImages.Png(2));
        _store.Seed("gone", TestImages.Png(3));
        AppSettings settings = AppSettings.Default with { Printers = [Custom("a")] };

        await PrinterImageStartup.RunAsync(() => _store, _images, settings, SettingsLoadStatus.Loaded, ["a", "skipped"], new ListLogger<CustomPrinterImages>(), Ct);

        Assert.Equal(["a", "skipped"], _store.Images.Keys.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(SettingsLoadStatus.NotFound)]
    [InlineData(SettingsLoadStatus.Corrupted)]
    [InlineData(SettingsLoadStatus.Unreadable)]
    public async Task StartupNeverSweepsAfterAFailedOrMissingLoad(SettingsLoadStatus status)
    {
        // The defaults of a failed load have no printers: a sweep would delete every image.
        _store.Seed("a", TestImages.Png());

        await PrinterImageStartup.RunAsync(() => _store, _images, AppSettings.Default, status, ["a"], new ListLogger<CustomPrinterImages>(), Ct);

        Assert.DoesNotContain(_store.Operations, static operation => operation.StartsWith("orphans", StringComparison.Ordinal));
        Assert.True(_store.Images.ContainsKey("a"));
    }

    [Fact]
    public async Task StartupWithoutAnImageFolderIsLoggedNotThrown()
    {
        ListLogger<CustomPrinterImages> logger = new();

        await PrinterImageStartup.RunAsync(() => throw new InvalidOperationException("no folder"), _images, AppSettings.Default, SettingsLoadStatus.Loaded, [], logger, Ct);

        Assert.Contains(logger.Entries, static entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
    }

    [Fact]
    public async Task StartupStopsQuietlyWhenCancelled()
    {
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        await PrinterImageStartup.RunAsync(() => _store, _images, AppSettings.Default with { Printers = [Custom("a")] }, SettingsLoadStatus.Loaded, [], new ListLogger<CustomPrinterImages>(), cancelled.Token);

        Assert.DoesNotContain("read a", _store.Operations);
    }
}
