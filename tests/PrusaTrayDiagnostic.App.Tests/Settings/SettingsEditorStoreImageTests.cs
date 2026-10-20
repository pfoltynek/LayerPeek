using PrusaTrayDiagnostic.App.Printers;
using PrusaTrayDiagnostic.App.Settings;
using PrusaTrayDiagnostic.App.Tests.Printers;
using PrusaTrayDiagnostic.App.Tests.Tray;
using PrusaTrayDiagnostic.App.Tests.Tray.Menu;
using PrusaTrayDiagnostic.Core.Images;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Settings;

using static PrusaTrayDiagnostic.App.Tests.Settings.SettingsTestData;

namespace PrusaTrayDiagnostic.App.Tests.Settings;

/// <summary>
/// Printer images and detected models in the settings store (T-039): custom images are read for the window, stored only
/// after the settings were saved, deleted after a saved removal or switch away; a save takes the model detected meanwhile.
/// </summary>
public sealed class SettingsEditorStoreImageTests : IAsyncDisposable
{
    private static readonly PasswordPolicy Supported = new(IsSupported: true, MaxBytes: 2560);
    private static readonly PrinterModelCode Mk4SCode = new(1, 4, 1);
    private static readonly PrinterModelCode CoreOneCode = new(7, 1, 0);

    private readonly List<string> _log = [];
    private readonly FakeSecretStore _secrets;
    private readonly MemorySettingsStore _settings;
    private readonly RecordingApplier _applier;
    private readonly FakeWebGateway _gateway = new();
    private readonly FakeImageStore _images = new();
    private readonly CustomPrinterImages _shown = new();
    private readonly ListLogger<SettingsEditorStore> _logger = new();

    public SettingsEditorStoreImageTests()
    {
        _secrets = new FakeSecretStore(_log);
        _settings = new MemorySettingsStore(_log);
        _applier = new RecordingApplier(_log);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync() => _gateway.DisposeAsync();

    private SettingsEditorStore Create(Func<IPrinterImageStore>? images = null) =>
        new(() => _settings, _secrets, Supported, _applier, _gateway, _logger, imageStore: images ?? (() => _images), customImages: _shown);

    private static PrinterDefinition Custom(PrinterDefinition printer) => printer with { Image = PrinterImageChoice.Custom };

    private static AppSettings With(params PrinterDefinition[] printers) => AppSettings.Default with { Printers = printers };

    private static Dictionary<string, string> NoPasswords() => [];

    // ---- Load ----

    [Fact]
    public async Task LoadReadsTheImagesOfPrintersThatUseOne()
    {
        byte[] png = TestImages.Png();
        _images.Seed("a", png);
        _images.Seed("b", TestImages.Png(2));
        _settings.LoadResult = new SettingsLoadResult { Settings = With(Custom(LinkPrinter("a", "A")), LinkPrinter("b", "B")), Status = SettingsLoadStatus.Loaded };

        SettingsEditorData data = await Create().LoadAsync(Ct);

        Assert.Same(png, Assert.Single(data.CustomImages!).Value);
        Assert.Equal(["read a"], _images.Operations);
    }

    [Fact]
    public async Task UnreadableImageOrStoreLeavesItOut()
    {
        _images.FailRead.Add("a");
        _settings.LoadResult = new SettingsLoadResult { Settings = With(Custom(LinkPrinter("a", "A"))), Status = SettingsLoadStatus.Loaded };

        Assert.Null((await Create().LoadAsync(Ct)).CustomImages);
        Assert.Null((await Create(() => throw new InvalidOperationException("no folder")).LoadAsync(Ct)).CustomImages);
    }

    // ---- Save ----

    [Fact]
    public async Task NewImageIsStoredAfterTheSettingsAndShownByTheTray()
    {
        AppSettings updated = With(Custom(LinkPrinter("a", "A")));
        byte[] png = TestImages.Png();

        SettingsSaveResult result = await Create().SaveAsync(new SettingsChange(With(LinkPrinter("a", "A")), updated, NoPasswords(), new Dictionary<string, byte[]> { ["a"] = png }));

        Assert.Equal(SettingsSaveStatus.Saved, result.Status);
        Assert.Equal(0, result.UnsavedImages);
        Assert.Equal(png, _images.Images["a"]);
        Assert.Same(png, _shown.Get("a"));
        // Settings first, then the image, then monitoring.
        Assert.Equal(["save settings", "apply"], _log.Where(static entry => entry is "save settings" or "apply"));
        Assert.Equal(["save a"], _images.Operations);
    }

    [Fact]
    public async Task FailedSettingsSaveStoresNoImage()
    {
        _settings.SaveFailure = new IOException("disk full");

        SettingsSaveResult result = await Create().SaveAsync(new SettingsChange(With(LinkPrinter("a", "A")), With(Custom(LinkPrinter("a", "A"))), NoPasswords(), new Dictionary<string, byte[]> { ["a"] = TestImages.Png() }));

        Assert.Equal(SettingsSaveStatus.SettingsNotSaved, result.Status);
        Assert.Empty(_images.Operations);
        Assert.Null(_shown.Get("a"));
    }

    [Fact]
    public async Task ImageThatCannotBeStoredIsCounted()
    {
        _images.FailSave.Add("a");

        SettingsSaveResult result = await Create().SaveAsync(new SettingsChange(AppSettings.Default, With(Custom(LinkPrinter("a", "A"))), NoPasswords(), new Dictionary<string, byte[]> { ["a"] = TestImages.Png() }));

        Assert.Equal(SettingsSaveStatus.Saved, result.Status);
        Assert.Equal(1, result.UnsavedImages);
        Assert.Null(_shown.Get("a"));
    }

    [Fact]
    public async Task ImageOfAPrinterThatDoesNotUseItIsIgnored()
    {
        SettingsSaveResult result = await Create().SaveAsync(new SettingsChange(AppSettings.Default, With(LinkPrinter("a", "A")), NoPasswords(), new Dictionary<string, byte[]> { ["a"] = TestImages.Png() }));

        Assert.Equal(0, result.UnsavedImages);
        Assert.Empty(_images.Operations);
    }

    [Fact]
    public async Task RemovedPrinterAndSwitchAwayDeleteTheirImages()
    {
        _images.Seed("a", TestImages.Png(1));
        _images.Seed("b", TestImages.Png(2));
        _images.Seed("c", TestImages.Png(3));
        _shown.Set("a", TestImages.Png(1));
        _shown.Set("b", TestImages.Png(2));
        AppSettings original = With(Custom(LinkPrinter("a", "A")), Custom(LinkPrinter("b", "B", "192.168.1.21")), Custom(LinkPrinter("c", "C", "192.168.1.22")));
        AppSettings updated = With(LinkPrinter("b", "B", "192.168.1.21") with { Image = PrinterImageChoice.ForModel(PrinterModel.Mini) }, Custom(LinkPrinter("c", "C", "192.168.1.22")));

        SettingsSaveResult result = await Create().SaveAsync(new SettingsChange(original, updated, NoPasswords()));

        Assert.Equal(SettingsSaveStatus.Saved, result.Status);
        Assert.Equal(["delete a", "delete b"], _images.Operations);
        Assert.Equal(["c"], _images.Images.Keys);
        Assert.Null(_shown.Get("a"));
        Assert.Null(_shown.Get("b"));
    }

    [Fact]
    public async Task ImageThatCannotBeDeletedDoesNotFailTheSave()
    {
        _images.Seed("a", TestImages.Png());
        _images.FailDelete.Add("a");

        SettingsSaveResult result = await Create().SaveAsync(new SettingsChange(With(Custom(LinkPrinter("a", "A"))), AppSettings.Default, NoPasswords()));

        Assert.Equal(SettingsSaveStatus.Saved, result.Status);
        Assert.Contains(_logger.Entries, static entry => entry.Message.Contains("could not be deleted", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WithoutAnImageFolderNewImagesAreReportedUnsaved()
    {
        SettingsSaveResult result = await Create(() => throw new InvalidOperationException("no folder"))
            .SaveAsync(new SettingsChange(AppSettings.Default, With(Custom(LinkPrinter("a", "A"))), NoPasswords(), new Dictionary<string, byte[]> { ["a"] = TestImages.Png() }));

        Assert.Equal(SettingsSaveStatus.Saved, result.Status);
        Assert.Equal(1, result.UnsavedImages);
    }

    // ---- The latest detected model (window open while monitoring detected it) ----

    [Fact]
    public async Task SaveTakesTheModelDetectedWhileTheWindowWasOpen()
    {
        PrinterDefinition loaded = LinkPrinter("a", "A");
        _applier.CurrentSettings = With(loaded with { DetectedModel = Mk4SCode });

        await Create().SaveAsync(new SettingsChange(With(loaded), With(loaded with { Name = "Renamed" }), NoPasswords()));

        Assert.Equal(Mk4SCode, Assert.Single(_settings.Saved!.Printers).DetectedModel);
        Assert.Equal("Renamed", _settings.Saved.Printers[0].Name);
        Assert.Equal(Mk4SCode, Assert.Single(Assert.Single(_applier.Applied).Printers).DetectedModel);
    }

    [Fact]
    public async Task ModelChangedInTheWindowWins()
    {
        // A connection test in the window detected another model: it is newer than what monitoring knew at the start.
        PrinterDefinition loaded = LinkPrinter("a", "A") with { DetectedModel = Mk4SCode };
        _applier.CurrentSettings = With(loaded);

        await Create().SaveAsync(new SettingsChange(With(loaded), With(loaded with { DetectedModel = CoreOneCode }), NoPasswords()));

        Assert.Equal(CoreOneCode, Assert.Single(_settings.Saved!.Printers).DetectedModel);
    }

    [Fact]
    public async Task NewAddressDoesNotTakeTheOldMachinesModel()
    {
        PrinterDefinition loaded = LinkPrinter("a", "A");
        _applier.CurrentSettings = With(loaded with { DetectedModel = Mk4SCode });

        await Create().SaveAsync(new SettingsChange(With(loaded), With(loaded with { Address = "192.168.1.99" }), NoPasswords()));

        Assert.Null(Assert.Single(_settings.Saved!.Printers).DetectedModel);
    }

    [Fact]
    public async Task NewPrinterAndUnknownMonitoringStateAreLeftAsTheyAre()
    {
        _applier.CurrentSettings = With(LinkPrinter("n", "New") with { DetectedModel = Mk4SCode });

        await Create().SaveAsync(new SettingsChange(AppSettings.Default, With(LinkPrinter("n", "New")), NoPasswords()));
        Assert.Null(Assert.Single(_settings.Saved!.Printers).DetectedModel);

        _applier.CurrentSettings = null;
        await Create().SaveAsync(new SettingsChange(With(LinkPrinter("a", "A")), With(LinkPrinter("a", "A2")), NoPasswords()));
        Assert.Null(Assert.Single(_settings.Saved!.Printers).DetectedModel);
    }
}
