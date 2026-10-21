using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using PrusaTrayDiagnostic.App.Cameras;
using PrusaTrayDiagnostic.App.Tests.Tray;
using PrusaTrayDiagnostic.App.Tray;
using PrusaTrayDiagnostic.Core.Cameras;

namespace PrusaTrayDiagnostic.App.Tests.Cameras;

/// <summary>"Apply" in the settings places the open camera window again by the saved settings (T-040).</summary>
public sealed class CameraViewModelApplyTests : IAsyncDisposable
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
    private readonly CultureScope _culture = new("en-US");

    public async ValueTask DisposeAsync()
    {
        _culture.Dispose();
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

    public static TheoryData<CameraWindowStartMode, CameraWindowMode, CameraWindowMode> Modes => new()
    {
        // Start mode, current mode, mode after Apply ("same as last time" keeps the current one).
        { CameraWindowStartMode.Last, CameraWindowMode.Normal, CameraWindowMode.Normal },
        { CameraWindowStartMode.Last, CameraWindowMode.Mini, CameraWindowMode.Mini },
        { CameraWindowStartMode.Last, CameraWindowMode.FullScreen, CameraWindowMode.FullScreen },
        { CameraWindowStartMode.Normal, CameraWindowMode.Mini, CameraWindowMode.Normal },
        { CameraWindowStartMode.Normal, CameraWindowMode.FullScreen, CameraWindowMode.Normal },
        { CameraWindowStartMode.Mini, CameraWindowMode.Normal, CameraWindowMode.Mini },
        { CameraWindowStartMode.Mini, CameraWindowMode.FullScreen, CameraWindowMode.Mini },
        { CameraWindowStartMode.FullScreen, CameraWindowMode.Normal, CameraWindowMode.FullScreen },
        { CameraWindowStartMode.FullScreen, CameraWindowMode.Mini, CameraWindowMode.FullScreen },
    };

    [Theory]
    [MemberData(nameof(Modes))]
    public void ApplyTakesTheModeOfTheStartMode(CameraWindowStartMode startMode, CameraWindowMode current, CameraWindowMode expected)
    {
        CameraViewModel viewModel = Create(CameraWindowDefaults.Default with { StartMode = current switch
        {
            CameraWindowMode.Mini => CameraWindowStartMode.Mini,
            CameraWindowMode.FullScreen => CameraWindowStartMode.FullScreen,
            _ => CameraWindowStartMode.Normal,
        } });
        Assert.Equal(current, viewModel.Mode);

        viewModel.ApplyWindowSettings(CameraWindowDefaults.Default with { StartMode = startMode });

        Assert.Equal(expected, viewModel.Mode);
    }

    [Fact]
    public void ApplyTakesTheDefaultsAndDropsThePlacementsOfThisWindow()
    {
        _states.Set("mk4", Remembered);
        CameraViewModel viewModel = Create(CameraWindowDefaults.Default);
        Assert.NotNull(viewModel.NormalPlacement);
        Assert.NotNull(viewModel.MiniPlacement);
        CameraWindowDefaults applied = CameraWindowDefaults.Default with
        {
            Normal = new CameraWindowLayout(new CameraWindowSize(640, 360), CameraWindowCorner.TopLeft),
            TopmostMini = false,
        };

        viewModel.ApplyWindowSettings(applied);

        Assert.Same(applied, viewModel.Defaults);
        Assert.Null(viewModel.NormalPlacement);
        Assert.Null(viewModel.MiniPlacement);
        Assert.False(viewModel.IsTopmost);
        Assert.Equal("Preview", viewModel.MiniToolTip);
    }

    [Fact]
    public void ApplyAsksTheWindowToPlaceItselfAfterTheModeChanged()
    {
        CameraViewModel viewModel = Create(CameraWindowDefaults.Default);
        List<string> events = [];
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CameraViewModel.Mode))
            {
                events.Add($"Mode {viewModel.Mode} resetting={viewModel.IsResettingPlacement}");
            }
        };
        viewModel.PlacementResetRequested += (_, _) => events.Add($"Reset {viewModel.Mode} resetting={viewModel.IsResettingPlacement}");

        viewModel.ApplyWindowSettings(CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Mini });

        Assert.Equal(["Mode Mini resetting=True", "Reset Mini resetting=False"], events);
        Assert.False(viewModel.IsResettingPlacement);
    }

    [Fact]
    public void ApplyWithoutAModeChangeStillAsksTheWindowToPlaceItself()
    {
        CameraViewModel viewModel = Create(CameraWindowDefaults.Default);
        int resets = 0;
        viewModel.PlacementResetRequested += (_, _) => resets++;

        viewModel.ApplyWindowSettings(CameraWindowDefaults.Default);

        Assert.Equal(1, resets);
        Assert.Equal(CameraWindowMode.Normal, viewModel.Mode);
    }

    [Fact]
    public void ApplyRaisesTopmostChangedWhenTheResultChanges()
    {
        CameraViewModel viewModel = Create(CameraWindowDefaults.Default);
        List<string?> changed = [];
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        viewModel.ApplyWindowSettings(CameraWindowDefaults.Default with { TopmostNormal = true });

        Assert.True(viewModel.IsTopmost);
        Assert.Contains(nameof(CameraViewModel.IsTopmost), changed);
    }

    [Fact]
    public void WindowIsRememberedAgainAfterApply()
    {
        // The settings forgot the state (a changed default or "Factory settings") and then applied it to the open window.
        _states.Set("mk4", Remembered);
        CameraViewModel viewModel = Create(CameraWindowDefaults.Default);
        _states.Remove("mk4");

        viewModel.ApplyWindowSettings(CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Normal });
        viewModel.RememberPlacement(CameraWindowMode.Normal, new CameraWindowPlacement(5, 6, 960, 540));
        viewModel.Dispose();

        Assert.Equal(new CameraWindowState(CameraWindowMode.Normal, new CameraWindowPlacement(5, 6, 960, 540), null), _states.Get("mk4"));
    }

    [Fact]
    public void FullScreenAppliedFromTheMiniPreviewReturnsToIt()
    {
        CameraViewModel viewModel = Create(CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Mini });

        viewModel.ApplyWindowSettings(CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.FullScreen });
        viewModel.EscapeCommand.Execute(null);

        Assert.Equal(CameraWindowMode.Mini, viewModel.Mode);
    }

    [Fact]
    public void FullScreenIsAvailableInTheMiniPreviewAndReturnsToIt()
    {
        // T-040: the mini preview has its own full screen button; F11 leaves full screen back to it.
        CameraViewModel viewModel = Create(CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Mini });
        Assert.True(viewModel.ShowFullScreenCommand.CanExecute(null));

        viewModel.ShowFullScreenCommand.Execute(null);
        Assert.True(viewModel.IsFullScreen);

        viewModel.ToggleFullScreenCommand.Execute(null);
        Assert.True(viewModel.IsMini);
    }

    [Fact]
    public void DisposedViewModelIgnoresApply()
    {
        CameraViewModel viewModel = Create(CameraWindowDefaults.Default);
        int resets = 0;
        viewModel.PlacementResetRequested += (_, _) => resets++;
        viewModel.Dispose();

        viewModel.ApplyWindowSettings(CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Mini });

        Assert.Equal(0, resets);
        Assert.Same(CameraWindowDefaults.Default, viewModel.Defaults);
    }

    [Fact]
    public void ApplyRejectsNull()
    {
        CameraViewModel viewModel = Create(CameraWindowDefaults.Default);

        Assert.Throws<ArgumentNullException>(() => viewModel.ApplyWindowSettings(null!));
    }
}
