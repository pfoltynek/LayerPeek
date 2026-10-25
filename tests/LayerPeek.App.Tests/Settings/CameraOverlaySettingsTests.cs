using LayerPeek.App.Cameras;
using LayerPeek.App.Settings;
using LayerPeek.App.Tests.Cameras;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Settings.SettingsTestData;

namespace LayerPeek.App.Tests.Settings;

/// <summary>The "Overlay" sub-tab of a printer (T-036): corner and items per mode, saving and the language switch.</summary>
public sealed class CameraOverlaySettingsTests : IDisposable
{
    private static readonly PasswordPolicy Supported = new(IsSupported: true, MaxBytes: 2560);
    private static readonly Uri StreamUri = new("rtsp://192.168.0.227/live");

    private static readonly CameraOverlayOptions Custom = new()
    {
        Corner = CameraOverlayCorner.TopRight,
        Normal = CameraOverlayItems.PrinterName | CameraOverlayItems.NozzleTemperature | CameraOverlayItems.FinishTime,
        Mini = CameraOverlayItems.Progress,
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

    private static CameraSettings Camera(bool enabled = true, CameraOverlayOptions? overlay = null) =>
        new(enabled, StreamUri) { LegacyOverlay = overlay ?? CameraOverlayOptions.Default };

    private async Task<SettingsViewModel> LoadAsync(params PrinterDefinition[] printers)
    {
        _store.Data = new SettingsEditorData(new AppSettings { Printers = printers }, SettingsLoadStatus.Loaded, [], Ids());
        SettingsViewModel vm = new(_store, new FakeConnectionTester(), Supported, cameraTester: new FakeCameraTester(), cameraWindowStates: _states);
        _viewModel = vm;
        await vm.LoadAsync();
        return vm;
    }

    private async Task<AppSettings> SaveAsync(SettingsViewModel vm)
    {
        await vm.SaveCommand.ExecuteAsync(null);
        return Assert.Single(_store.Saves).Updated;
    }

    private static CameraOverlayItemRow Row(PrinterEditorViewModel printer, CameraOverlayItems item) =>
        printer.OverlayRows.Single(row => row.Item == item);

    // ---- Loading ----

    [Fact]
    public async Task RowsFollowTheDesignOrderPrinterDataFirst()
    {
        PrinterEditorViewModel printer = (await LoadAsync(Printer("a", "Alpha", Camera()))).Printers[0];

        Assert.Equal(CameraOverlayOptions.Items, printer.OverlayRows.Select(static row => row.Item));
        string[] expected = ["Printer name", "Printer state", "Nozzle temperature", "Bed temperature", "Progress (%)", "Remaining time", "Finish time (clock)", "Print time", "File name"];
        Assert.Equal(expected, printer.OverlayRows.Select(static row => row.Text));
    }

    [Fact]
    public async Task DefaultsShowTodaysOverlay()
    {
        PrinterEditorViewModel printer = (await LoadAsync(Printer("a", "Alpha", Camera()))).Printers[0];

        Assert.Equal(CameraOverlayCorner.BottomLeft, printer.SelectedOverlayCorner.Value);
        Assert.Equal(
            new[] { CameraOverlayItems.State, CameraOverlayItems.Progress, CameraOverlayItems.RemainingTime },
            printer.OverlayRows.Where(static row => row.IsNormal).Select(static row => row.Item));
        Assert.Equal(
            new[] { CameraOverlayItems.State, CameraOverlayItems.Progress, CameraOverlayItems.RemainingTime },
            printer.OverlayRows.Where(static row => row.IsMini).Select(static row => row.Item));
        Assert.Same(CameraOverlayOptions.Default, printer.CameraOverlay);
    }

    [Fact]
    public async Task NewPrinterShowsTheDefaults()
    {
        SettingsViewModel vm = await LoadAsync();
        vm.AddPrinterCommand.Execute(null);

        Assert.Same(CameraOverlayOptions.Default, vm.SelectedPrinter!.CameraOverlay);
    }

    [Fact]
    public async Task SavedValuesAreShown()
    {
        PrinterEditorViewModel printer = (await LoadAsync(Printer("a", "Alpha", Camera(overlay: Custom)))).Printers[0];

        Assert.Equal(CameraOverlayCorner.TopRight, printer.SelectedOverlayCorner.Value);
        Assert.Equal(
            new[] { CameraOverlayItems.PrinterName, CameraOverlayItems.NozzleTemperature, CameraOverlayItems.FinishTime },
            printer.OverlayRows.Where(static row => row.IsNormal).Select(static row => row.Item));
        Assert.Equal(new[] { CameraOverlayItems.Progress }, printer.OverlayRows.Where(static row => row.IsMini).Select(static row => row.Item));
        Assert.Equal(Custom, printer.CameraOverlay);
    }

    [Fact]
    public async Task CornerOptionsAreTheFourCornersWithTheWindowPositionNames()
    {
        PrinterEditorViewModel printer = (await LoadAsync(Printer("a", "Alpha", Camera()))).Printers[0];

        Assert.Equal(
            new[] { CameraOverlayCorner.TopLeft, CameraOverlayCorner.TopRight, CameraOverlayCorner.BottomLeft, CameraOverlayCorner.BottomRight },
            printer.OverlayCornerOptions.Select(static option => option.Value));
        string[] names = ["Top left", "Top right", "Bottom left", "Bottom right"];
        Assert.Equal(names, printer.OverlayCornerOptions.Select(static option => option.Text));
    }

    // ---- Editing and saving ----

    [Fact]
    public async Task EditedValuesAreSaved()
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha", Camera()));
        PrinterEditorViewModel printer = vm.Printers[0];

        printer.SelectedOverlayCorner = printer.OverlayCornerOptions[1];
        Row(printer, CameraOverlayItems.State).IsNormal = false;
        Row(printer, CameraOverlayItems.Progress).IsNormal = false;
        Row(printer, CameraOverlayItems.RemainingTime).IsNormal = false;
        Row(printer, CameraOverlayItems.PrinterName).IsNormal = true;
        Row(printer, CameraOverlayItems.NozzleTemperature).IsNormal = true;
        Row(printer, CameraOverlayItems.FinishTime).IsNormal = true;
        Row(printer, CameraOverlayItems.State).IsMini = false;
        Row(printer, CameraOverlayItems.RemainingTime).IsMini = false;
        Assert.True(vm.IsDirty);
        AppSettings saved = await SaveAsync(vm);

        Assert.Equal(Custom, saved.Printers[0].Camera.LegacyOverlay);
        // The window defaults are untouched.
        Assert.Equal(CameraWindowDefaults.Default, saved.Printers[0].Camera.Window);
    }

    [Fact]
    public async Task NormalAndMiniSelectionsAreIndependent()
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha", Camera()));
        PrinterEditorViewModel printer = vm.Printers[0];

        Row(printer, CameraOverlayItems.FileName).IsMini = true;
        Row(printer, CameraOverlayItems.State).IsNormal = false;

        Assert.Equal(CameraOverlayItems.Progress | CameraOverlayItems.RemainingTime, printer.CameraOverlay.Normal);
        Assert.Equal(CameraOverlayOptions.DefaultItems | CameraOverlayItems.FileName, printer.CameraOverlay.Mini);
    }

    [Fact]
    public async Task NoItemsCanBeSaved()
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha", Camera()));
        PrinterEditorViewModel printer = vm.Printers[0];

        foreach (CameraOverlayItemRow row in printer.OverlayRows)
        {
            row.IsNormal = false;
            row.IsMini = false;
        }

        AppSettings saved = await SaveAsync(vm);

        Assert.Equal(CameraOverlayItems.None, saved.Printers[0].Camera.LegacyOverlay.Normal);
        Assert.Equal(CameraOverlayItems.None, saved.Printers[0].Camera.LegacyOverlay.Mini);
    }

    [Fact]
    public async Task UndoneEditIsNotAChange()
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha", Camera(overlay: Custom)));
        PrinterEditorViewModel printer = vm.Printers[0];
        List<string?> changed = [];
        Row(printer, CameraOverlayItems.BedTemperature).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Row(printer, CameraOverlayItems.BedTemperature).IsMini = true;
        Assert.True(vm.IsDirty);
        Assert.Contains(nameof(CameraOverlayItemRow.IsMini), changed);
        Row(printer, CameraOverlayItems.BedTemperature).IsMini = false;
        printer.SelectedOverlayCorner = printer.OverlayCornerOptions[0];
        Assert.True(vm.IsDirty);
        printer.SelectedOverlayCorner = printer.OverlayCornerOptions[1];

        Assert.False(vm.IsDirty);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Empty(_store.Saves);
    }

    [Fact]
    public async Task NullCornerFromTheComboIsIgnored()
    {
        PrinterEditorViewModel printer = (await LoadAsync(Printer("a", "Alpha", Camera(overlay: Custom)))).Printers[0];

        printer.SelectedOverlayCorner = null!;

        Assert.Equal(CameraOverlayCorner.TopRight, printer.SelectedOverlayCorner.Value);
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
    public async Task OverlaySurvivesEverySaveOfTheCamera(string variant, bool enabled, string? url)
    {
        CameraSettings camera = url is null ? CameraSettings.Disabled with { LegacyOverlay = Custom }
            : CameraSettings.TryParseStreamUri(url, out Uri? uri, out _) ? new CameraSettings(enabled, uri) { LegacyOverlay = Custom }
            : CameraSettings.FromInvalidStreamUrl(url) with { LegacyOverlay = Custom };
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha", camera));
        Assert.False(vm.IsDirty, variant);

        vm.Printers[0].Name = "Alpha 2";
        AppSettings saved = await SaveAsync(vm);

        Assert.Equal(camera, saved.Printers[0].Camera);
    }

    [Fact]
    public async Task OverlayOfATurnedOffCameraIsEditedAndKept()
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha", CameraSettings.Disabled));
        PrinterEditorViewModel printer = vm.Printers[0];

        Row(printer, CameraOverlayItems.PrintTime).IsNormal = true;
        AppSettings saved = await SaveAsync(vm);

        Assert.False(saved.Printers[0].Camera.Enabled);
        Assert.Null(saved.Printers[0].Camera.StreamUri);
        Assert.Equal(CameraOverlayOptions.DefaultItems | CameraOverlayItems.PrintTime, saved.Printers[0].Camera.LegacyOverlay.Normal);
    }

    [Fact]
    public async Task OverlayHasNoErrorsAndAFailedSaveNeverOpensItsSubTab()
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha", Camera()));
        PrinterEditorViewModel printer = vm.Printers[0];
        vm.SelectedPrinterTabIndex = SettingsViewModel.OverlayPrinterTab;
        Row(printer, CameraOverlayItems.FileName).IsNormal = true;
        printer.CameraUrl = "http://x";

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Empty(_store.Saves);
        Assert.Equal(SettingsViewModel.CameraPrinterTab, vm.SelectedPrinterTabIndex);
    }

    [Fact]
    public async Task OverlayChangeAloneDoesNotForgetTheRememberedWindow()
    {
        CameraWindowState remembered = new(CameraWindowMode.Mini, new CameraWindowPlacement(1, 2, 700, 400), null);
        _states.Set("a", remembered);
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha", Camera()));
        vm.Printers[0].SelectedOverlayCorner = vm.Printers[0].OverlayCornerOptions[0];

        await SaveAsync(vm);

        Assert.Equal(remembered, _states.Get("a"));
    }

    /// <summary>
    /// Interim until T-092 replaces this sub-tab: the camera window draws the widget layout (T-081), so the old corner stays
    /// in the in-memory settings only and does not change the layout of the camera window.
    /// </summary>
    [Fact]
    public async Task SavedOldCornerDoesNotChangeTheWidgetLayout()
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha", Camera()));
        vm.Printers[0].SelectedOverlayCorner = vm.Printers[0].OverlayCornerOptions[3];

        AppSettings saved = await SaveAsync(vm);

        Assert.Equal(CameraOverlayCorner.BottomRight, saved.Printers[0].Camera.LegacyOverlay.Corner);
        Assert.Same(Core.Cameras.Overlay.CameraOverlayLayout.Default, CameraTarget.From(saved.Printers[0])!.Overlay);
    }

    // ---- Language ----

    [Fact]
    public async Task TextsFollowALanguageSwitch()
    {
        PrinterEditorViewModel printer = (await LoadAsync(Printer("a", "Alpha", Camera(overlay: Custom)))).Printers[0];
        CameraOverlayCornerOption corner = printer.SelectedOverlayCorner;
        CameraOverlayItemRow nozzle = Row(printer, CameraOverlayItems.NozzleTemperature);
        Assert.Equal("Nozzle temperature – Normal window and full screen", nozzle.NormalName);
        Assert.Equal("Nozzle temperature – Mini preview", nozzle.MiniName);
        List<string?> changed = [];
        nozzle.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        using (new UiCultureScope("cs"))
        {
            _viewModel!.RefreshTexts();

            Assert.Equal("Vpravo nahoře", corner.Text);
            Assert.Equal("Teplota trysky", nozzle.Text);
            Assert.Equal("Teplota trysky – Normální okno a celá obrazovka", nozzle.NormalName);
            Assert.Equal("Teplota trysky – Malý náhled", nozzle.MiniName);
            Assert.Equal("Stav tiskárny", Row(printer, CameraOverlayItems.State).Text);
        }

        Assert.Contains(nameof(CameraOverlayItemRow.Text), changed);
        Assert.Contains(nameof(CameraOverlayItemRow.NormalName), changed);
        Assert.Contains(nameof(CameraOverlayItemRow.MiniName), changed);
        // The selection stays.
        Assert.Same(corner, printer.SelectedOverlayCorner);
        Assert.True(nozzle.IsNormal);
        Assert.False(nozzle.IsMini);
    }
}
