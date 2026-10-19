using PrusaTrayDiagnostic.App.Cameras;
using PrusaTrayDiagnostic.App.Settings;
using PrusaTrayDiagnostic.App.Tests.Cameras;
using PrusaTrayDiagnostic.App.Tests.Tray;
using PrusaTrayDiagnostic.Core.Cameras;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Settings;

using static PrusaTrayDiagnostic.App.Tests.Cameras.FakeScreenCatalog;
using static PrusaTrayDiagnostic.App.Tests.Settings.SettingsTestData;

namespace PrusaTrayDiagnostic.App.Tests.Settings;

/// <summary>The "Camera window" sub-tab of a printer (T-035): fields, validation, monitors, saving and forgetting.</summary>
public sealed class CameraWindowSettingsTests : IDisposable
{
    private const string SizeError = "Enter a width of 160–16384 and a height of 90–16384.";

    private static readonly PasswordPolicy Supported = new(IsSupported: true, MaxBytes: 2560);
    private static readonly Uri StreamUri = new("rtsp://192.168.0.227/live");

    private static readonly CameraWindowDefaults Custom = new()
    {
        RememberLastPlacement = false,
        StartMode = CameraWindowStartMode.FullScreen,
        Monitor = new CameraMonitorId("LG HDR 4K", 1920, 1080),
        Normal = new CameraWindowLayout(new CameraWindowSize(1280, 720), CameraWindowCorner.TopRight),
        Mini = new CameraWindowLayout(new CameraWindowSize(400, 225), CameraWindowCorner.TopLeft),
    };

    private readonly CultureScope _culture = new("en-US");
    private readonly FakeEditorStore _store = new();
    private readonly MemoryCameraWindowStateStore _states = new();
    private SettingsViewModel? _viewModel;

    public void Dispose()
    {
        _viewModel?.Dispose();
        _culture.Dispose();
    }

    private static PrinterDefinition Printer(string id, string name, CameraSettings camera) => LinkPrinter(id, name, $"192.168.1.{20 + id.Length}") with { Camera = camera };

    private static CameraSettings Camera(bool enabled = true, CameraWindowDefaults? window = null) =>
        new(enabled, StreamUri) { Window = window ?? CameraWindowDefaults.Default };

    private async Task<SettingsViewModel> LoadAsync(IScreenCatalog? screens, params PrinterDefinition[] printers)
    {
        _store.Data = new SettingsEditorData(new AppSettings { Printers = printers }, SettingsLoadStatus.Loaded, [], Ids());
        SettingsViewModel vm = new(_store, new FakeConnectionTester(), Supported, cameraTester: new FakeCameraTester(), screens: screens, cameraWindowStates: _states);
        _viewModel = vm;
        await vm.LoadAsync();
        return vm;
    }

    private async Task<PrinterEditorViewModel> LoadOneAsync(CameraSettings camera, IScreenCatalog? screens = null) =>
        (await LoadAsync(screens, Printer("a", "Alpha", camera))).Printers[0];

    private async Task<AppSettings> SaveAsync(SettingsViewModel vm)
    {
        await vm.SaveCommand.ExecuteAsync(null);
        return Assert.Single(_store.Saves).Updated;
    }

    // ---- Loading ----

    [Fact]
    public async Task SavedValuesAreShown()
    {
        PrinterEditorViewModel printer = await LoadOneAsync(Camera(window: Custom), ThreeMonitors());

        Assert.False(printer.RememberLastPlacement);
        Assert.Equal(CameraWindowStartMode.FullScreen, printer.SelectedStartMode.Value);
        Assert.Equal(Custom.Monitor, printer.SelectedMonitor.Value);
        Assert.Equal("1280\u00A0×\u00A0720", printer.NormalSizeText);
        Assert.Equal(CameraWindowCorner.TopRight, printer.SelectedNormalCorner.Value);
        Assert.Equal("400\u00A0×\u00A0225", printer.MiniSizeText);
        Assert.Equal(CameraWindowCorner.TopLeft, printer.SelectedMiniCorner.Value);
        Assert.Null(printer.NormalSizeError);
        Assert.False(printer.HasCameraWindowErrors);
    }

    [Fact]
    public async Task NewPrinterShowsTheDefaults()
    {
        SettingsViewModel vm = await LoadAsync(null);
        vm.AddPrinterCommand.Execute(null);
        PrinterEditorViewModel printer = vm.Printers[0];

        Assert.True(printer.RememberLastPlacement);
        Assert.Equal(CameraWindowStartMode.Last, printer.SelectedStartMode.Value);
        Assert.Null(printer.SelectedMonitor.Value);
        Assert.Equal("960\u00A0×\u00A0540", printer.NormalSizeText);
        Assert.Equal(CameraWindowCorner.Center, printer.SelectedNormalCorner.Value);
        Assert.Equal("320\u00A0×\u00A0180", printer.MiniSizeText);
        Assert.Equal(CameraWindowCorner.BottomRight, printer.SelectedMiniCorner.Value);
        Assert.Equal(["Same as last time", "Normal window", "Mini preview", "Full screen"], printer.StartModeOptions.Select(static option => option.Text));
        Assert.Equal(["Top left", "Top right", "Bottom left", "Bottom right", "Center"], printer.CornerOptions.Select(static option => option.Text));
    }

    [Fact]
    public void SizePresetsAreTheCorePresets()
    {
        Assert.Equal(["640\u00A0×\u00A0360", "960\u00A0×\u00A0540", "1280\u00A0×\u00A0720", "1920\u00A0×\u00A01080"], CameraWindowSizeText.Presets);
    }

    // ---- Saving (and the data loss fixed in T-035: the window defaults were dropped on every save) ----

    [Fact]
    public async Task EditedValuesAreSaved()
    {
        SettingsViewModel vm = await LoadAsync(ThreeMonitors(), Printer("a", "Alpha", Camera()));
        PrinterEditorViewModel printer = vm.Printers[0];

        printer.RememberLastPlacement = false;
        printer.SelectedStartMode = printer.StartModeOptions[3];
        printer.SelectedMonitor = printer.MonitorOptions.Single(option => option.Value?.DisplayName == "LG HDR 4K");
        printer.NormalSizeText = "1280x720";
        printer.SelectedNormalCorner = printer.CornerOptions[1];
        printer.MiniSizeText = " 400 X 225 ";
        printer.SelectedMiniCorner = printer.CornerOptions[0];
        Assert.True(vm.IsDirty);
        AppSettings saved = await SaveAsync(vm);

        Assert.Equal(Custom, saved.Printers[0].Camera.Window);
    }

    public static TheoryData<string, bool, string?> CameraVariants => new()
    {
        { "enabled", true, "rtsp://192.168.0.227/live" },
        { "disabled with an address", false, "rtsp://192.168.0.227/live" },
        { "disabled without an address", false, null },
        { "disabled with an invalid address", false, "http://192.168.0.227/live" },
    };

    [Theory]
    [MemberData(nameof(CameraVariants))]
    public async Task WindowDefaultsSurviveEverySaveOfTheCamera(string variant, bool enabled, string? url)
    {
        CameraSettings camera = url is null ? CameraSettings.Disabled with { Window = Custom }
            : CameraSettings.TryParseStreamUri(url, out Uri? uri, out _) ? new CameraSettings(enabled, uri) { Window = Custom }
            : CameraSettings.FromInvalidStreamUrl(url) with { Window = Custom };
        SettingsViewModel vm = await LoadAsync(ThreeMonitors(), Printer("a", "Alpha", camera));
        Assert.False(vm.IsDirty, variant);

        // Another field changes, so that something is saved.
        vm.Printers[0].Name = "Alpha 2";
        AppSettings saved = await SaveAsync(vm);

        Assert.Equal(Custom, saved.Printers[0].Camera.Window);
        Assert.Equal(camera with { Window = Custom }, saved.Printers[0].Camera);
    }

    [Fact]
    public async Task EmptyDisabledCameraWithChangedWindowKeepsTheWindow()
    {
        SettingsViewModel vm = await LoadAsync(null, Printer("a", "Alpha", CameraSettings.Disabled));

        vm.Printers[0].MiniSizeText = "400 × 225";
        AppSettings saved = await SaveAsync(vm);

        Assert.False(saved.Printers[0].Camera.Enabled);
        Assert.Null(saved.Printers[0].Camera.StreamUri);
        Assert.Equal(new CameraWindowSize(400, 225), saved.Printers[0].Camera.Window.Mini.Size);
    }

    [Fact]
    public async Task UntouchedWindowIsNotAChange()
    {
        SettingsViewModel vm = await LoadAsync(ThreeMonitors(), Printer("a", "Alpha", Camera(window: Custom)));

        Assert.False(vm.IsDirty);
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Empty(_store.Saves);
    }

    // ---- Size text ----

    [Theory]
    [InlineData("1280 × 720", 1280, 720)]
    [InlineData("1280\u00A0×\u00A0720", 1280, 720)]
    [InlineData("1280×720", 1280, 720)]
    [InlineData("1280x720", 1280, 720)]
    [InlineData("1280X720", 1280, 720)]
    [InlineData("  1280   x   720  ", 1280, 720)]
    [InlineData("0160x0090", 160, 90)]
    [InlineData("99999x1", 99999, 1)]
    public void SizeTextIsParsed(string text, int width, int height)
    {
        Assert.True(CameraWindowSizeText.TryParse(text, out int parsedWidth, out int parsedHeight));
        Assert.Equal((width, height), (parsedWidth, parsedHeight));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1280")]
    [InlineData("1280x")]
    [InlineData("x720")]
    [InlineData("1280x720x1")]
    [InlineData("1280 * 720")]
    [InlineData("-1280x720")]
    [InlineData("+1280x720")]
    [InlineData("1,280x720")]
    [InlineData("12.5x720")]
    [InlineData("1 280x720")]
    [InlineData("١٢٨٠x720")]
    [InlineData("9999999999x720")]
    public void InvalidSizeTextIsRejected(string? text)
    {
        Assert.False(CameraWindowSizeText.TryParse(text, out _, out _));
        Assert.Null(CameraWindowSizeText.Parse(text));
    }

    [Theory]
    [InlineData("159 × 90")]
    [InlineData("160 × 89")]
    [InlineData("16385 × 90")]
    [InlineData("160 × 16385")]
    [InlineData("big")]
    public async Task InvalidSizeIsAnErrorWhileTheCameraIsOn(string text)
    {
        SettingsViewModel vm = await LoadAsync(null, Printer("a", "Alpha", Camera()));
        PrinterEditorViewModel printer = vm.Printers[0];

        printer.MiniSizeText = text;

        Assert.Null(printer.NormalSizeError);
        Assert.Equal(SizeError, printer.MiniSizeError);
        Assert.True(printer.HasCameraWindowErrors);
        Assert.True(printer.ShowsCameraWindowErrorMarker);
        Assert.True(printer.ShowsErrorMarker);
        Assert.False(printer.ShowsCameraErrorMarker);
        Assert.False(printer.ShowsConnectionErrorMarker);
        Assert.Equal("Camera window – contains errors", printer.CameraWindowTabName);

        vm.SelectedPrinterTabIndex = SettingsViewModel.ConnectionPrinterTab;
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Empty(_store.Saves);
        Assert.Equal(SettingsViewModel.CameraWindowPrinterTab, vm.SelectedPrinterTabIndex);
    }

    [Theory]
    [InlineData(160, 90)]
    [InlineData(16384, 16384)]
    public async Task SizesAtTheLimitsAreValid(int width, int height)
    {
        SettingsViewModel vm = await LoadAsync(null, Printer("a", "Alpha", Camera()));

        vm.Printers[0].NormalSizeText = $"{width}x{height}";
        AppSettings saved = await SaveAsync(vm);

        Assert.Equal(new CameraWindowSize(width, height), saved.Printers[0].Camera.Window.Normal.Size);
    }

    [Fact]
    public async Task InvalidSizeIsNotValidatedWhileTheCameraIsOffAndTheSavedSizeIsKept()
    {
        // T-033 Q9 and T-035 Q6.
        SettingsViewModel vm = await LoadAsync(null, Printer("a", "Alpha", Camera(enabled: false, window: Custom)));
        PrinterEditorViewModel printer = vm.Printers[0];

        printer.NormalSizeText = "10 × 10";
        printer.SelectedNormalCorner = printer.CornerOptions[4];

        Assert.Null(printer.NormalSizeError);
        Assert.False(printer.HasCameraWindowErrors);
        Assert.False(printer.ShowsErrorMarker);
        Assert.Equal("Camera window", printer.CameraWindowTabName);
        AppSettings saved = await SaveAsync(vm);
        Assert.Equal(new CameraWindowLayout(Custom.Normal.Size, CameraWindowCorner.Center), saved.Printers[0].Camera.Window.Normal);
        Assert.Equal("10 × 10", printer.NormalSizeText);
    }

    [Fact]
    public async Task TurningTheCameraOnRevealsAnInvalidSizeAndOffHidesIt()
    {
        SettingsViewModel vm = await LoadAsync(null, Printer("a", "Alpha", Camera(enabled: false)));
        PrinterEditorViewModel printer = vm.Printers[0];
        printer.NormalSizeText = "1x1";
        List<string?> changed = [];
        printer.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        printer.CameraEnabled = true;

        Assert.Equal(SizeError, printer.NormalSizeError);
        Assert.True(printer.ShowsCameraWindowErrorMarker);
        Assert.Contains(nameof(PrinterEditorViewModel.NormalSizeError), changed);
        Assert.Contains(nameof(PrinterEditorViewModel.ShowsCameraWindowErrorMarker), changed);

        printer.CameraEnabled = false;

        Assert.Null(printer.NormalSizeError);
        Assert.False(printer.ShowsCameraWindowErrorMarker);
    }

    [Fact]
    public async Task FixingTheSizeRemovesTheErrorAndTheMarker()
    {
        SettingsViewModel vm = await LoadAsync(null, Printer("a", "Alpha", Camera()));
        PrinterEditorViewModel printer = vm.Printers[0];
        printer.NormalSizeText = "1x1";

        printer.NormalSizeText = CameraWindowSizeText.Presets[2];

        Assert.Null(printer.NormalSizeError);
        Assert.False(printer.ShowsCameraWindowErrorMarker);
        Assert.Equal("Camera window", printer.CameraWindowTabName);
    }

    [Fact]
    public async Task SizeErrorsOfAnUntouchedNewPrinterStayHiddenUntilSaving()
    {
        SettingsViewModel vm = await LoadAsync(null);
        vm.AddPrinterCommand.Execute(null);
        PrinterEditorViewModel printer = vm.Printers[0];

        Assert.Null(printer.NormalSizeError);
        Assert.False(printer.ShowsCameraWindowErrorMarker);
    }

    // ---- Navigation of a failed save: Printer, then Camera, then Camera window ----

    [Theory]
    [InlineData(true, true, SettingsViewModel.ConnectionPrinterTab)]
    [InlineData(false, true, SettingsViewModel.CameraPrinterTab)]
    [InlineData(false, false, SettingsViewModel.CameraWindowPrinterTab)]
    public async Task FailedSaveOpensTheFirstSubTabWithAnError(bool connectionError, bool cameraError, int expected)
    {
        SettingsViewModel vm = await LoadAsync(null, Printer("a", "Alpha", Camera()));
        PrinterEditorViewModel printer = vm.Printers[0];
        printer.NormalSizeText = "1x1";
        if (connectionError)
        {
            printer.Address = string.Empty;
        }

        if (cameraError)
        {
            printer.CameraUrl = "http://192.168.0.227/live";
        }

        vm.SelectedTabIndex = SettingsViewModel.GeneralTab;
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Empty(_store.Saves);
        Assert.Equal(SettingsViewModel.PrintersTab, vm.SelectedTabIndex);
        Assert.Equal(expected, vm.SelectedPrinterTabIndex);
    }

    // ---- Monitors ----

    [Fact]
    public async Task WithoutScreensOnlyThePrimaryMonitorIsOffered()
    {
        PrinterEditorViewModel printer = await LoadOneAsync(Camera());

        CameraMonitorOption only = Assert.Single(printer.MonitorOptions);
        Assert.Null(only.Value);
        Assert.Equal("Primary monitor", only.Text);
        Assert.Same(only, printer.SelectedMonitor);
    }

    [Fact]
    public async Task ConnectedMonitorsAreNumberedLeftToRightWithNameAndResolution()
    {
        PrinterEditorViewModel printer = await LoadOneAsync(Camera(), ThreeMonitors());

        Assert.Equal(
            [
                "Primary monitor",
                "Monitor 1 – LG HDR 4K (1920\u00A0×\u00A01080)",
                "Monitor 2 – DELL U2719D (2560\u00A0×\u00A01440)",
                "Monitor 3 – DELL U2719D (1440\u00A0×\u00A02560)",
            ],
            printer.MonitorOptions.Select(static option => option.Text));
        Assert.All(printer.MonitorOptions, static option => Assert.True(option.IsConnected));
        Assert.Equal(new CameraMonitorId("DELL U2719D", 1440, 2560), printer.MonitorOptions[3].Value);
    }

    [Fact]
    public async Task MonitorWithoutAFriendlyNameShowsOnlyItsNumberAndResolution()
    {
        FakeScreenCatalog screens = new(Screen(@"\\.\DISPLAY1", 0, 0, 1920, 1080, primary: true), Screen(string.Empty, 1920, 0, 1280, 1024));

        PrinterEditorViewModel printer = await LoadOneAsync(Camera(), screens);

        // A monitor without any name cannot be stored, so it is not offered.
        Assert.Equal(["Primary monitor", "Monitor 1 (1920\u00A0×\u00A01080)"], printer.MonitorOptions.Select(static option => option.Text));
        Assert.Equal(new CameraMonitorId(@"\\.\DISPLAY1", 1920, 1080), printer.MonitorOptions[1].Value);
    }

    [Fact]
    public async Task IdenticalMonitorsAreOfferedOnce()
    {
        FakeScreenCatalog screens = new(Screen("DELL U2719D", 0, 0, 2560, 1440, primary: true), Screen("DELL U2719D", 2560, 0, 2560, 1440));

        PrinterEditorViewModel printer = await LoadOneAsync(Camera(), screens);

        Assert.Equal(2, printer.MonitorOptions.Count);
    }

    [Fact]
    public async Task SavedConnectedMonitorIsSelected()
    {
        CameraWindowDefaults window = CameraWindowDefaults.Default with { Monitor = new CameraMonitorId("DELL U2719D", 1440, 2560) };

        PrinterEditorViewModel printer = await LoadOneAsync(Camera(window: window), ThreeMonitors());

        Assert.Equal(4, printer.MonitorOptions.Count);
        Assert.Same(printer.MonitorOptions[3], printer.SelectedMonitor);
    }

    [Fact]
    public async Task SavedMonitorThatIsNotConnectedIsKeptAndMarked()
    {
        CameraMonitorId missing = new("BenQ PD2700U", 3840, 2160);

        PrinterEditorViewModel printer = await LoadOneAsync(Camera(window: CameraWindowDefaults.Default with { Monitor = missing }), ThreeMonitors());

        CameraMonitorOption last = printer.MonitorOptions[^1];
        Assert.Equal("BenQ PD2700U (3840\u00A0×\u00A02160) – not connected", last.Text);
        Assert.False(last.IsConnected);
        Assert.Equal(missing, last.Value);
        Assert.Same(last, printer.SelectedMonitor);
    }

    [Fact]
    public async Task SavedMonitorWithoutAFriendlyNameThatIsNotConnected()
    {
        CameraMonitorId missing = new(@"\\.\DISPLAY3", 2560, 1440);

        PrinterEditorViewModel printer = await LoadOneAsync(Camera(window: CameraWindowDefaults.Default with { Monitor = missing }));

        Assert.Equal("Monitor (2560\u00A0×\u00A01440) – not connected", printer.SelectedMonitor.Text);
    }

    [Fact]
    public async Task SavedMonitorWithAnotherResolutionCountsAsConnected()
    {
        // T-035 Q4: the same name with another resolution (rotated, resolution changed) is the same monitor.
        CameraMonitorId rotated = new("LG HDR 4K", 1080, 1920);

        PrinterEditorViewModel printer = await LoadOneAsync(Camera(window: CameraWindowDefaults.Default with { Monitor = rotated }), ThreeMonitors());

        Assert.Equal("LG HDR 4K (1080\u00A0×\u00A01920)", printer.SelectedMonitor.Text);
        Assert.True(printer.SelectedMonitor.IsConnected);
        Assert.Equal(rotated, printer.SelectedMonitor.Value);
    }

    [Fact]
    public async Task UntouchedNotConnectedMonitorIsSavedUnchanged()
    {
        CameraWindowDefaults window = CameraWindowDefaults.Default with { Monitor = new CameraMonitorId("BenQ PD2700U", 3840, 2160) };
        SettingsViewModel vm = await LoadAsync(ThreeMonitors(), Printer("a", "Alpha", Camera(window: window)));

        vm.Printers[0].Name = "Renamed";
        AppSettings saved = await SaveAsync(vm);

        Assert.Equal(window, saved.Printers[0].Camera.Window);
    }

    // ---- Language ----

    [Fact]
    public async Task TextsFollowALanguageSwitch()
    {
        CameraWindowDefaults window = CameraWindowDefaults.Default with { Monitor = new CameraMonitorId("BenQ PD2700U", 3840, 2160) };
        SettingsViewModel vm = await LoadAsync(ThreeMonitors(), Printer("a", "Alpha", Camera(window: window)));
        PrinterEditorViewModel printer = vm.Printers[0];
        printer.MiniSizeText = "1x1";
        CameraStartModeOption selectedMode = printer.SelectedStartMode;
        List<string?> changed = [];
        selectedMode.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        using (new UiCultureScope("cs"))
        {
            vm.RefreshTexts();

            Assert.Equal("Jako minule", selectedMode.Text);
            Assert.Contains(nameof(CameraStartModeOption.Text), changed);
            Assert.Equal("Vpravo dole", printer.SelectedMiniCorner.Text);
            Assert.Equal("Hlavní monitor", printer.MonitorOptions[0].Text);
            Assert.Equal("Monitor 1 – LG HDR 4K (1920\u00A0×\u00A01080)", printer.MonitorOptions[1].Text);
            Assert.Equal("BenQ PD2700U (3840\u00A0×\u00A02160) – nepřipojený", printer.SelectedMonitor.Text);
            Assert.Equal("Zadejte šířku 160–16384 a výšku 90–16384.", printer.MiniSizeError);
            Assert.Equal("Okno kamery – obsahuje chyby", printer.CameraWindowTabName);
            Assert.Equal("Malý náhled – Velikost", printer.MiniSizeName);
            Assert.Equal("Normální okno – Umístění", printer.NormalCornerName);
        }

        // The selection and the typed text stay.
        Assert.Same(selectedMode, printer.SelectedStartMode);
        Assert.Equal("1x1", printer.MiniSizeText);
    }

    // ---- Forgetting the remembered window (T-035 Q3) ----

    [Theory]
    [InlineData("start mode")]
    [InlineData("monitor")]
    [InlineData("normal size")]
    [InlineData("normal corner")]
    [InlineData("mini size")]
    [InlineData("mini corner")]
    public async Task SavedDefaultChangeForgetsTheRememberedWindowOfThatPrinterOnly(string change)
    {
        CameraWindowState remembered = new(CameraWindowMode.Mini, new CameraWindowPlacement(1, 2, 700, 400), null);
        _states.Set("a", remembered);
        _states.Set("bb", remembered);
        SettingsViewModel vm = await LoadAsync(ThreeMonitors(), Printer("a", "Alpha", Camera()), Printer("bb", "Beta", Camera()));
        PrinterEditorViewModel printer = vm.Printers[0];

        switch (change)
        {
            case "start mode":
                printer.SelectedStartMode = printer.StartModeOptions[2];
                break;
            case "monitor":
                printer.SelectedMonitor = printer.MonitorOptions[1];
                break;
            case "normal size":
                printer.NormalSizeText = "640x360";
                break;
            case "normal corner":
                printer.SelectedNormalCorner = printer.CornerOptions[0];
                break;
            case "mini size":
                printer.MiniSizeText = "640x360";
                break;
            default:
                printer.SelectedMiniCorner = printer.CornerOptions[0];
                break;
        }

        await SaveAsync(vm);

        Assert.Same(CameraWindowState.Default, _states.Get("a"));
        Assert.Equal(1, _states.GetRemoveCount("a"));
        Assert.Equal(remembered, _states.Get("bb"));
        Assert.Equal(0, _states.GetRemoveCount("bb"));
    }

    [Fact]
    public async Task RememberSwitchAloneDoesNotForget()
    {
        CameraWindowState remembered = new(CameraWindowMode.Mini, new CameraWindowPlacement(1, 2, 700, 400), null);
        _states.Set("a", remembered);
        SettingsViewModel vm = await LoadAsync(null, Printer("a", "Alpha", Camera()));

        vm.Printers[0].RememberLastPlacement = false;
        AppSettings saved = await SaveAsync(vm);

        Assert.False(saved.Printers[0].Camera.Window.RememberLastPlacement);
        Assert.Equal(remembered, _states.Get("a"));
        Assert.Equal(0, _states.GetRemoveCount("a"));
    }

    [Fact]
    public async Task OtherChangesAndNewPrintersDoNotForget()
    {
        _states.Set("a", CameraWindowState.Default with { Mode = CameraWindowMode.Mini });
        SettingsViewModel vm = await LoadAsync(null, Printer("a", "Alpha", Camera()));

        vm.Printers[0].Name = "Renamed";
        vm.AddPrinterCommand.Execute(null);
        PrinterEditorViewModel added = vm.Printers[1];
        added.Name = "New";
        added.Address = "192.168.1.99";
        added.MiniSizeText = "640x360";
        await SaveAsync(vm);

        Assert.Equal(0, _states.GetRemoveCount("a"));
        Assert.Equal(0, _states.GetRemoveCount(added.Id));
        Assert.Equal(CameraWindowMode.Mini, _states.Get("a").Mode);
    }

    [Fact]
    public async Task FailedSaveForgetsNothing()
    {
        _states.Set("a", CameraWindowState.Default with { Mode = CameraWindowMode.Mini });
        _store.SaveResult = new SettingsSaveResult(SettingsSaveStatus.SettingsNotSaved);
        SettingsViewModel vm = await LoadAsync(null, Printer("a", "Alpha", Camera()));

        vm.Printers[0].NormalSizeText = "640x360";
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(0, _states.GetRemoveCount("a"));
        Assert.Equal(CameraWindowMode.Mini, _states.Get("a").Mode);
    }

    [Fact]
    public async Task SecondSaveComparesWithTheFirstOne()
    {
        _states.Set("a", CameraWindowState.Default with { Mode = CameraWindowMode.Mini });
        _store.SaveResult = new SettingsSaveResult(SettingsSaveStatus.Saved, UndeletedPasswords: 1);
        SettingsViewModel vm = await LoadAsync(null, Printer("a", "Alpha", Camera()));
        PrinterEditorViewModel printer = vm.Printers[0];
        printer.NormalSizeText = "640x360";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(1, _states.GetRemoveCount("a"));

        // The window stays open (a warning); saving again without a window change forgets nothing more.
        printer.Name = "Renamed";
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(2, _store.Saves.Count);
        Assert.Equal(1, _states.GetRemoveCount("a"));
    }
}
