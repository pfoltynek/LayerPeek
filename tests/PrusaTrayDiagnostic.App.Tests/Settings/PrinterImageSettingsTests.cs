using PrusaTrayDiagnostic.App.Printers;
using PrusaTrayDiagnostic.App.Resources;
using PrusaTrayDiagnostic.App.Settings;
using PrusaTrayDiagnostic.App.Tests.Printers;
using PrusaTrayDiagnostic.App.Tests.Tray;
using PrusaTrayDiagnostic.Core.Images;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Settings;

using static PrusaTrayDiagnostic.App.Tests.Settings.SettingsTestData;

namespace PrusaTrayDiagnostic.App.Tests.Settings;

/// <summary>
/// The printer image in the settings window (T-039): the detected model and the image choice survive a save, a new address
/// or connection type forgets the detected model, a connection test detects it, the image menu and the custom image import
/// (kept in memory until saved, dropped by Cancel).
/// </summary>
public sealed class PrinterImageSettingsTests : IDisposable
{
    private static readonly PasswordPolicy Supported = new(IsSupported: true, MaxBytes: 2560);
    private static readonly PrinterModelCode Mk4SCode = new(1, 4, 1);
    private static readonly PrinterModelCode CoreOneCode = new(7, 1, 0);

    private readonly CultureScope _culture = new("en-US");
    private readonly FakeEditorStore _store = new();
    private readonly FakeConnectionTester _tester = new();
    private readonly FakeImageProcessor _processor = new();
    private readonly FakeImagePicker _picker = new();
    private SettingsViewModel? _viewModel;

    public void Dispose()
    {
        _viewModel?.Dispose();
        _culture.Dispose();
    }

    private async Task<SettingsViewModel> LoadAsync(IReadOnlyDictionary<string, byte[]>? customImages, params PrinterDefinition[] printers)
    {
        _store.Data = new SettingsEditorData(new AppSettings { Printers = printers }, SettingsLoadStatus.Loaded, [], Ids(), CustomImages: customImages);
        SettingsViewModel vm = new(_store, _tester, Supported, cameraTester: new FakeCameraTester(), imageImporter: new PrinterImageImporter(_processor))
        {
            ImageFilePicker = _picker,
        };
        _viewModel = vm;
        await vm.LoadAsync();
        return vm;
    }

    private Task<SettingsViewModel> LoadAsync(params PrinterDefinition[] printers) => LoadAsync(null, printers);

    private async Task<SettingsChange> SaveAsync(SettingsViewModel vm)
    {
        await vm.SaveCommand.ExecuteAsync(null);
        return Assert.Single(_store.Saves);
    }

    private static PrinterDefinition Detected(PrinterDefinition printer, PrinterModelCode code) => printer with { DetectedModel = code };

    // ---- Carrying the detected model and the choice (data-loss guard) ----

    [Fact]
    public async Task UnrelatedEditKeepsTheDetectedModelAndTheChoice()
    {
        PrinterDefinition saved = Detected(LinkPrinter("a", "Alpha"), Mk4SCode) with { Image = PrinterImageChoice.ForModel(PrinterModel.Mini) };
        SettingsViewModel vm = await LoadAsync(saved);

        vm.Printers[0].Name = "Alpha 2";
        SettingsChange change = await SaveAsync(vm);

        PrinterDefinition printer = Assert.Single(change.Updated.Printers);
        Assert.Equal(Mk4SCode, printer.DetectedModel);
        Assert.Equal(PrinterImageChoice.ForModel(PrinterModel.Mini), printer.Image);
    }

    [Fact]
    public async Task UntouchedPrinterIsNotDirtyAndSavesNothing()
    {
        SettingsViewModel vm = await LoadAsync(Detected(LinkPrinter("a", "Alpha"), Mk4SCode) with { Image = PrinterImageChoice.Custom });

        Assert.False(vm.IsDirty);
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Empty(_store.Saves);
    }

    [Theory]
    [InlineData("192.168.1.99")]
    [InlineData("printer.local")]
    public async Task NewAddressForgetsTheDetectedModel(string address)
    {
        SettingsViewModel vm = await LoadAsync(Detected(LinkPrinter("a", "Alpha"), Mk4SCode));
        PrinterEditorViewModel printer = vm.Printers[0];
        Assert.Equal("mk4", printer.Picture.IllustrationId);

        printer.Address = address;

        Assert.Same(PrinterPicture.Generic, printer.Picture);
        Assert.Equal("Automatic by model (not detected yet)", printer.AutomaticImageText);
        Assert.Null(Assert.Single((await SaveAsync(vm)).Updated.Printers).DetectedModel);
    }

    [Fact]
    public async Task SameAddressAgainRestoresTheDetectedModel()
    {
        SettingsViewModel vm = await LoadAsync(Detected(LinkPrinter("a", "Alpha", "192.168.1.20"), Mk4SCode));
        PrinterEditorViewModel printer = vm.Printers[0];

        printer.Address = "192.168.1.21";
        printer.Address = " 192.168.1.20 ";

        Assert.Equal("mk4", printer.Picture.IllustrationId);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task OtherConnectionTypeForgetsTheDetectedModel()
    {
        SettingsViewModel vm = await LoadAsync(Detected(CloudPrinter("c", "Cloud"), Mk4SCode));
        PrinterEditorViewModel printer = vm.Printers[0];
        // Prusa Connect never shows a detected model (generic illustration, T-039).
        Assert.Same(PrinterPicture.Generic, printer.Picture);
        Assert.Equal(Strings.SettingsImageGeneric, printer.AutomaticImageText);

        printer.SelectedConnectionType = printer.ConnectionTypes.Single(static option => option.Value == ConnectionType.PrusaLink);

        Assert.Null(Assert.Single((await SaveAsync(vm)).Updated.Printers).DetectedModel);
    }

    // ---- Connection test ----

    [Fact]
    public async Task SuccessfulTestStoresTheReportedModel()
    {
        SettingsViewModel vm = await LoadAsync(LinkPrinter("a", "Alpha"));
        PrinterEditorViewModel printer = vm.Printers[0];
        _tester.Immediate = new ConnectionTestResult(ConnectionTestOutcome.Success, "7.1.0", "6.4.0", ModelCode: CoreOneCode);

        await printer.TestConnectionCommand.ExecuteAsync(null);

        Assert.Equal("core-one", printer.Picture.IllustrationId);
        Assert.Equal("Automatic by model (Core One)", printer.AutomaticImageText);
        Assert.Equal("Printer image: Core One (detected)", printer.ImageName);
        Assert.True(vm.IsDirty);
        Assert.Equal(CoreOneCode, Assert.Single((await SaveAsync(vm)).Updated.Printers).DetectedModel);
    }

    [Fact]
    public async Task TestOfANewAddressDetectsForThatAddress()
    {
        SettingsViewModel vm = await LoadAsync(Detected(LinkPrinter("a", "Alpha"), Mk4SCode));
        PrinterEditorViewModel printer = vm.Printers[0];
        printer.Address = "192.168.1.77";
        _tester.Immediate = new ConnectionTestResult(ConnectionTestOutcome.Success, ModelCode: CoreOneCode);

        await printer.TestConnectionCommand.ExecuteAsync(null);

        PrinterDefinition saved = Assert.Single((await SaveAsync(vm)).Updated.Printers);
        Assert.Equal("192.168.1.77", saved.Address);
        Assert.Equal(CoreOneCode, saved.DetectedModel);
    }

    [Fact]
    public async Task FirmwareWithoutAModelClearsTheStoredOne()
    {
        SettingsViewModel vm = await LoadAsync(Detected(LinkPrinter("a", "Alpha"), Mk4SCode));
        PrinterEditorViewModel printer = vm.Printers[0];
        _tester.Immediate = new ConnectionTestResult(ConnectionTestOutcome.Success, null, "6.4.0");

        await printer.TestConnectionCommand.ExecuteAsync(null);

        Assert.Same(PrinterPicture.Generic, printer.Picture);
        Assert.Null(Assert.Single((await SaveAsync(vm)).Updated.Printers).DetectedModel);
    }

    [Fact]
    public async Task FailedTestKeepsTheModel()
    {
        SettingsViewModel vm = await LoadAsync(Detected(LinkPrinter("a", "Alpha"), Mk4SCode));
        PrinterEditorViewModel printer = vm.Printers[0];
        _tester.Immediate = new ConnectionTestResult(ConnectionTestOutcome.NotReachable);

        await printer.TestConnectionCommand.ExecuteAsync(null);

        Assert.Equal("mk4", printer.Picture.IllustrationId);
        Assert.False(vm.IsDirty);
    }

    // ---- Image menu ----

    [Fact]
    public async Task ChoosingAModelChecksItAndShowsItsIllustration()
    {
        SettingsViewModel vm = await LoadAsync(Detected(LinkPrinter("a", "Alpha"), Mk4SCode));
        PrinterEditorViewModel printer = vm.Printers[0];
        Assert.True(printer.IsAutomaticImage);
        Assert.Equal(PrinterModels.Selectable, printer.ModelOptions.Select(static option => option.Model));
        Assert.Equal(["Core One", "Core One L", "MK4S", "MK4", "MK3.9S", "MK3.9", "MK3.5S", "MK3.5", "XL+", "XL", "MINI"], printer.ModelOptions.Select(static option => option.Text));

        printer.ModelOptions.Single(static option => option.Model == PrinterModel.XlPlus).Command.Execute(null);

        Assert.Equal("xl", printer.Picture.IllustrationId);
        Assert.False(printer.IsAutomaticImage);
        Assert.Equal([PrinterModel.XlPlus], printer.ModelOptions.Where(static option => option.IsChecked).Select(static option => option.Model));
        Assert.Equal("Printer image: XL+", printer.ImageName);
        Assert.Equal(PrinterImageChoice.ForModel(PrinterModel.XlPlus), Assert.Single((await SaveAsync(vm)).Updated.Printers).Image);
    }

    [Fact]
    public async Task BackToAutomaticUsesTheDetectedModel()
    {
        SettingsViewModel vm = await LoadAsync(Detected(LinkPrinter("a", "Alpha"), Mk4SCode) with { Image = PrinterImageChoice.ForModel(PrinterModel.Mini) });
        PrinterEditorViewModel printer = vm.Printers[0];

        printer.UseAutomaticImageCommand.Execute(null);

        Assert.Equal("mk4", printer.Picture.IllustrationId);
        Assert.True(printer.IsAutomaticImage);
        Assert.DoesNotContain(printer.ModelOptions, static option => option.IsChecked);
        Assert.Equal(PrinterImageChoice.Automatic, Assert.Single((await SaveAsync(vm)).Updated.Printers).Image);
    }

    [Fact]
    public async Task PrusaConnectCanChooseAModel()
    {
        SettingsViewModel vm = await LoadAsync(CloudPrinter("c", "Cloud"));
        PrinterEditorViewModel printer = vm.Printers[0];

        printer.ModelOptions.Single(static option => option.Model == PrinterModel.CoreOneL).Command.Execute(null);

        Assert.Equal("core-one", printer.Picture.IllustrationId);
        Assert.Equal("Printer image: Core One L", printer.ImageName);
    }

    [Fact]
    public async Task UnknownDetectedModelIsGenericButNamed()
    {
        SettingsViewModel vm = await LoadAsync(Detected(LinkPrinter("a", "Alpha"), new PrinterModelCode(4, 1, 0)));
        PrinterEditorViewModel printer = vm.Printers[0];

        Assert.Same(PrinterPicture.Generic, printer.Picture);
        Assert.Equal("Automatic by model (generic illustration)", printer.AutomaticImageText);
        Assert.Equal("Printer image: generic illustration", printer.ImageName);
    }

    // ---- Custom image ----

    [Fact]
    public async Task ImportedImageIsShownAndSavedOnlyWithTheSettings()
    {
        SettingsViewModel vm = await LoadAsync(Detected(LinkPrinter("a", "Alpha"), Mk4SCode));
        PrinterEditorViewModel printer = vm.Printers[0];

        await printer.ChooseCustomImageCommand.ExecuteAsync(null);

        Assert.True(printer.IsCustomImage);
        Assert.Same(_processor.Result, printer.Picture.CustomPng);
        Assert.Equal("Printer image: custom image", printer.ImageName);
        Assert.True(vm.IsDirty);
        Assert.Empty(_store.Saves);
        SettingsChange change = await SaveAsync(vm);
        Assert.Equal(PrinterImageChoice.Custom, Assert.Single(change.Updated.Printers).Image);
        Assert.Same(_processor.Result, Assert.Single(change.NewImages!).Value);
    }

    [Fact]
    public async Task ReplacingTheStoredImageIsDirtyEvenThoughTheChoiceStays()
    {
        byte[] stored = TestImages.Png(3);
        SettingsViewModel vm = await LoadAsync(new Dictionary<string, byte[]> { ["a"] = stored }, LinkPrinter("a", "Alpha") with { Image = PrinterImageChoice.Custom });
        PrinterEditorViewModel printer = vm.Printers[0];
        Assert.Same(stored, printer.Picture.CustomPng);

        await printer.ChooseCustomImageCommand.ExecuteAsync(null);

        Assert.True(vm.IsDirty);
        SettingsChange change = await SaveAsync(vm);
        Assert.Equal(change.Original, change.Updated);
        Assert.Same(_processor.Result, change.NewImages!["a"]);
    }

    [Fact]
    public async Task CancelledDialogChangesNothing()
    {
        SettingsViewModel vm = await LoadAsync(LinkPrinter("a", "Alpha"));
        PrinterEditorViewModel printer = vm.Printers[0];
        _picker.Content = null;

        await printer.ChooseCustomImageCommand.ExecuteAsync(null);

        Assert.True(printer.IsAutomaticImage);
        Assert.Null(printer.ImageError);
        Assert.False(vm.IsDirty);
        Assert.Equal(0, _processor.Calls);
    }

    public static TheoryData<string, byte[]?, bool, string> ImportErrors => new()
    {
        { "too large", new byte[PrinterImageFormats.MaxSourceBytes + 1], false, "The image is larger than 5 MB." },
        { "not an image", "GIF89a"u8.ToArray(), false, "Only PNG and JPG images can be used." },
        { "empty file", [], false, "Only PNG and JPG images can be used." },
        { "corrupt", TestImages.Jpeg(), true, "The image could not be read. It may be damaged or have too many pixels." },
        { "unreadable", null, false, "The file could not be opened." },
    };

    [Theory]
    [MemberData(nameof(ImportErrors))]
    public async Task ImportErrorsAreShownAndChangeNothing(string scenario, byte[]? content, bool undecodable, string expected)
    {
        _ = scenario;
        SettingsViewModel vm = await LoadAsync(Detected(LinkPrinter("a", "Alpha"), Mk4SCode));
        PrinterEditorViewModel printer = vm.Printers[0];
        _picker.Content = content;
        _picker.Throw = content is null ? new IOException("locked") : null;
        _processor.Fail = undecodable;

        await printer.ChooseCustomImageCommand.ExecuteAsync(null);

        Assert.Equal(expected, printer.ImageError);
        Assert.True(printer.HasImageError);
        // Shown in the status line (the form has no room for it, T-039 layout).
        Assert.Equal(expected, vm.StatusText);
        Assert.True(vm.StatusIsError);
        Assert.True(printer.IsAutomaticImage);
        Assert.Equal("mk4", printer.Picture.IllustrationId);
        Assert.False(vm.IsDirty);
        Assert.False(printer.IsImportingImage);
    }

    [Fact]
    public async Task ImportErrorFollowsTheLanguageAndGoesWithTheNextChoice()
    {
        SettingsViewModel vm = await LoadAsync(LinkPrinter("a", "Alpha"));
        PrinterEditorViewModel printer = vm.Printers[0];
        _picker.Content = "GIF89a"u8.ToArray();
        await printer.ChooseCustomImageCommand.ExecuteAsync(null);

        using (new UiCultureScope("cs"))
        {
            vm.RefreshTexts();

            Assert.Equal("Použít lze jen obrázky PNG a JPG.", printer.ImageError);
            Assert.Equal("Automaticky podle modelu (zatím nezjištěn)", printer.AutomaticImageText);
        }

        printer.ModelOptions[0].Command.Execute(null);

        Assert.Null(printer.ImageError);
        Assert.False(printer.HasImageError);
        Assert.Null(vm.StatusText);
    }

    [Fact]
    public async Task CancelDropsTheImportedImage()
    {
        SettingsViewModel vm = await LoadAsync(LinkPrinter("a", "Alpha"));
        bool closed = false;
        vm.CloseRequested += (_, _) => closed = true;
        await vm.Printers[0].ChooseCustomImageCommand.ExecuteAsync(null);

        vm.CancelCommand.Execute(null);

        Assert.True(closed);
        Assert.Empty(_store.Saves);
    }

    [Fact]
    public async Task RemovingTheCustomImageGoesBackToAutomatic()
    {
        SettingsViewModel vm = await LoadAsync(new Dictionary<string, byte[]> { ["a"] = TestImages.Png() }, Detected(LinkPrinter("a", "Alpha"), Mk4SCode) with { Image = PrinterImageChoice.Custom });
        PrinterEditorViewModel printer = vm.Printers[0];
        Assert.True(printer.RemoveCustomImageCommand.CanExecute(null));

        printer.RemoveCustomImageCommand.Execute(null);

        Assert.True(printer.IsAutomaticImage);
        Assert.False(printer.RemoveCustomImageCommand.CanExecute(null));
        Assert.Equal("mk4", printer.Picture.IllustrationId);
        SettingsChange change = await SaveAsync(vm);
        Assert.Equal(PrinterImageChoice.Automatic, Assert.Single(change.Updated.Printers).Image);
        Assert.Null(change.NewImages);
    }

    [Fact]
    public async Task ChoosingAnotherImageAfterAnImportDropsIt()
    {
        SettingsViewModel vm = await LoadAsync(LinkPrinter("a", "Alpha"));
        PrinterEditorViewModel printer = vm.Printers[0];
        await printer.ChooseCustomImageCommand.ExecuteAsync(null);

        printer.ModelOptions[0].Command.Execute(null);

        Assert.Null(printer.PendingImage);
        Assert.Null((await SaveAsync(vm)).NewImages);
    }

    [Fact]
    public async Task SavedImportBecomesTheStoredImage()
    {
        SettingsViewModel vm = await LoadAsync(LinkPrinter("a", "Alpha"), LinkPrinter("b", "Beta", "192.168.1.21"));
        PrinterEditorViewModel printer = vm.Printers[0];
        await printer.ChooseCustomImageCommand.ExecuteAsync(null);
        _store.SaveResult = new SettingsSaveResult(SettingsSaveStatus.Saved, UndeletedPasswords: 1);

        await vm.SaveCommand.ExecuteAsync(null);

        // The window stays open (warning); the import is now the baseline.
        Assert.Null(printer.PendingImage);
        Assert.Same(_processor.Result, printer.Picture.CustomPng);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task ImageThatCouldNotBeStoredIsReported()
    {
        SettingsViewModel vm = await LoadAsync(LinkPrinter("a", "Alpha"));
        bool closed = false;
        vm.CloseRequested += (_, _) => closed = true;
        await vm.Printers[0].ChooseCustomImageCommand.ExecuteAsync(null);
        _store.SaveResult = new SettingsSaveResult(SettingsSaveStatus.Saved, UnsavedImages: 1);

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.False(closed);
        Assert.Equal(Strings.SettingsSavedImageNotStored, vm.StatusText);
        Assert.False(vm.StatusIsError);
    }

    [Fact]
    public async Task WithoutAFileDialogTheCustomImageCannotBeChosen()
    {
        SettingsViewModel vm = await LoadAsync(LinkPrinter("a", "Alpha"));
        vm.ImageFilePicker = null;

        Assert.False(vm.Printers[0].ChooseCustomImageCommand.CanExecute(null));
        await vm.Printers[0].ChooseCustomImageCommand.ExecuteAsync(null);

        Assert.Equal(0, _picker.Calls);
    }

    [Fact]
    public async Task NewPrinterStartsWithTheGenericAutomaticImage()
    {
        SettingsViewModel vm = await LoadAsync();

        vm.AddPrinterCommand.Execute(null);

        PrinterEditorViewModel printer = Assert.Single(vm.Printers);
        Assert.Same(PrinterPicture.Generic, printer.Picture);
        Assert.True(printer.IsAutomaticImage);
        Assert.Equal("Printer image: generic illustration", printer.ImageName);
    }
}
