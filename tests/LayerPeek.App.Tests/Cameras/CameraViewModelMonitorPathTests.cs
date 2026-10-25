using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using LayerPeek.App.Cameras;
using LayerPeek.App.Tests.Tray;
using LayerPeek.App.Tray;
using LayerPeek.Core.Cameras;

using static LayerPeek.App.Tests.Cameras.FakeScreenCatalog;

namespace LayerPeek.App.Tests.Cameras;

/// <summary>
/// The monitors read when the camera window opens: a saved monitor found unambiguously on another connector gets its new
/// path saved (user decision 2026-10-23); ambiguous cases and known twins change nothing.
/// </summary>
public sealed class CameraViewModelMonitorPathTests : IAsyncDisposable
{
    private const string MovedPath = @"\\?\DISPLAY#DEL415A#5&224edec0&0&UID160009#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";

    private static readonly Uri StreamUri = new("rtsp://192.168.0.227/live");

    private readonly FakeCameraFeedFactory _factory = new();
    private readonly FakeTimeProvider _time = new();
    private readonly FakePrinterMonitor _monitor = new();
    private readonly MonitoredPrinters _printers = new();
    private readonly ManualDispatcher _dispatcher = new();
    private readonly MemoryCameraWindowStateStore _states = new();
    private readonly RecordingMonitorPathUpdater _updater = new();
    private readonly List<CameraViewModel> _created = [];

    public async ValueTask DisposeAsync()
    {
        foreach (CameraViewModel viewModel in _created)
        {
            viewModel.Dispose();
            await viewModel.StreamCompletion.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        }
    }

    private CameraViewModel Create(CameraMonitorId? monitor, ICameraMonitorPathUpdater? updater = null)
    {
        ReconnectingCameraFeed feed = new(_factory, _time, NullLogger<ReconnectingCameraFeed>.Instance, CameraFeedOptions.Default);
        CameraTarget target = CameraTargets.For("mk4", "MK4", StreamUri, CameraWindowDefaults.Default with { Monitor = monitor });
        CameraViewModel viewModel = new(target, feed, _monitor, _printers, _dispatcher, _states, _time, CameraFeedOptions.Default, NullLogger<CameraViewModel>.Instance, updater ?? _updater);
        _created.Add(viewModel);
        return viewModel;
    }

    [Fact]
    public void MonitorOnAnotherConnectorGetsItsNewPathSaved()
    {
        CameraMonitorId saved = new("DELL U2719D", 2560, 1440, DellPathB, [DellPathA]);
        CameraViewModel viewModel = Create(saved);

        viewModel.OnScreensRead([Screen("Other", 0, 0, 1920, 1080, primary: true, devicePath: "other"), Screen("DELL U2719D", 1920, 0, 2560, 1440, devicePath: MovedPath)]);

        (string printerId, CameraMonitorId expected, CameraMonitorId updated) = Assert.Single(_updater.Requests);
        Assert.Equal("mk4", printerId);
        Assert.Same(saved, expected);
        Assert.Equal(new CameraMonitorId("DELL U2719D", 2560, 1440, MovedPath, [DellPathA]), updated);
    }

    [Fact]
    public void OlderSettingsGetThePathOfTheOnlyMonitorWithTheirName()
    {
        CameraMonitorId saved = new("DELL U2719D", 2560, 1440);
        CameraViewModel viewModel = Create(saved);

        viewModel.OnScreensRead([Screen("DELL U2719D", 0, 0, 2560, 1440, primary: true, devicePath: DellPathA)]);

        Assert.Equal(new CameraMonitorId("DELL U2719D", 2560, 1440, DellPathA), Assert.Single(_updater.Requests).Updated);
    }

    public static TheoryData<string> Unchanged => ["FoundByPath", "KnownTwin", "TwoCandidates", "Missing", "NoMonitor", "OldTwoSameName"];

    [Theory]
    [MemberData(nameof(Unchanged))]
    public void NothingIsSavedWithoutAnUnambiguousNewPath(string situation)
    {
        CameraScreen dellA = Screen("DELL U2719D", 0, 0, 2560, 1440, primary: true, devicePath: DellPathA);
        CameraScreen dellB = Screen("DELL U2719D", 2560, 0, 2560, 1440, devicePath: DellPathB);
        CameraScreen moved = dellB with { DevicePath = MovedPath };
        (CameraMonitorId? saved, CameraScreen[] screens) = situation switch
        {
            "FoundByPath" => (new CameraMonitorId("DELL U2719D", 2560, 1440, DellPathB, [DellPathA]), new[] { dellA, dellB }),
            "KnownTwin" => (new CameraMonitorId("DELL U2719D", 2560, 1440, DellPathB, [DellPathA]), [dellA]),
            "TwoCandidates" => (new CameraMonitorId("DELL U2719D", 2560, 1440, DellPathB), [dellA, moved]),
            "Missing" => (new CameraMonitorId("BenQ PD2700U", 3840, 2160, "benq"), [dellA]),
            "NoMonitor" => ((CameraMonitorId?)null, [dellA]),
            "OldTwoSameName" => (new CameraMonitorId("DELL U2719D", 2560, 1440), [dellA, dellB]),
            _ => throw new ArgumentOutOfRangeException(nameof(situation)),
        };
        CameraViewModel viewModel = Create(saved);

        viewModel.OnScreensRead(screens);
        viewModel.OnScreensRead([]);

        Assert.Empty(_updater.Requests);
    }

    [Fact]
    public void WithoutAnUpdaterNothingHappens()
    {
        ReconnectingCameraFeed feed = new(_factory, _time, NullLogger<ReconnectingCameraFeed>.Instance, CameraFeedOptions.Default);
        CameraTarget target = CameraTargets.For("mk4", "MK4", StreamUri, CameraWindowDefaults.Default with { Monitor = new CameraMonitorId("DELL U2719D", 2560, 1440, DellPathB) });
        CameraViewModel viewModel = new(target, feed, _monitor, _printers, _dispatcher, _states, _time, CameraFeedOptions.Default, NullLogger<CameraViewModel>.Instance);
        _created.Add(viewModel);

        viewModel.OnScreensRead([Screen("DELL U2719D", 0, 0, 2560, 1440, primary: true, devicePath: MovedPath)]);

        Assert.Empty(_updater.Requests);
    }
}
