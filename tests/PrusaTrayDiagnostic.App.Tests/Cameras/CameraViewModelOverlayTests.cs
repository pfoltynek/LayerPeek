using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using PrusaTrayDiagnostic.App.Cameras;
using PrusaTrayDiagnostic.App.Tests.Tray;
using PrusaTrayDiagnostic.App.Tray;
using PrusaTrayDiagnostic.Core.Cameras;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Settings;

using static PrusaTrayDiagnostic.App.Tests.Tray.TestPrinters;

namespace PrusaTrayDiagnostic.App.Tests.Cameras;

/// <summary>The print overlay of the camera window (T-036): items per mode, corner, applying saved settings.</summary>
public sealed class CameraViewModelOverlayTests : IAsyncDisposable
{
    private static readonly Uri StreamUri = new("rtsp://192.168.0.227/live");

    private static readonly CameraOverlayOptions NameAndTemperatures = new()
    {
        Normal = CameraOverlayItems.PrinterName | CameraOverlayItems.NozzleTemperature,
        Mini = CameraOverlayItems.FileName,
        Corner = CameraOverlayCorner.TopRight,
    };

    private readonly FakeCameraFeedFactory _factory = new();
    private readonly FakeTimeProvider _time = new();
    private readonly FakePrinterMonitor _monitor = new();
    private readonly MonitoredPrinters _printers = new();
    private readonly ManualDispatcher _dispatcher = new();
    private readonly MemoryCameraWindowStateStore _states = new();
    private readonly List<CameraViewModel> _created = [];
    private readonly CultureScope _culture = new("en-US");

    public CameraViewModelOverlayTests()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.Utc);
    }

    public async ValueTask DisposeAsync()
    {
        _culture.Dispose();
        foreach (CameraViewModel viewModel in _created)
        {
            viewModel.Dispose();
            await viewModel.StreamCompletion.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        }
    }

    private CameraViewModel Create(CameraOverlayOptions overlay, CameraWindowDefaults? window = null)
    {
        ReconnectingCameraFeed feed = new(_factory, _time, NullLogger<ReconnectingCameraFeed>.Instance, CameraFeedOptions.Default);
        CameraTarget target = new("mk4", "MK4", StreamUri) { Overlay = overlay, Window = window ?? CameraWindowDefaults.Default };
        CameraViewModel viewModel = new(target, feed, _monitor, _printers, _dispatcher, _states, _time, CameraFeedOptions.Default, NullLogger<CameraViewModel>.Instance);
        _created.Add(viewModel);
        return viewModel;
    }

    private static PrinterDefinition Printer(CameraOverlayOptions overlay, string name = "MK4") =>
        Definition("mk4", name) with { Camera = new CameraSettings(enabled: true, StreamUri) { Overlay = overlay } };

    private void Save(CameraOverlayOptions overlay, string name = "MK4")
    {
        ((IMonitoringObserver)_printers).ApplySettings(new AppSettings { Printers = [Printer(overlay, name)] });
        _dispatcher.RunAll();
    }

    private static PrinterSnapshot Printing(double nozzle = 214.6) => new()
    {
        PrinterId = "mk4",
        State = PrinterState.Printing,
        Timestamp = Now,
        Job = new PrintJob { FileName = "benchy.bgcode", ProgressPercent = 42.7, Elapsed = TimeSpan.FromMinutes(130), Remaining = TimeSpan.FromMinutes(65) },
        Telemetry = new Telemetry { Nozzle = new Temperature(nozzle, 215), Bed = new Temperature(59.8, 60) },
    };

    private void Raise(PrinterSnapshot snapshot)
    {
        _monitor.Raise(snapshot);
        _dispatcher.RunAll();
    }

    private static string[] Texts(CameraViewModel viewModel) => [.. viewModel.OverlayLines.Select(static line => line.Text)];

    [Fact]
    public void TargetCarriesTheOverlayOfThePrinter()
    {
        Assert.Equal(NameAndTemperatures, CameraTarget.From(Printer(NameAndTemperatures))!.Overlay);
        Assert.Same(CameraOverlayOptions.Default, new CameraTarget("a", "A", StreamUri).Overlay);
    }

    [Fact]
    public void DefaultOverlayIsTodaysOverlayInTheBottomLeftCorner()
    {
        CameraViewModel viewModel = Create(CameraOverlayOptions.Default);
        Raise(Printing());

        Assert.Equal(["Printing – 42%", "1 h 5 min remaining"], Texts(viewModel));
        Assert.Equal(CameraOverlayCorner.BottomLeft, viewModel.OverlayCorner);
        Assert.False(viewModel.IsOverlayTop);
        Assert.False(viewModel.IsOverlayRight);
    }

    [Fact]
    public void NormalWindowAndFullScreenShowTheNormalItemsAndTheMiniPreviewItsOwn()
    {
        CameraViewModel viewModel = Create(NameAndTemperatures);
        Raise(Printing());

        Assert.Equal(["MK4", "Nozzle: 215 / 215 °C"], Texts(viewModel));

        viewModel.ToggleFullScreenCommand.Execute(null);
        Assert.True(viewModel.IsFullScreen);
        Assert.Equal(["MK4", "Nozzle: 215 / 215 °C"], Texts(viewModel));

        viewModel.ShowMiniCommand.Execute(null);
        Assert.True(viewModel.IsMini);
        Assert.Equal(["benchy.bgcode"], Texts(viewModel));
        Assert.Equal(CameraOverlayItems.FileName, viewModel.OverlayItems);

        viewModel.ShowNormalCommand.Execute(null);
        Assert.Equal(["MK4", "Nozzle: 215 / 215 °C"], Texts(viewModel));
    }

    [Fact]
    public void WindowOpeningInTheMiniPreviewStartsWithTheMiniItems()
    {
        CameraViewModel viewModel = Create(NameAndTemperatures, CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Mini });
        Raise(Printing());

        Assert.True(viewModel.IsMini);
        Assert.Equal(["benchy.bgcode"], Texts(viewModel));
    }

    [Fact]
    public void SwitchingTheModeUsesTheLatestSnapshotWithoutWaitingForAPoll()
    {
        CameraViewModel viewModel = Create(NameAndTemperatures);
        Raise(Printing());
        int posts = _dispatcher.PostCount;

        viewModel.ShowMiniCommand.Execute(null);

        Assert.Equal(["benchy.bgcode"], Texts(viewModel));
        Assert.Equal(posts, _dispatcher.PostCount);
    }

    [Fact]
    public void NoItemsHidesTheOverlay()
    {
        CameraViewModel viewModel = Create(new CameraOverlayOptions { Normal = CameraOverlayItems.None, Mini = CameraOverlayItems.State });
        Raise(Printing());

        Assert.False(viewModel.IsPrintOverlayVisible);
        Assert.Empty(viewModel.OverlayLines);

        viewModel.ShowMiniCommand.Execute(null);
        Assert.True(viewModel.IsPrintOverlayVisible);
        Assert.Equal(["Printing"], Texts(viewModel));
    }

    [Fact]
    public void PrinterNameIsShownBeforeTheFirstPoll()
    {
        CameraViewModel viewModel = Create(NameAndTemperatures);

        Assert.True(viewModel.IsPrintOverlayVisible);
        Assert.Equal(["MK4"], Texts(viewModel));
    }

    [Fact]
    public void UnselectedChangesPostNothing()
    {
        CameraViewModel viewModel = Create(CameraOverlayOptions.Default);
        Raise(Printing());
        int posts = _dispatcher.PostCount;
        int changes = 0;
        viewModel.PropertyChanged += (_, _) => changes++;

        _monitor.Raise(Printing(nozzle: 190) with { Timestamp = Now.AddSeconds(5) });

        Assert.Equal(posts, _dispatcher.PostCount);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void SelectedTemperatureChangeUpdatesTheOverlay()
    {
        CameraViewModel viewModel = Create(NameAndTemperatures);
        Raise(Printing());

        Raise(Printing(nozzle: 190.2));

        Assert.Equal(["MK4", "Nozzle: 190 / 215 °C"], Texts(viewModel));
    }

    [Fact]
    public void SavedItemsApplyToTheOpenWindowRightAway()
    {
        CameraViewModel viewModel = Create(CameraOverlayOptions.Default);
        Raise(Printing());
        List<string?> changed = [];
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Save(NameAndTemperatures);

        Assert.Equal(["MK4", "Nozzle: 215 / 215 °C"], Texts(viewModel));
        Assert.Contains(nameof(CameraViewModel.OverlayLines), changed);
        // The window stays as it is: same mode, stream untouched, nothing to place again.
        Assert.True(viewModel.IsNormal);
        Assert.Empty(_factory.Uris);
        Assert.DoesNotContain(nameof(CameraViewModel.Mode), changed);
    }

    [Fact]
    public void SavedMiniItemsApplyToAnOpenMiniPreview()
    {
        CameraViewModel viewModel = Create(CameraOverlayOptions.Default);
        viewModel.ShowMiniCommand.Execute(null);
        Raise(Printing());

        Save(NameAndTemperatures);

        Assert.Equal(["benchy.bgcode"], Texts(viewModel));
    }

    [Theory]
    [InlineData(CameraOverlayCorner.TopLeft, true, false)]
    [InlineData(CameraOverlayCorner.TopRight, true, true)]
    [InlineData(CameraOverlayCorner.BottomLeft, false, false)]
    [InlineData(CameraOverlayCorner.BottomRight, false, true)]
    public void SavedCornerApplies(CameraOverlayCorner corner, bool top, bool right)
    {
        CameraViewModel viewModel = Create(CameraOverlayOptions.Default with { Corner = CameraOverlayCorner.BottomLeft });
        List<string?> changed = [];
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Save(CameraOverlayOptions.Default with { Corner = corner });

        Assert.Equal(corner, viewModel.OverlayCorner);
        Assert.Equal(top, viewModel.IsOverlayTop);
        Assert.Equal(right, viewModel.IsOverlayRight);
        Assert.Equal(corner != CameraOverlayCorner.BottomLeft, changed.Contains(nameof(CameraViewModel.OverlayCorner)));
    }

    [Fact]
    public void UnchangedOverlayAfterSavingChangesNothing()
    {
        CameraViewModel viewModel = Create(NameAndTemperatures);
        Raise(Printing());
        List<string?> changed = [];
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Save(NameAndTemperatures);

        Assert.Empty(changed);
    }

    [Fact]
    public void RenamedPrinterUpdatesTheNameLine()
    {
        CameraViewModel viewModel = Create(NameAndTemperatures);
        Raise(Printing());

        Save(NameAndTemperatures, name: "Workshop MK4");

        Assert.Equal(["Workshop MK4", "Nozzle: 215 / 215 °C"], Texts(viewModel));
        Assert.Equal("Workshop MK4 – Camera", viewModel.Title);
    }

    [Fact]
    public void LanguageSwitchFormatsTheOverlayAgain()
    {
        CameraViewModel viewModel = Create(CameraOverlayOptions.Default with { Normal = CameraOverlayOptions.AllItems });
        Raise(Printing());
        List<string?> changed = [];
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        using (new UiCultureScope("cs"))
        {
            viewModel.RefreshTexts();

            Assert.StartsWith("Tiskne – 42", Texts(viewModel)[1], StringComparison.Ordinal);
            Assert.Equal("Tryska: 215 / 215 °C", Texts(viewModel)[2]);
            Assert.Equal("Doba tisku: 2 h 10 min", Texts(viewModel)[6]);
        }

        Assert.Contains(nameof(CameraViewModel.OverlayLines), changed);
    }

    [Fact]
    public void ModeAndSettingsChangesAfterDisposeDoNothing()
    {
        CameraViewModel viewModel = Create(CameraOverlayOptions.Default);
        Raise(Printing());
        IReadOnlyList<CameraOverlayLine> lines = viewModel.OverlayLines;
        viewModel.Dispose();

        Save(NameAndTemperatures);
        _monitor.Raise(Printing(nozzle: 100));
        _dispatcher.RunAll();

        Assert.Same(lines, viewModel.OverlayLines);
    }
}
