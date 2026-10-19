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

/// <summary>Remembered state versus the window defaults when a camera window opens and closes (T-035).</summary>
public sealed class CameraViewModelWindowTests : IAsyncDisposable
{
    private static readonly Uri StreamUri = new("rtsp://192.168.0.227/live");

    private static readonly CameraWindowState Remembered = new(
        CameraWindowMode.Mini,
        new CameraWindowPlacement(100, 80, 700, 400),
        new CameraWindowPlacement(300, 200, 400, 225));

    private readonly FakeCameraFeedFactory _factory = new();
    private readonly FakeTimeProvider _time = new();
    private readonly FakePrinterMonitor _monitor = new();
    private readonly MonitoredPrinters _printers = new();
    private readonly ManualDispatcher _dispatcher = new();
    private readonly MemoryCameraWindowStateStore _states = new();
    private readonly List<CameraViewModel> _created = [];

    public async ValueTask DisposeAsync()
    {
        foreach (CameraViewModel viewModel in _created)
        {
            viewModel.Dispose();
            await viewModel.StreamCompletion.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        }
    }

    private CameraViewModel Create(CameraWindowDefaults defaults, string id = "mk4")
    {
        ReconnectingCameraFeed feed = new(_factory, _time, NullLogger<ReconnectingCameraFeed>.Instance, CameraFeedOptions.Default);
        CameraTarget target = new(id, "MK4", StreamUri) { Window = defaults };
        CameraViewModel viewModel = new(target, feed, _monitor, _printers, _dispatcher, _states, _time, CameraFeedOptions.Default, NullLogger<CameraViewModel>.Instance);
        _created.Add(viewModel);
        return viewModel;
    }

    private static PrinterDefinition Printer(CameraWindowDefaults defaults, string id = "mk4") =>
        Definition(id, "MK4") with { Camera = new CameraSettings(enabled: true, StreamUri) { Window = defaults } };

    [Fact]
    public void TargetCarriesTheWindowDefaultsOfThePrinter()
    {
        CameraWindowDefaults defaults = CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Mini };

        Assert.Equal(defaults, CameraTarget.From(Printer(defaults))!.Window);
        Assert.Same(CameraWindowDefaults.Default, new CameraTarget("a", "A", StreamUri).Window);
    }

    [Fact]
    public void RememberOnAndNothingRememberedOpensWithTheDefaults()
    {
        CameraViewModel viewModel = Create(CameraWindowDefaults.Default);

        Assert.Equal(CameraWindowMode.Normal, viewModel.Mode);
        Assert.Null(viewModel.NormalPlacement);
        Assert.Null(viewModel.MiniPlacement);
        Assert.Same(CameraWindowDefaults.Default, viewModel.Defaults);
    }

    [Fact]
    public void RememberOnUsesTheRememberedModeAndPlacements()
    {
        _states.Set("mk4", Remembered);

        CameraViewModel viewModel = Create(CameraWindowDefaults.Default);

        Assert.Equal(CameraWindowMode.Mini, viewModel.Mode);
        Assert.Equal(Remembered.Normal, viewModel.NormalPlacement);
        Assert.Equal(Remembered.Mini, viewModel.MiniPlacement);
    }

    [Fact]
    public void RememberOffIgnoresThePlacementsButSameAsLastTimeKeepsTheMode()
    {
        // T-035 Q2: the remembered mode, at the default position and size.
        _states.Set("mk4", Remembered);

        CameraViewModel viewModel = Create(CameraWindowDefaults.Default with { RememberLastPlacement = false });

        Assert.Equal(CameraWindowMode.Mini, viewModel.Mode);
        Assert.Null(viewModel.NormalPlacement);
        Assert.Null(viewModel.MiniPlacement);
    }

    [Theory]
    [InlineData(CameraWindowStartMode.Last, CameraWindowMode.Mini)]
    [InlineData(CameraWindowStartMode.Normal, CameraWindowMode.Normal)]
    [InlineData(CameraWindowStartMode.Mini, CameraWindowMode.Mini)]
    [InlineData(CameraWindowStartMode.FullScreen, CameraWindowMode.FullScreen)]
    public void StartModeChoosesTheMode(CameraWindowStartMode startMode, CameraWindowMode expected)
    {
        _states.Set("mk4", Remembered);

        Assert.Equal(expected, Create(CameraWindowDefaults.Default with { StartMode = startMode }).Mode);
    }

    [Fact]
    public void StateIsWrittenBackWhenTheWindowCloses()
    {
        CameraViewModel viewModel = Create(CameraWindowDefaults.Default);
        viewModel.RememberPlacement(CameraWindowMode.Normal, new CameraWindowPlacement(5, 6, 700, 400));

        viewModel.Dispose();

        Assert.Equal(new CameraWindowState(CameraWindowMode.Normal, new CameraWindowPlacement(5, 6, 700, 400), null), _states.Get("mk4"));
    }

    [Fact]
    public void RememberOffKeepsTheStoredPlacementsAndRemembersOnlyTheMode()
    {
        // User decision 2026-10-19: with "remember" off, closing must not overwrite the placements remembered earlier.
        _states.Set("mk4", Remembered);
        CameraViewModel viewModel = Create(CameraWindowDefaults.Default with { RememberLastPlacement = false });
        Assert.True(viewModel.IsMini);

        // The user moved the window and switched to the normal window (without showing the mini preview again).
        viewModel.RememberPlacement(CameraWindowMode.Mini, new CameraWindowPlacement(5, 6, 330, 190));
        viewModel.ShowNormalCommand.Execute(null);
        viewModel.RememberPlacement(CameraWindowMode.Normal, new CameraWindowPlacement(7, 8, 800, 450));
        viewModel.Dispose();

        Assert.Equal(Remembered with { Mode = CameraWindowMode.Normal }, _states.Get("mk4"));
    }

    [Fact]
    public void RememberOffWithNothingStoredRemembersOnlyTheMode()
    {
        CameraViewModel viewModel = Create(CameraWindowDefaults.Default with { RememberLastPlacement = false, StartMode = CameraWindowStartMode.Mini });
        viewModel.RememberPlacement(CameraWindowMode.Mini, new CameraWindowPlacement(5, 6, 330, 190));

        viewModel.Dispose();

        Assert.Equal(new CameraWindowState(CameraWindowMode.Mini, null, null), _states.Get("mk4"));
    }

    [Fact]
    public void StateRemovedWhileTheWindowIsOpenIsNotWrittenBack()
    {
        // The settings changed the defaults: they must apply the next time the window opens (T-035).
        _states.Set("mk4", Remembered);
        _states.Set("xl", Remembered);
        CameraViewModel viewModel = Create(CameraWindowDefaults.Default);
        CameraViewModel other = Create(CameraWindowDefaults.Default, id: "xl");
        viewModel.RememberPlacement(CameraWindowMode.Normal, new CameraWindowPlacement(5, 6, 700, 400));

        _states.Remove("mk4");
        viewModel.Dispose();
        other.Dispose();

        Assert.Same(CameraWindowState.Default, _states.Get("mk4"));
        Assert.Equal(Remembered, _states.Get("xl"));
    }

    [Fact]
    public void DefaultsFollowSavedSettingsWithoutChangingTheOpenWindow()
    {
        _states.Set("mk4", Remembered);
        CameraViewModel viewModel = Create(CameraWindowDefaults.Default);
        CameraWindowDefaults changed = CameraWindowDefaults.Default with { Mini = new CameraWindowLayout(new CameraWindowSize(400, 300), CameraWindowCorner.TopLeft) };

        ((IMonitoringObserver)_printers).ApplySettings(new AppSettings { Printers = [Printer(changed)] });
        _dispatcher.RunAll();

        Assert.Equal(changed, viewModel.Defaults);
        Assert.Equal(CameraWindowMode.Mini, viewModel.Mode);
        Assert.Equal(Remembered.Mini, viewModel.MiniPlacement);
    }
}
