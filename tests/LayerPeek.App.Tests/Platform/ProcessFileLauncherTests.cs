using System.Diagnostics;

using LayerPeek.App.Platform;
using LayerPeek.App.Tests.Tray;

namespace LayerPeek.App.Tests.Platform;

/// <summary>
/// "Play last recording" (T-146): an existing <c>.mp4</c> recording is handed to the file manager by absolute path (Explorer /
/// <c>/usr/bin/open</c>), which opens it in the default app; anything else is refused, so that a program is never started.
/// Failures are reported, never thrown, and the path never reaches the log.
/// </summary>
public sealed class ProcessFileLauncherTests : IDisposable
{
    private readonly string _folder = TestDirectories.NewDirectoryPath();
    private readonly List<ProcessStartInfo> _started = [];
    private readonly ListLogger<ProcessFileLauncher> _logger = new();

    public ProcessFileLauncherTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => TestDirectories.Delete(_folder);

    private ProcessFileLauncher Launcher(Func<ProcessStartInfo, IDisposable?>? start = null) =>
        new(_logger, UrlLaunchPlatform.MacOS, start ?? (info =>
        {
            _started.Add(info);
            return null;
        }));

    private string File(string name)
    {
        string path = Path.Combine(_folder, name);
        System.IO.File.WriteAllBytes(path, [0]);
        return path;
    }

    [Fact]
    public async Task AnExistingRecordingIsOpenedAsOneArgumentByAbsolutePath()
    {
        string recording = File("2026-11-03_10-00-00_benchy.MP4");

        Assert.True(await Launcher().OpenAsync(recording));

        ProcessStartInfo info = Assert.Single(_started);
        Assert.Equal("/usr/bin/open", info.FileName);
        Assert.False(info.UseShellExecute);
        Assert.Equal([recording], info.ArgumentList);
    }

    [Theory]
    [InlineData("tool.exe")]
    [InlineData("script.command")]
    [InlineData("video.mp4.app")]
    [InlineData("noextension")]
    public async Task AnythingButARecordingIsRefused(string name)
    {
        Assert.False(await Launcher().OpenAsync(File(name)));
        Assert.False(await Launcher().OpenAsync("relative/2026-11-03_10-00-00.mp4"));

        Assert.Empty(_started);
    }

    [Fact]
    public async Task AMissingRecordingAndAFailedStartAreReportedWithoutThePath()
    {
        string missing = Path.Combine(_folder, "2026-11-03_10-00-00.mp4");
        Assert.False(await Launcher().OpenAsync(missing));

        string recording = File("2026-11-03_11-00-00.mp4");
        Assert.False(await Launcher(static _ => throw new System.ComponentModel.Win32Exception(2)).OpenAsync(recording));

        Assert.Empty(_started);
        Assert.DoesNotContain(_logger.Entries, entry => entry.Message.Contains(_folder, StringComparison.Ordinal));
        Assert.Contains(_logger.Entries, static entry => entry.Message.Contains("Win32Exception", StringComparison.Ordinal));
    }
}
