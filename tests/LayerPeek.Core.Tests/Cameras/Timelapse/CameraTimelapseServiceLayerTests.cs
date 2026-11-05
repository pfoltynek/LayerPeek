using Microsoft.Extensions.Time.Testing;

using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Recording;
using LayerPeek.Core.Cameras.Timelapse;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Tests.Cameras.Recording;
using LayerPeek.Core.Tests.Monitoring;
using LayerPeek.Core.Tests.Printers;

namespace LayerPeek.Core.Tests.Cameras.Timelapse;

/// <summary>
/// T-159: the recording service with the layer trigger: a frame at every estimated layer change, from the snapshots of a
/// fake monitor (the recorded Core One print and synthetic layers), with fake timelapses and with the real timelapse
/// recorder over a fake camera stream.
/// </summary>
public sealed class CameraTimelapseServiceLayerTests : IAsyncDisposable
{
    private const string Job = "coreone_0.4n_0.2mm_PLA_COREONE_2h50m.bgcode";
    private const long JobId = 354;
    private static readonly DateTimeOffset Start = new(2026, 11, 4, 18, 12, 29, TimeSpan.Zero);
    private static readonly Uri StreamUri = new("rtsp://192.168.0.229/live");

    private static readonly CameraRecordingSettings LayerSettings = new() { TimelapseTrigger = CameraTimelapseTriggerKind.Layer };

    private readonly string _root = TestDirectories.NewDirectoryPath();
    private readonly SnapshotMonitor _monitor = new();
    private readonly FakeCameraRecorder _recorder = new();
    private readonly FakeCameraTimelapseRecorder _timelapses = new();
    private readonly FakeRecordingStorage _storage = new();
    private readonly CountingTimeProvider _time = new(new FakeTimeProvider(Start));
    private readonly ListLogger<CameraRecordingService> _logger = new();
    private readonly List<IAsyncDisposable> _disposables = [];

    private FakeTimeProvider Clock => _time.Fake;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        foreach (IAsyncDisposable disposable in Enumerable.Reverse(_disposables))
        {
            await disposable.DisposeAsync().AsTask().WaitAsync(TestTimeouts.Wait, Ct);
        }

        TestDirectories.Delete(_root);
    }

    private CameraRecordingService CreateService(ICameraTimelapseRecorder? timelapses = null)
    {
        CameraRecordingService service = new(_monitor, _recorder, _storage, _time, _logger, new CameraRecordingServiceOptions { DefaultRoot = _root }, timelapses ?? _timelapses);
        _disposables.Add(service);
        return service;
    }

    private static PrinterDefinition Printer() => new()
    {
        Id = "core-one",
        Name = "Core One",
        ConnectionType = ConnectionType.PrusaLink,
        Address = "192.168.0.228",
        Camera = new CameraSettings(true, StreamUri) { TimelapsePrints = true },
    };

    private void Raise(PrinterState state, double? z, long jobId = JobId)
    {
        _monitor.Raise(new PrinterSnapshot
        {
            PrinterId = "core-one",
            State = state,
            Timestamp = Clock.GetUtcNow(),
            Job = new PrintJob { Id = jobId, FileName = Job, ProgressPercent = 10 },
            Telemetry = z is null ? null : new Telemetry { AxisZ = z },
        });
        Clock.Advance(TimeSpan.FromSeconds(5));
    }

    /// <summary>Four polls (5 s each) at the height of <paramref name="layer"/> (0.2 mm layers).</summary>
    private void Layer(int layer, long jobId = JobId)
    {
        for (int poll = 0; poll < 4; poll++)
        {
            Raise(PrinterState.Printing, Math.Round(layer * 0.2, 1), jobId);
        }
    }

    /// <summary>
    /// Waits for timelapse <paramref name="number"/> and its first frame request (the trigger exists from then on: both are
    /// under the service's lock) and lets its first key frame arrive.
    /// </summary>
    private async Task<FakeTimelapse> TimelapseAsync(CameraRecordingService service, int number)
    {
        await Eventually.TrueAsync(() => _timelapses.Started.Length >= number, $"timelapse {number} started");
        FakeTimelapse timelapse = _timelapses.Started[number - 1];
        await Eventually.TrueAsync(() => timelapse.Requests == 1, "first frame asked for");
        timelapse.BeginFile();
        await Eventually.TrueAsync(() => service.GetStatus("core-one").Timelapse.Activity == CameraRecordingActivity.Recording, "recording");
        return timelapse;
    }

    /// <summary>The print starts (one snapshot at the first layer); returns its timelapse once the trigger exists.</summary>
    private async Task<FakeTimelapse> StartAsync(CameraRecordingService service)
    {
        Raise(PrinterState.Printing, 0.2);
        return await TimelapseAsync(service, 1);
    }

    [Fact]
    public async Task TheLayerTriggerTakesAFrameAtEveryLayerOfTheRealPrintAndOneOfTheFinishedPart()
    {
        CameraRecordingService service = CreateService();
        service.Configure([Printer()], LayerSettings);
        ZRecording recording = ZRecording.Load("coreone-job354-0.2mm.csv");
        (int Second, PrinterState State, double Z)[] polls = [.. recording.Poll(5, 0)];
        // The start sequence (Z at 40 mm) for the first minute: the timelapse starts meanwhile.
        Raise(polls[0].State, polls[0].Z);
        FakeTimelapse timelapse = await TimelapseAsync(service, 1);

        int index = 1;
        for (; polls[index].State == PrinterState.Printing; index++)
        {
            Raise(polls[index].State, polls[index].Z);
        }

        // The start and 276 layer changes (277 layers of 0.2 mm, the first reached from the start sequence); no timer of
        // the trigger, only the disk check.
        Assert.Equal(1 + 276, timelapse.Requests);
        Assert.Equal(1, _time.ActiveTimers);

        // The end of the print: the frame of the finished part.
        Raise(polls[index].State, polls[index].Z);
        Assert.Equal(PrinterState.Finished, polls[index].State);
        Assert.Equal(1 + 276 + 1, timelapse.Requests);
        Assert.Single(_timelapses.Started);
    }

    [Fact]
    public async Task EveryNthLayerOfTheSettingsAppliesAndChangingItReplacesTheRunningTrigger()
    {
        CameraRecordingService service = CreateService();
        service.Configure([Printer()], LayerSettings with { TimelapseEveryNthLayer = 3 });
        FakeTimelapse timelapse = await StartAsync(service);
        Layer(1);
        for (int layer = 2; layer <= 10; layer++)
        {
            Layer(layer);
        }

        // Nine changes: frames at the 3rd, 6th and 9th.
        Assert.Equal(1 + 3, timelapse.Requests);
        Assert.Equal(1, _time.ActiveTimers);

        // Every layer from now on: the new trigger takes a frame at once and learns the current layer first.
        service.Configure([Printer()], LayerSettings);
        Assert.Equal(5, timelapse.Requests);
        Layer(10);
        Layer(11);
        Layer(12);
        Assert.Equal(7, timelapse.Requests);
        Assert.Equal(1, _time.ActiveTimers);
    }

    [Fact]
    public async Task SwitchingBetweenTheTimeAndTheLayerTriggerReplacesTheRunningOne()
    {
        CameraRecordingService service = CreateService();
        service.Configure([Printer()], CameraRecordingSettings.Default);
        FakeTimelapse timelapse = await StartAsync(service);
        Assert.Equal(2, _time.ActiveTimers); // the time trigger and the disk check

        service.Configure([Printer()], LayerSettings);
        Assert.Equal(1, _time.ActiveTimers); // the time trigger's timer is gone
        int requests = timelapse.Requests;
        Clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(requests, timelapse.Requests);
        Layer(2);
        Layer(3);
        Assert.Equal(requests + 1, timelapse.Requests);

        // Changing only the interval keeps the layer trigger (and its layer).
        service.Configure([Printer()], LayerSettings with { TimelapseIntervalSeconds = 60 });
        Assert.Equal(requests + 1, timelapse.Requests);
        Layer(4);
        Assert.Equal(requests + 2, timelapse.Requests);

        service.Configure([Printer()], CameraRecordingSettings.Default);
        Assert.Equal(2, _time.ActiveTimers);
        Assert.Equal(requests + 3, timelapse.Requests);
    }

    [Fact]
    public async Task APauseTakesNoFramesAndTheNextJobStartsItsOwnTimelapseAndLayers()
    {
        CameraRecordingService service = CreateService();
        service.Configure([Printer()], LayerSettings);
        FakeTimelapse first = await StartAsync(service);
        Layer(1);
        Layer(2);
        Layer(3);
        Assert.Equal(3, first.Requests);

        Raise(PrinterState.Paused, 15.6);
        Raise(PrinterState.Paused, 15.6);
        Assert.False(service.GetStatus("core-one").Timelapse.IsCapturing);
        Assert.False(first.IsRequestPending);
        Layer(3);
        Layer(4);
        Assert.Equal(4, first.Requests);

        // A new job right away: its own file, and its first plateau (above the old layer) is no change.
        Layer(5, JobId + 1);
        await Eventually.TrueAsync(() => first.IsEnded, "first timelapse ended");
        FakeTimelapse second = await TimelapseAsync(service, 2);
        Layer(5, JobId + 1);
        Assert.Equal(1, second.Requests);
        Layer(6, JobId + 1);
        Assert.Equal(2, second.Requests);
    }

    [Fact]
    public async Task ThePrintersOutageKeepsTheLayer()
    {
        CameraRecordingService service = CreateService();
        service.Configure([Printer()], LayerSettings);
        FakeTimelapse timelapse = await StartAsync(service);
        Layer(1);
        Layer(2);
        Assert.Equal(2, timelapse.Requests);

        _monitor.Raise(PrinterSnapshot.Unavailable("core-one", PrinterState.Offline, Clock.GetUtcNow()));
        Clock.Advance(TimeSpan.FromSeconds(20));
        Layer(4);

        Assert.Equal(3, timelapse.Requests);
        Assert.False(timelapse.IsEnded);
    }

    [Fact]
    public async Task WithTheRealRecorderEveryLayerChangeWritesTheNextKeyFrameOfTheStream()
    {
        FakeItemFeedFactory feeds = new();
        CameraFeedOptions options = new() { ConnectTimeout = CameraFeedOptions.MaxValue, FrameTimeout = CameraFeedOptions.MaxValue, ReconnectDelay = TimeSpan.FromSeconds(5) };
        CameraStreamHub hub = new(new ReconnectingCameraFeed(feeds, Clock, new ListLogger<ReconnectingCameraFeed>(), options), Clock, new ListLogger<CameraStreamHub>());
        _disposables.Add(hub);
        hub.Configure([Printer()]);
        FakeRecordingWriterFactory writers = new();
        CameraRecordingService service = CreateService(new CameraTimelapseRecorder(hub, writers, new ListLogger<CameraTimelapseRecorder>()));
        service.Configure([Printer()], LayerSettings with { TimelapseEveryNthLayer = 2 });

        Raise(PrinterState.Printing, 0.2);
        FakeItemFeed feed = await feeds.FeedAsync(1);
        long Frames() => service.GetStatus("core-one").Timelapse.FrameCount;
        async Task KeyFramesAsync(long frames)
        {
            // The stream goes on (a packet and a key frame now and then) until the frame asked for is written; key frames
            // nobody asked for are not.
            for (int round = 0; Frames() < frames; round++)
            {
                Assert.True(round < 500, $"{frames} frames expected, {Frames()} written");
                feed.PushPacket(1);
                feed.PushPacket(2, keyFrame: true);
                await Task.Delay(10, Ct);
            }

            Assert.Equal(frames, Frames());
        }

        // The first frame (the start); the trigger exists once it is written.
        await KeyFramesAsync(1);
        Layer(1);
        for (int layer = 2; layer <= 9; layer++)
        {
            Layer(layer);
            await KeyFramesAsync(1 + ((layer - 1) / 2));
        }

        Raise(PrinterState.Finished, 225.6);
        await KeyFramesAsync(6);
        feed.PushPacket(2, keyFrame: true);
        await Task.Delay(50, Ct);
        Assert.Equal(6, Frames());
        // Only key frames, one per frame asked for.
        Assert.All(Assert.Single(writers.Writers).Packets, static p => Assert.True(p.IsKeyFrame));
        Assert.Equal(6, writers.Writers[0].Packets.Length);
    }
}
