#if DEBUG
using LayerPeek.App.Platform;
using LayerPeek.App.Tests.Tray;

namespace LayerPeek.App.Tests.Platform;

/// <summary>
/// The test mode never starts a player (T-210, user decision 2026-11-13): "Play last recording" appends the full path of the
/// recording, one complete line each, to <c>opened-files.txt</c> in the test folder; the same files are refused as by
/// <see cref="ProcessFileLauncher"/>, and no path reaches the log.
/// </summary>
public sealed class RecordingFileLauncherTests : IDisposable
{
    private readonly string _folder = TestDirectories.NewDirectoryPath();
    private readonly string _recordings;
    private readonly ListLogger<RecordingFileLauncher> _logger = new();

    public RecordingFileLauncherTests()
    {
        _recordings = Path.Combine(_folder, "Recordings", "Core One");
        Directory.CreateDirectory(_recordings);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string FilePath => Path.Combine(_folder, "opened-files.txt");

    public void Dispose() => TestDirectories.Delete(_folder);

    private string Recording(string name)
    {
        string path = Path.Combine(_recordings, name);
        File.WriteAllBytes(path, [0]);
        return path;
    }

    [Fact]
    public async Task RecordingsAreAppendedAsFullPathsOneLineEach()
    {
        RecordingFileLauncher launcher = new(_folder, _logger);
        string first = Recording("2026-11-15_10-00-00_benchy.mp4");
        string second = Recording("2026-11-15_11-00-00_cube-2.MP4");

        Assert.True(await launcher.OpenAsync(first));
        Assert.True(await launcher.OpenAsync(second));

        Assert.Equal(FilePath, launcher.FilePath);
        Assert.Equal(first + "\n" + second + "\n", await File.ReadAllTextAsync(FilePath, Ct));
        Assert.All(_logger.Entries, entry =>
        {
            Assert.DoesNotContain(_folder, entry.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("benchy", entry.Message, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("tool.exe")]
    [InlineData("video.mp4.app")]
    [InlineData("noextension")]
    public async Task AnythingButAnExistingRecordingIsRefused(string name)
    {
        RecordingFileLauncher launcher = new(_folder, _logger);

        Assert.False(await launcher.OpenAsync(Recording(name)));
        Assert.False(await launcher.OpenAsync("relative/2026-11-15_10-00-00.mp4"));
        Assert.False(await launcher.OpenAsync(Path.Combine(_recordings, "deleted.mp4")));

        Assert.False(File.Exists(FilePath));
    }
}
#endif
