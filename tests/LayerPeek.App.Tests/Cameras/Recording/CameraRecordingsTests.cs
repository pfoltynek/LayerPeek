using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.App.Cameras.Recording;
using LayerPeek.App.Platform;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Cameras.Recording;

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
