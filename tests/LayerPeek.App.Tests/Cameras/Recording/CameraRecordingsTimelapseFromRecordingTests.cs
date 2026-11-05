using System.Runtime.CompilerServices;

using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.App.Cameras.Recording;
using LayerPeek.App.Resources;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Recording;
using LayerPeek.Core.Cameras.Timelapse;
using LayerPeek.Core.Printers;

namespace LayerPeek.App.Tests.Cameras.Recording;

/// <summary>
/// T-164: "Create timelapse from recording…" of the UI adapter: the open dialog starts in the printer's recordings folder and
/// lists MP4 files, the chosen recording becomes a timelapse at the interval of the live timelapse (T-160, no dialog of its
/// own) next to it; one dialog at a time, shared with the JPEG export (T-165), and none while a timelapse is made.
/// </summary>
public sealed class CameraRecordingsTimelapseFromRecordingTests : IAsyncDisposable
{
    private readonly string _root = TestDirectories.NewDirectoryPath();
    private readonly CameraRecordingsTests.LauncherSpy _launchers = new();
    private readonly FakeRecordingFilePicker _picker = new();
    private readonly CameraRecordingService _service;
    private readonly KeyFrameFiles _files = new();
    private readonly CameraTimelapseFromRecordingMaker _maker;
    private readonly TaskCompletionSource<CameraTimelapseFromRecordingResult> _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public CameraRecordingsTimelapseFromRecordingTests()
    {
        _service = new CameraRecordingService(
            new FakePrinterMonitor(),
            new CameraRecordingsTests.NoRecorder(),
            CameraRecordingStorage.Instance,
            TimeProvider.System,
            NullLogger<CameraRecordingService>.Instance,
            new CameraRecordingServiceOptions { DefaultRoot = _root });
        _service.Configure([Printer()], new CameraRecordingSettings { TimelapseIntervalSeconds = 10 });
        _maker = new CameraTimelapseFromRecordingMaker(_files, new FileWriters(), NullLogger<CameraTimelapseFromRecordingMaker>.Instance, _service);
        _maker.Completed += (_, result) => _completed.TrySetResult(result);
    }

    public async ValueTask DisposeAsync()
    {
        await _maker.DisposeAsync();
        await _service.DisposeAsync();
        TestDirectories.Delete(_root);
    }

    private static PrinterDefinition Printer() => new()
    {
        Id = "mk4",
        Name = "Workshop MK4",
        ConnectionType = ConnectionType.PrusaLink,
        Address = "192.168.0.10",
        Camera = new CameraSettings(enabled: true, new Uri("rtsp://192.168.0.10/live")),
    };

    private CameraRecordings Recordings(CameraTimelapseFromRecordingMaker? maker = null, CameraTimelapseJpegExporter? exporter = null) =>
        new(_service, _launchers, _launchers, new CameraRecordingServiceOptions { DefaultRoot = _root }, exporter, _picker, maker ?? _maker);

    [Fact]
    public async Task TheChosenRecordingBecomesATimelapseAtTheIntervalOfTheSettingsNextToIt()
    {
        string folder = _service.GetPrinterFolder("mk4")!;
        CameraRecordingStorage.Instance.PrepareFolder(folder);
        string recording = Path.Combine(folder, "2026-11-04_10-00-00_Benchy.mp4");
        await File.WriteAllBytesAsync(recording, [1], TestContext.Current.CancellationToken);
        _picker.Answer = recording;
        CameraRecordings recordings = Recordings();
        Assert.True(recordings.IsTimelapseFromRecordingAvailable);

        await recordings.CreateTimelapseFromRecordingAsync("mk4");
        CameraTimelapseFromRecordingResult result = await _completed.Task.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);

        RecordingFilePickRequest request = Assert.Single(_picker.Requests);
        Assert.Equal(folder, request.StartFolder);
        Assert.Equal(Strings.TimelapseFromRecordingPickerTitle, request.Title);
        Assert.Equal(Strings.TimelapseFromRecordingPickerFilter, request.FilterName);
        Assert.Equal(["*.mp4"], request.Patterns);
        Assert.Equal("mp4", request.Extension);
        Assert.True(result.Succeeded, result.Problem.ToString());
        Assert.Equal("mk4", result.PrinterId);
        // 60 s of recording with a key frame every 2 s, every 10 s: 0, 10 … 50 s.
        Assert.Equal(6, result.Frames);
        Assert.Equal([Path.Combine(folder, "2026-11-04_10-00-00_Benchy_recording_timelapse.mp4")], result.OutputFiles);
        Assert.Equal([0, 10, 20, 30, 40, 50], await File.ReadAllBytesAsync(result.OutputFiles[0], TestContext.Current.CancellationToken));
        Assert.Equal(CameraTimelapseFromRecordingStatus.Idle, recordings.TimelapseFromRecording);
    }

    [Fact]
    public async Task OneDialogAtATimeAlsoWithTheJpegExportAndNoneWhileATimelapseIsMade()
    {
        TaskCompletionSource open = new();
        _picker.OpenUntil = open.Task;
        await using CameraTimelapseJpegExporter exporter = new(_files, new NoEncoders(), NullLogger<CameraTimelapseJpegExporter>.Instance);
        CameraRecordings recordings = Recordings(exporter: exporter);

        Task first = recordings.ExportTimelapseToJpegAsync("mk4");
        await Waiting.UntilAsync(() => _picker.Requests.Count == 1, "the dialog");
        await recordings.CreateTimelapseFromRecordingAsync("mk4");
        Assert.Single(_picker.Requests);
        open.SetResult();
        await first;

        // A timelapse is made (the recording holds its packets back): no dialog; the item cancels it.
        string recording = Path.Combine(_root, "2026-11-04_10-00-00.mp4");
        Directory.CreateDirectory(_root);
        await File.WriteAllBytesAsync(recording, [1], TestContext.Current.CancellationToken);
        KeyFrameFiles slow = new() { Gate = new TaskCompletionSource() };
        await using CameraTimelapseFromRecordingMaker maker = new(slow, new FileWriters(), NullLogger<CameraTimelapseFromRecordingMaker>.Instance);
        TaskCompletionSource<CameraTimelapseFromRecordingResult> ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
        maker.Completed += (_, result) => ended.TrySetResult(result);
        CameraRecordings busy = Recordings(maker);
        Assert.True(maker.Start(recording, TimeSpan.FromSeconds(10)));
        await busy.CreateTimelapseFromRecordingAsync("mk4");
        Assert.Single(_picker.Requests);
        Assert.True(busy.TimelapseFromRecording.IsRunning);
        busy.CancelTimelapseFromRecording();
        Assert.Equal(CameraTimelapseExportProblem.Cancelled, (await ended.Task.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken)).Problem);
        Assert.Equal([recording], Directory.GetFiles(_root));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task NoChoiceStartsNothing(string? answer)
    {
        _picker.Answer = answer;
        CameraRecordings recordings = Recordings();

        await recordings.CreateTimelapseFromRecordingAsync("mk4");

        Assert.Single(_picker.Requests);
        Assert.False(recordings.TimelapseFromRecording.IsRunning);
        Assert.False(_completed.Task.IsCompleted);
    }

    [Fact]
    public async Task WithoutAMakerAPickerOrAMuxerThereIsNoTimelapseFromARecording()
    {
        CameraRecordings none = new(_service, _launchers, _launchers, new CameraRecordingServiceOptions { DefaultRoot = _root });
        await using CameraTimelapseFromRecordingMaker noMuxer = new(_files, new FileWriters { Available = false }, NullLogger<CameraTimelapseFromRecordingMaker>.Instance);
        CameraRecordings unavailable = Recordings(noMuxer);
        CameraRecordings noPicker = new(_service, _launchers, _launchers, new CameraRecordingServiceOptions { DefaultRoot = _root }, maker: _maker);

        Assert.False(none.IsTimelapseFromRecordingAvailable);
        Assert.False(unavailable.IsTimelapseFromRecordingAvailable);
        Assert.False(noPicker.IsTimelapseFromRecordingAvailable);
        Assert.Equal(CameraTimelapseFromRecordingStatus.Idle, none.TimelapseFromRecording);
        await none.CreateTimelapseFromRecordingAsync("mk4");
        await noPicker.CreateTimelapseFromRecordingAsync("mk4");
        none.CancelTimelapseFromRecording();
        Assert.Empty(_picker.Requests);
    }

    [Fact]
    public async Task TheMakersProgressIsAnnouncedThroughTheSameEventAsTheExport()
    {
        int changes = 0;
        CameraRecordings recordings = Recordings();
        EventHandler handler = (_, _) => Interlocked.Increment(ref changes);
        recordings.TimelapseExportChanged += handler;
        string recording = Path.Combine(_root, "2026-11-04_10-00-00.mp4");
        Directory.CreateDirectory(_root);
        await File.WriteAllBytesAsync(recording, [1], TestContext.Current.CancellationToken);

        Assert.True(_maker.Start(recording, TimeSpan.FromSeconds(10)));
        await _completed.Task.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        int seen = Volatile.Read(ref changes);
        recordings.TimelapseExportChanged -= handler;

        // Started, progress, ended.
        Assert.True(seen >= 2, $"{seen} changes.");
    }

    /// <summary>
    /// Every file is 60 s of a recording at 1 frame/s with a key frame every 2 s (90 kHz); packet <i>n</i> holds the byte
    /// <i>n</i>. Can wait for <see cref="Gate"/> before the first packet.
    /// </summary>
    private sealed class KeyFrameFiles : ICameraFeedFactory
    {
        private static readonly CameraStreamInfo Info = new(CameraVideoCodec.H264, 1280, 720, CameraTimeBase.Rtp90kHz, new byte[] { 1, 0x42 });

        public TaskCompletionSource? Gate { get; init; }

        public ICameraFeed Create(Uri streamUri) => new Feed(Gate);

        private sealed class Feed(TaskCompletionSource? gate) : ICameraFeed
        {
            private readonly byte[] _data = new byte[1];

            public IAsyncEnumerable<CameraFrame> ReadFramesAsync(CancellationToken cancellationToken) => throw new CameraFeedException("No decoder.");

            public async IAsyncEnumerable<CameraStreamItem> ReadAsync(CameraStreamDemand demand, [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                yield return CameraStreamItem.Started(Info);
                for (int n = 0; n < 60; n++)
                {
                    if (gate is not null)
                    {
                        await gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                    }

                    _data[0] = (byte)n;
                    yield return CameraStreamItem.FromPacket(new CameraPacket(_data, n * 90_000L, n * 90_000L, 90_000, n % 2 == 0, CameraTimeBase.Rtp90kHz));
                }
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    /// <summary>Writes the packets' bytes into the file, one after the other.</summary>
    private sealed class FileWriters : ICameraRecordingWriterFactory
    {
        public bool Available { get; init; } = true;

        public CameraRecorderAvailability Availability => Available ? CameraRecorderAvailability.Available : CameraRecorderAvailability.Unavailable("No muxer.");

        public ICameraRecordingWriter Create(string path, CameraStreamInfo info, ReadOnlySpan<byte> firstKeyFrame) => new Writer(path);

        private sealed class Writer(string path) : ICameraRecordingWriter
        {
            private readonly FileStream _file = new(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);

            public long BytesWritten => _file.Length;

            public void Write(ReadOnlySpan<byte> data, long pts, long dts, long duration, bool isKeyFrame) => _file.Write(data);

            public void Complete() => _file.Flush();

            public void Dispose() => _file.Dispose();
        }
    }

    private sealed class NoEncoders : ICameraJpegSequenceEncoderFactory
    {
        public ICameraJpegSequenceEncoder Create(int quality) => throw new InvalidOperationException("No encoder.");
    }
}
