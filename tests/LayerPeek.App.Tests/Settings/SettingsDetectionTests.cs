using LayerPeek.App.Resources;
using LayerPeek.App.Settings;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Detection;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Settings.SettingsTestData;

namespace LayerPeek.App.Tests.Settings;

/// <summary>
/// The print failure detection settings of the settings window (T-154, user decisions 2026-11-04): the global section of
/// the Recording tab (switch off by default, alarm repeat, saving snapshots on by default, sampling interval 2–5 s) and the
/// Detection sub-tab of each printer (switch off by default, bed region, every detector with its sensitivity), saved by Save
/// and loaded again; the last camera frame of the region editor read once and written on a successful camera test.
/// </summary>
public sealed class SettingsDetectionTests : IDisposable
{
    private static readonly PasswordPolicy Supported = new(IsSupported: true, MaxBytes: 2560);
    private static readonly CameraSettings Camera = new(enabled: true, new Uri("rtsp://192.168.0.227/live"));

    private readonly FakeEditorStore _store = new();
    private readonly FakeLastCameraFrames _frames = new();
    private SettingsViewModel? _viewModel;

    public void Dispose() => _viewModel?.Dispose();

    private static PrinterDefinition Printer(string id = "a", PrinterImageChoice? image = null) =>
        LinkPrinter(id, "Alpha " + id) with { Camera = Camera, Image = image ?? PrinterImageChoice.Automatic };

    private async Task<SettingsViewModel> LoadAsync(AppSettings settings, ICameraTester? cameraTester = null, IReadOnlyList<PrintFailureDetectorRegistration>? detectors = null)
    {
        _store.Data = new SettingsEditorData(settings, SettingsLoadStatus.Loaded, [], Ids([.. settings.Printers.Select(static p => p.Id)]));
        _viewModel?.Dispose();
        _viewModel = new SettingsViewModel(_store, new FakeConnectionTester(), Supported, static () => Guid.NewGuid().ToString("N"), cameraTester: cameraTester, cameraPreviewRenderer: new FakePreviewRenderer())
        {
            DetectorRegistrations = detectors ?? [DetectionFakes.BedChange, DetectionFakes.Other],
            LastFrames = _frames,
        };
        await _viewModel.LoadAsync();
        return _viewModel;
    }

    private async Task<AppSettings> SaveAsync(SettingsViewModel vm)
    {
        await vm.SaveCommand.ExecuteAsync(null);
        return _store.Saves[^1].Updated;
    }

    // ---- Defaults and saving ----

    [Fact]
    public async Task AfterInstallationEverythingIsOffWithTheDefaults()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [Printer()] });
        PrinterEditorViewModel printer = vm.Printers[0];

        Assert.False(vm.DetectionGloballyEnabled);
        Assert.True(vm.DetectionSaveSnapshots);
        Assert.Equal(PrintFailureAlarmRepeat.OncePerPrint, vm.SelectedAlarmRepeat.Value);
        Assert.Equal(2, vm.DetectionIntervalSeconds);
        Assert.False(printer.DetectionEnabled);
        Assert.Equal(DetectionRegion.Full, printer.DetectionRegion);
        Assert.Equal([DetectionFakes.BedChange.Id, DetectionFakes.Other.Id], printer.Detectors.Select(static d => d.Id));
        Assert.All(printer.Detectors, static d => Assert.True(d.Enabled));
        Assert.All(printer.Detectors, static d => Assert.Equal(DetectorSettings.DefaultSensitivity, d.Sensitivity));
        Assert.False(vm.IsDirty);

        // Saving another change keeps the detection as it was (before T-154 Save dropped it).
        vm.DebugLogging = true;
        AppSettings saved = await SaveAsync(vm);
        Assert.Equal(PrintFailureDetectionSettings.Default, saved.Detection);
        Assert.Equal(PrinterDetectionSettings.Default, saved.Printers[0].Detection);
    }

    [Fact]
    public async Task GlobalAndPrinterSettingsAreSavedAndLoadedAgain()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [Printer()] });
        PrinterEditorViewModel printer = vm.Printers[0];

        vm.DetectionGloballyEnabled = true;
        vm.DetectionSaveSnapshots = false;
        vm.SelectedAlarmRepeat = vm.AlarmRepeatOptions[1];
        vm.DetectionIntervalSeconds = 3.5m;
        printer.DetectionEnabled = true;
        printer.DetectionRegion = new DetectionRegion(0.1, 0.2, 0.5, 0.4);
        printer.Detectors[0].Sensitivity = 80.4;
        printer.Detectors[1].Enabled = false;
        Assert.True(vm.IsDirty);
        AppSettings saved = await SaveAsync(vm);

        Assert.Equal(
            new PrintFailureDetectionSettings { Enabled = true, SaveSnapshots = false, AlarmRepeat = PrintFailureAlarmRepeat.WhilePersisting, SampleInterval = TimeSpan.FromSeconds(3.5) },
            saved.Detection);
        PrinterDetectionSettings detection = saved.Printers[0].Detection;
        Assert.True(detection.Enabled);
        Assert.Equal(new DetectionRegion(0.1, 0.2, 0.5, 0.4), detection.Region);
        Assert.Equal(new DetectorSettings { Sensitivity = 80 }, detection.For(DetectionFakes.BedChange.Id));
        Assert.Equal(new DetectorSettings { Enabled = false }, detection.For(DetectionFakes.Other.Id));

        SettingsViewModel again = await LoadAsync(saved);
        Assert.True(again.DetectionGloballyEnabled);
        Assert.False(again.DetectionSaveSnapshots);
        Assert.Equal(PrintFailureAlarmRepeat.WhilePersisting, again.SelectedAlarmRepeat.Value);
        Assert.Equal(3.5m, again.DetectionIntervalSeconds);
        Assert.True(again.Printers[0].DetectionEnabled);
        Assert.Equal(new DetectionRegion(0.1, 0.2, 0.5, 0.4), again.Printers[0].DetectionRegion);
        Assert.Equal(80, again.Printers[0].Detectors[0].Sensitivity);
        Assert.False(again.Printers[0].Detectors[1].Enabled);
        Assert.False(again.IsDirty);
    }

    [Fact]
    public async Task SettingsOfDetectorsThisVersionDoesNotOfferAreKept()
    {
        PrinterDefinition printer = Printer() with
        {
            Detection = new PrinterDetectionSettings
            {
                Enabled = true,
                Detectors = new Dictionary<string, DetectorSettings> { ["future-kind"] = new() { Sensitivity = 10 }, [DetectionFakes.BedChange.Id] = new() { Sensitivity = 70 } },
            },
        };
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [printer] }, detectors: [DetectionFakes.BedChange]);

        Assert.Single(vm.Printers[0].Detectors);
        vm.Printers[0].Detectors[0].Sensitivity = DetectorSettings.DefaultSensitivity;
        AppSettings saved = await SaveAsync(vm);

        // The default is not written; the unknown one stays as it was.
        Assert.Equal(["future-kind"], saved.Printers[0].Detection.Detectors.Keys);
        Assert.Equal(10, saved.Printers[0].Detection.For("future-kind").Sensitivity);
    }

    [Fact]
    public async Task TheSensitivityIsAWholeNumberWithinTheRange()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [Printer()] });
        DetectorSettingsViewModel detector = vm.Printers[0].Detectors[0];

        detector.Sensitivity = 150;
        Assert.Equal(100, detector.Sensitivity);
        detector.Sensitivity = -3;
        Assert.Equal(0, detector.Sensitivity);
        detector.Sensitivity = 41.5;
        Assert.Equal(42, detector.Sensitivity);
        Assert.Equal("42", detector.SensitivityText);
        detector.Sensitivity = double.NaN;
        Assert.Equal(42, detector.Sensitivity);
        Assert.Equal(Strings.SettingsDetectionSensitivityName.Replace("{0}", Strings.DetectorBedChange, StringComparison.Ordinal), detector.SensitivityName);
        Assert.Equal(Strings.DetectorBedChangeDescription, detector.Description);
        Assert.True(detector.HasLimitation);
        Assert.Equal(Strings.DetectorBedChangeLimitation, detector.Limitation);

        DetectorSettingsViewModel other = vm.Printers[0].Detectors[1];
        Assert.Equal(Strings.DetectorUnknown.Replace("{0}", "other-kind", StringComparison.Ordinal), other.Name);
        Assert.False(other.HasDescription);
        Assert.False(other.HasLimitation);
    }

    // ---- Interval ----

    [Theory]
    [InlineData(2, true)]
    [InlineData(5, true)]
    [InlineData(3.5, true)]
    [InlineData(1.9, false)]
    [InlineData(5.1, false)]
    [InlineData(0, false)]
    [InlineData(60, false)]
    public async Task TheIntervalIsAcceptedOnlyFrom2To5Seconds(double seconds, bool valid)
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [Printer()] });
        vm.SelectedTabIndex = SettingsViewModel.GeneralTab;

        vm.DetectionIntervalSeconds = (decimal)seconds;
        // Something to save also when the interval stays the default.
        vm.DetectionGloballyEnabled = true;

        Assert.Equal(!valid, vm.HasDetectionIntervalError);
        Assert.Equal(valid, vm.ShowsDetectionIntervalHint);
        await vm.SaveCommand.ExecuteAsync(null);
        if (valid)
        {
            Assert.Equal(TimeSpan.FromSeconds(seconds), Assert.Single(_store.Saves).Updated.Detection.SampleInterval);
        }
        else
        {
            Assert.Empty(_store.Saves);
            Assert.Equal(SettingsViewModel.RecordingTab, vm.SelectedTabIndex);
            Assert.Equal(Strings.SettingsDetectionIntervalError.Replace("{0}", "2", StringComparison.Ordinal).Replace("{1}", "5", StringComparison.Ordinal), vm.DetectionIntervalError);
            Assert.Equal(vm.DetectionIntervalError, vm.DetectionIntervalHelpText);
        }
    }

    [Fact]
    public async Task AnEmptyIntervalBlocksSaving()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [Printer()] });

        vm.DetectionIntervalSeconds = null;
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Empty(_store.Saves);
        Assert.True(vm.HasDetectionIntervalError);
    }

    // ---- Region ----

    [Fact]
    public async Task AnUneditedRegionFollowsTheModelAndAnEditedOneIsThePrintersOwn()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [Printer()] });
        PrinterEditorViewModel printer = vm.Printers[0];
        Assert.False(printer.IsCoreOne);
        Assert.Equal(DetectionRegion.Full, printer.DetectionRegion);

        // A Core One (chosen image): its default region and the button for it, still unset.
        printer.ModelOptions.Single(static option => option.Model == PrinterModel.CoreOne).Command.Execute(null);
        Assert.True(printer.IsCoreOne);
        Assert.Equal(DetectionRegion.CoreOneBed, printer.DetectionRegion);
        AppSettings saved = await SaveAsync(vm);
        Assert.Null(saved.Printers[0].Detection.Region);

        // "Whole frame" is an edit: saved as the printer's own, also for a Core One.
        printer.DetectionFullFrameCommand.Execute(null);
        Assert.Equal(DetectionRegion.Full, printer.DetectionRegion);
        saved = await SaveAsync(vm);
        Assert.Equal(DetectionRegion.Full, saved.Printers[0].Detection.Region);

        printer.DetectionCoreOneCommand.Execute(null);
        Assert.Equal(DetectionRegion.CoreOneBed, printer.DetectionRegion);
        Assert.Equal(Strings.SettingsDetectionRegionName.Replace("{0}", "15", StringComparison.Ordinal).Replace("{1}", "55", StringComparison.Ordinal).Replace("{2}", "70", StringComparison.Ordinal).Replace("{3}", "45", StringComparison.Ordinal), printer.DetectionRegionName);
    }

    [Fact]
    public async Task TheDetectedModelCountsLikeTheChosenImage()
    {
        PrinterDefinition coreOne = Printer() with { DetectedModel = new PrinterModelCode(7, 1, 0) };
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [coreOne] });

        Assert.True(vm.Printers[0].IsCoreOne);
        Assert.Equal(DetectionRegion.CoreOneBed, vm.Printers[0].DetectionRegion);
    }

    // ---- Hints ----

    [Fact]
    public async Task TheHintSaysWhyNothingRunsOrWhenItRuns()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [Printer()] });
        PrinterEditorViewModel printer = vm.Printers[0];

        Assert.True(printer.ShowsDetectionGloballyOff);
        Assert.False(printer.ShowsDetectionRunsHint || printer.ShowsDetectionCameraOff);
        vm.DetectionGloballyEnabled = true;
        Assert.True(printer.ShowsDetectionRunsHint);
        Assert.False(printer.ShowsDetectionGloballyOff || printer.ShowsDetectionCameraOff);
        printer.CameraEnabled = false;
        Assert.True(printer.ShowsDetectionCameraOff);
        Assert.False(printer.ShowsDetectionRunsHint || printer.ShowsDetectionGloballyOff);
    }

    // ---- Last frame ----

    [Fact]
    public async Task TheLastFrameIsReadOnceWhenTheSubTabIsShownForThePrinter()
    {
        FakeImage frame = new();
        _frames.Frame = id => id == "b" ? frame : null;
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [Printer("a"), Printer("b")] });
        Assert.Empty(_frames.Loaded);

        vm.SelectedPrinterTabIndex = SettingsViewModel.DetectionPrinterTab;
        await Task.Yield();
        Assert.Equal(["a"], _frames.Loaded);
        Assert.Null(vm.Printers[0].RegionEditorImage);
        Assert.False(vm.Printers[0].HasRegionEditorImage);

        vm.SelectedPrinter = vm.Printers[1];
        await LayerPeek.App.Tests.Notifications.Waiting.ForAsync(() => vm.Printers[1].RegionEditorImage is not null);
        Assert.Same(frame, vm.Printers[1].RegionEditorImage);
        Assert.True(vm.Printers[1].ShowsLastSavedFrame);

        // Back and forth: never read again.
        vm.SelectedPrinter = vm.Printers[0];
        vm.SelectedPrinter = vm.Printers[1];
        vm.SelectedTabIndex = SettingsViewModel.GeneralTab;
        vm.SelectedTabIndex = SettingsViewModel.PrintersTab;
        Assert.Equal(["a", "b"], _frames.Loaded);

        // Closing the window releases it.
        vm.Dispose();
        Assert.True(frame.Disposed);
    }

    [Fact]
    public async Task ASuccessfulCameraTestIsTheEditorsFrameAndTheNewLastFrame()
    {
        CameraImage image = TestCameraImages.Quadrants(64, 36, 0xFFE05A1E, 0xFF2E7D32, 0xFF1565C0, 0xFFF9A825);
        FakeCameraTester tester = new() { Immediate = new CameraTestResult(CameraTestOutcome.Success, 64, 36, Image: image) };
        FakeImage last = new();
        _frames.Frame = _ => last;
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [Printer()] }, tester);
        PrinterEditorViewModel printer = vm.Printers[0];
        vm.SelectedPrinterTabIndex = SettingsViewModel.DetectionPrinterTab;
        await LayerPeek.App.Tests.Notifications.Waiting.ForAsync(() => printer.RegionEditorImage is not null);

        await printer.TestCameraCommand.ExecuteAsync(null);

        Assert.Same(printer.CameraPreview, printer.RegionEditorImage);
        Assert.False(printer.ShowsLastSavedFrame);
        Assert.Equal([("a", 64, 36)], _frames.Saved);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task AFrameThatArrivesAfterTheWindowClosedIsReleased()
    {
        FakeImage frame = new();
        _frames.Frame = _ => frame;
        _frames.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [Printer()] });
        vm.SelectedPrinterTabIndex = SettingsViewModel.DetectionPrinterTab;

        vm.Dispose();
        _frames.Gate.SetResult();
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Cancelled with the window (never shown), or released when it came anyway.
        Assert.True(frame.Disposed || _frames.Loaded.Count == 1);
        Assert.Null(vm.Printers.FirstOrDefault()?.RegionEditorImage);
    }

    [Fact]
    public async Task TheFrameOfARemovedPrinterIsDeletedWhenSaved()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [Printer("a"), Printer("b")] });

        vm.SelectedPrinter = vm.Printers[1];
        vm.RemovePrinterCommand.Execute(null);
        Assert.Empty(_frames.Deleted);
        await SaveAsync(vm);

        Assert.Equal(["b"], _frames.Deleted);
    }

    [Fact]
    public async Task TheFrameOfANewPrinterThatWasNeverSavedIsDeletedWhenTheWindowCloses()
    {
        CameraImage image = TestCameraImages.Quadrants(64, 36, 0xFFE05A1E, 0xFF2E7D32, 0xFF1565C0, 0xFFF9A825);
        FakeCameraTester tester = new() { Immediate = new CameraTestResult(CameraTestOutcome.Success, 64, 36, Image: image) };
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [Printer("a")] }, tester);
        await vm.Printers[0].TestCameraCommand.ExecuteAsync(null);
        vm.AddPrinterCommand.Execute(null);
        PrinterEditorViewModel added = vm.Printers[^1];
        added.CameraEnabled = true;
        added.CameraUrl = "rtsp://192.168.0.228/live";
        await added.TestCameraCommand.ExecuteAsync(null);
        Assert.Equal(["a", added.Id], _frames.Saved.Select(static s => s.PrinterId));

        // Closed without saving: the new printer does not exist, its frame goes; the saved printer keeps its own.
        vm.Dispose();

        Assert.Equal([added.Id], _frames.Deleted);
    }

    [Fact]
    public async Task TheFrameOfANewPrinterIsKeptOnceItIsSaved()
    {
        CameraImage image = TestCameraImages.Quadrants(64, 36, 0xFFE05A1E, 0xFF2E7D32, 0xFF1565C0, 0xFFF9A825);
        FakeCameraTester tester = new() { Immediate = new CameraTestResult(CameraTestOutcome.Success, 64, 36, Image: image) };
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [Printer("a")] }, tester);
        vm.AddPrinterCommand.Execute(null);
        PrinterEditorViewModel added = vm.Printers[^1];
        added.Name = "New";
        added.Address = "192.168.0.50";
        added.CameraEnabled = true;
        added.CameraUrl = "rtsp://192.168.0.228/live";
        await added.TestCameraCommand.ExecuteAsync(null);
        AppSettings saved = await SaveAsync(vm);
        Assert.Contains(saved.Printers, p => p.Id == added.Id);

        vm.Dispose();

        Assert.Empty(_frames.Deleted);
    }

    private sealed class FakePreviewRenderer : ICameraPreviewRenderer
    {
        public Avalonia.Media.IImage Render(CameraImage image) => new FakeImage();
    }

    private sealed class FakeImage : Avalonia.Media.IImage, IDisposable
    {
        public bool Disposed { get; private set; }

        public Avalonia.Size Size => new(64, 36);

        public void Draw(Avalonia.Media.DrawingContext context, Avalonia.Rect sourceRect, Avalonia.Rect destRect)
        {
        }

        public void Dispose() => Disposed = true;
    }
}
