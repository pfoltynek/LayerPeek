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
/// T-165: "Export timelapse to JPEG…" of the UI adapter: the open dialog starts in the printer's recordings folder (the root
/// before it exists), filters timelapses, and the chosen file is exported; one dialog and one export at a time.
/// </summary>
public sealed class CameraRecordingsExportTests : IAsyncDisposable
{
    private readonly string _root = TestDirectories.NewDirectoryPath();
    private readonly CameraRecordingsTests.LauncherSpy _launchers = new();
    private readonly FakeRecordingFilePicker _picker = new();
    private readonly CameraRecordingService _service;
    private readonly CameraTimelapseJpegExporter _exporter;
    private readonly TaskCompletionSource<CameraTimelapseExportResult> _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public CameraRecordingsExportTests()
    {
        _service = new CameraRecordingService(
            new FakePrinterMonitor(),
            new CameraRecordingsTests.NoRecorder(),
            CameraRecordingStorage.Instance,
            TimeProvider.System,
            NullLogger<CameraRecordingService>.Instance,
            new CameraRecordingServiceOptions { DefaultRoot = _root });
        _service.Configure([Printer()], CameraRecordingSettings.Default);
        _exporter = new CameraTimelapseJpegExporter(new TwoFrameFiles(), new ByteEncoders(), NullLogger<CameraTimelapseJpegExporter>.Instance, _service);
        _exporter.Completed += (_, result) => _completed.TrySetResult(result);
    }

    public async ValueTask DisposeAsync()
    {
        await _exporter.DisposeAsync();
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

    private CameraRecordings Recordings(IRecordingFilePicker? picker = null, CameraTimelapseJpegExporter? exporter = null) =>
        new(_service, _launchers, _launchers, new CameraRecordingServiceOptions { DefaultRoot = _root }, exporter ?? _exporter, picker ?? _picker);

    [Fact]
    public async Task TheChosenTimelapseOfThePrinterFolderIsExportedNextToIt()
    {
        string folder = _service.GetPrinterFolder("mk4")!;
        CameraRecordingStorage.Instance.PrepareFolder(folder);
        string timelapse = Path.Combine(folder, "2026-11-04_10-00-00_timelapse.mp4");
        await File.WriteAllBytesAsync(timelapse, [1], TestContext.Current.CancellationToken);
        _picker.Answer = timelapse;
        CameraRecordings recordings = Recordings();
        Assert.True(recordings.IsTimelapseExportAvailable);

        await recordings.ExportTimelapseToJpegAsync("mk4");
        CameraTimelapseExportResult result = await _completed.Task.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);

        RecordingFilePickRequest request = Assert.Single(_picker.Requests);
        Assert.Equal(folder, request.StartFolder);
        Assert.Equal(Strings.TimelapseExportPickerTitle, request.Title);
        Assert.Equal(["*_timelapse.mp4", "*_timelapse-*.mp4"], request.Patterns);
        Assert.Equal("mp4", request.Extension);
        Assert.True(result.Succeeded);
        Assert.Equal("mk4", result.PrinterId);
        Assert.Equal(Path.Combine(folder, "2026-11-04_10-00-00_timelapse_jpg"), result.OutputFolder);
        Assert.Equal(2, Directory.GetFiles(result.OutputFolder!).Length);
        Assert.Equal(CameraTimelapseExportStatus.Idle, recordings.TimelapseExport);
    }

    [Fact]
    public async Task BeforeThePrinterHasAFolderTheDialogOpensInTheRootOrWhereTheSystemWants()
    {
        CameraRecordings recordings = Recordings();

        await recordings.ExportTimelapseToJpegAsync("mk4");
        Directory.CreateDirectory(_root);
        await recordings.ExportTimelapseToJpegAsync("mk4");

        Assert.Equal([null, _root], _picker.Requests.Select(static r => r.StartFolder));
        // Cancelled dialogs: nothing was exported.
        Assert.False(_completed.Task.IsCompleted);
    }

    [Fact]
    public async Task OneDialogAtATimeAndNoneWhileAnExportRuns()
    {
        TaskCompletionSource open = new();
        _picker.OpenUntil = open.Task;
        CameraRecordings recordings = Recordings();

        Task first = recordings.ExportTimelapseToJpegAsync("mk4");
        await Waiting.UntilAsync(() => _picker.Requests.Count == 1, "the dialog");
        await recordings.ExportTimelapseToJpegAsync("mk4");
        Assert.Single(_picker.Requests);
        open.SetResult();
        await first;

        // An export runs (a dialog that never answers holds the exporter's own run): no dialog.
        string timelapse = Path.Combine(_root, "2026-11-04_10-00-00_timelapse.mp4");
        Directory.CreateDirectory(_root);
        await File.WriteAllBytesAsync(timelapse, [1], TestContext.Current.CancellationToken);
        TwoFrameFiles slow = new() { Gate = new TaskCompletionSource() };
        await using CameraTimelapseJpegExporter exporter = new(slow, new ByteEncoders(), NullLogger<CameraTimelapseJpegExporter>.Instance);
        CameraRecordings busy = Recordings(exporter: exporter);
        Assert.True(exporter.Start(timelapse));
        await busy.ExportTimelapseToJpegAsync("mk4");
        Assert.Single(_picker.Requests);
        Assert.True(busy.TimelapseExport.IsRunning);
        busy.CancelTimelapseExport();
        slow.Gate.SetResult();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AnEmptyAnswerOfTheDialogStartsNothingAndDoesNotThrow(string answer)
    {
        _picker.Answer = answer;
        CameraRecordings recordings = Recordings();

        await recordings.ExportTimelapseToJpegAsync("mk4");

        Assert.Single(_picker.Requests);
        Assert.False(recordings.TimelapseExport.IsRunning);
        Assert.False(_completed.Task.IsCompleted);
    }

    [Fact]
    public async Task WithoutAPickerOrAnExporterThereIsNoExport()
    {
        CameraRecordings none = new(_service, _launchers, _launchers, new CameraRecordingServiceOptions { DefaultRoot = _root });

        Assert.False(none.IsTimelapseExportAvailable);
        Assert.Equal(CameraTimelapseExportStatus.Idle, none.TimelapseExport);
        await none.ExportTimelapseToJpegAsync("mk4");
        none.CancelTimelapseExport();
        none.TimelapseExportChanged += static (_, _) => { };
        Assert.Empty(_picker.Requests);
    }

    /// <summary>Every file has two frames of 2 × 2; the decoding pass can wait for <see cref="Gate"/>.</summary>
    private sealed class TwoFrameFiles : ICameraFeedFactory
    {
        public TaskCompletionSource? Gate { get; init; }

        public ICameraFeed Create(Uri streamUri) => new Feed(Gate);

        private sealed class Feed(TaskCompletionSource? gate) : ICameraFeed
        {
            private readonly byte[] _pixels = new byte[16];

            public async IAsyncEnumerable<CameraFrame> ReadFramesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
            {
                for (int i = 0; i < 2; i++)
                {
                    if (gate is not null)
                    {
                        await gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                    }

                    yield return new CameraFrame(2, 2, 8, _pixels);
                }
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class ByteEncoders : ICameraJpegSequenceEncoderFactory
    {
        public ICameraJpegSequenceEncoder Create(int quality) => new Encoder();

        private sealed class Encoder : ICameraJpegSequenceEncoder
        {
            public void Encode(in CameraFrame frame, Stream destination) => destination.WriteByte(1);

            public void Dispose()
            {
            }
        }
    }
}
