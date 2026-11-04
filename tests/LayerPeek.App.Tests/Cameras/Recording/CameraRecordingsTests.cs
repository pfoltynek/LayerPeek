using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.App.Cameras.Recording;
using LayerPeek.App.Platform;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Recording;
using LayerPeek.Core.Printers;

namespace LayerPeek.App.Tests.Cameras.Recording;

/// <summary>
/// The adapter of the UI over <see cref="CameraRecordingService"/> (T-146): it opens only the folder of the running settings
/// or a fully qualified one, and plays only a recording the service knows; nothing else reaches the launchers.
/// </summary>
public sealed class CameraRecordingsTests : IDisposable
{
    private readonly string _root = TestDirectories.NewDirectoryPath();
    private readonly LauncherSpy _launchers = new();

    public void Dispose() => TestDirectories.Delete(_root);

    private CameraRecordingService Service() => new(
        new FakePrinterMonitor(),
        new NoRecorder(),
        CameraRecordingStorage.Instance,
        TimeProvider.System,
        NullLogger<CameraRecordingService>.Instance,
        new CameraRecordingServiceOptions { DefaultRoot = _root });

    private static PrinterDefinition Printer() => new()
    {
        Id = "mk4",
        Name = "Workshop MK4",
        ConnectionType = ConnectionType.PrusaLink,
        Address = "192.168.0.10",
        Camera = new CameraSettings(enabled: true, new Uri("rtsp://192.168.0.10/live")),
    };

    private CameraRecordings Recordings(CameraRecordingService service) =>
        new(service, _launchers, _launchers, new CameraRecordingServiceOptions { DefaultRoot = _root, MinFreeBytes = 123 });

    [Fact]
    public async Task OpenFolderOpensTheRunningFolderOrAFullyQualifiedOneOnly()
    {
        await using CameraRecordingService service = Service();
        CameraRecordings recordings = Recordings(service);
        string other = Path.Combine(_root, "other");

        Assert.True(await recordings.OpenFolderAsync());
        Assert.True(await recordings.OpenFolderAsync(other));
        Assert.False(await recordings.OpenFolderAsync("relative/folder"));

        Assert.Equal([service.RecordingsFolder, other], _launchers.Folders);
        Assert.Equal(_root, recordings.DefaultFolder);
        Assert.Equal(123, recordings.MinFreeBytes);
        Assert.False(recordings.IsAvailable);
    }

    [Fact]
    public async Task OpenPrinterFolderOpensItsSubfolderOnceItExistsAndTheRootBefore()
    {
        await using CameraRecordingService service = Service();
        service.Configure([Printer()], CameraRecordingSettings.Default);
        CameraRecordings recordings = Recordings(service);
        string printerFolder = service.GetPrinterFolder("mk4")!;

        // Not recorded yet: the root (the folder launcher creates it), never an empty printer folder.
        Assert.True(await recordings.OpenPrinterFolderAsync("mk4"));
        Assert.False(Directory.Exists(printerFolder));
        // An unknown printer (removed since the menu was built): the root as well.
        Assert.True(await recordings.OpenPrinterFolderAsync("removed"));

        Directory.CreateDirectory(printerFolder);
        Assert.True(await recordings.OpenPrinterFolderAsync("MK4"));

        Assert.Equal([_root, _root, printerFolder], _launchers.Folders);
        Assert.Equal(_root, Path.GetDirectoryName(printerFolder));
    }

    [Fact]
    public async Task PlayLastWithoutARecordingStartsNothing()
    {
        await using CameraRecordingService service = Service();

        Assert.False(await Recordings(service).PlayLastAsync("mk4"));

        Assert.Empty(_launchers.Files);
    }

    /// <summary>
    /// T-161: "Play last timelapse" opens the newest timelapse the service knows (also from an earlier run), "Play last
    /// recording" never picks it; without a timelapse recorder the timelapse cannot start.
    /// </summary>
    [Fact]
    public async Task PlayLastTimelapseOpensTheNewestTimelapseAndPlayLastTheNewestRecording()
    {
        await using CameraRecordingService service = Service();
        CameraRecordings recordings = Recordings(service);
        service.Configure([Printer()], CameraRecordingSettings.Default);
        string folder = service.GetPrinterFolder("mk4")!;
        CameraRecordingStorage.Instance.PrepareFolder(folder);
        string timelapse = Path.Combine(folder, "2026-11-04_08-00-00_Benchy_timelapse.mp4");
        string recording = Path.Combine(folder, "2026-11-04_09-00-00_Benchy.mp4");
        await File.WriteAllBytesAsync(timelapse, [1], TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(recording, [1], TestContext.Current.CancellationToken);

        // A new configuration scans the folder again (the rotation knows the newest files of every printer).
        service.Configure([Printer() with { Name = "Workshop MK4" }], new CameraRecordingSettings { SizeLimitGigabytes = 9 });
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        while (service.GetStatus("mk4").Timelapse.LastFile is null || service.GetStatus("mk4").LastFile is null)
        {
            await Task.Delay(10, timeout.Token);
        }

        Assert.True(await recordings.PlayLastTimelapseAsync("mk4"));
        Assert.True(await recordings.PlayLastAsync("mk4"));
        Assert.Equal([timelapse, recording], _launchers.Files);
        Assert.False(recordings.IsTimelapseAvailable);
        Assert.False(recordings.StartTimelapse("mk4"));
        recordings.StopTimelapse("mk4");
    }

    internal sealed class NoRecorder : ICameraRecorder
    {
        public CameraRecorderAvailability Availability { get; } = CameraRecorderAvailability.Unavailable("No MP4 muxer in this build.");

        public ICameraRecording Start(string printerId, string filePath, Action<ICameraRecording>? changed = null) =>
            throw new InvalidOperationException("Never started without the muxer.");

        public ICameraDashcam StartDashcam(string printerId, CameraDashcamOptions options, Action<ICameraDashcam>? changed = null, Action<CameraDashcamSave>? saved = null) =>
            throw new InvalidOperationException("Never started without the muxer.");
    }

    internal sealed class LauncherSpy : IFolderLauncher, IFileLauncher
    {
        public List<string> Folders { get; } = [];

        public List<string> Files { get; } = [];

        Task<bool> IFolderLauncher.OpenAsync(string directory)
        {
            Folders.Add(directory);
            return Task.FromResult(true);
        }

        Task<bool> IFileLauncher.OpenAsync(string file)
        {
            Files.Add(file);
            return Task.FromResult(true);
        }
    }
}
