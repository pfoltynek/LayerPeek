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

    private sealed class NoRecorder : ICameraRecorder
    {
        public CameraRecorderAvailability Availability { get; } = CameraRecorderAvailability.Unavailable("No MP4 muxer in this build.");

        public ICameraRecording Start(string printerId, string filePath, Action<ICameraRecording>? changed = null) =>
            throw new InvalidOperationException("Never started without the muxer.");
    }

    private sealed class LauncherSpy : IFolderLauncher, IFileLauncher
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
