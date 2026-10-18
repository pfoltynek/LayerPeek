using Microsoft.Extensions.Logging.Abstractions;

using PrusaTrayDiagnostic.App.Cameras;
using PrusaTrayDiagnostic.App.Tests.Tray;

namespace PrusaTrayDiagnostic.App.Tests.Cameras;

public sealed class CameraWindowStateStoreTests : IDisposable
{
    private static readonly CameraWindowState Mini = new(
        CameraWindowMode.Mini,
        new CameraWindowPlacement(-1900, 40, 960, 540),
        new CameraWindowPlacement(1500, 860, 320, 180));

    private readonly string _directory = TestDirectories.NewDirectoryPath();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => TestDirectories.Delete(_directory);

    private JsonCameraWindowStateStore Create() => new(_directory, NullLogger<JsonCameraWindowStateStore>.Instance);

    private string FilePath => Path.Combine(_directory, JsonCameraWindowStateStore.FileName);

    [Fact]
    public async Task MissingFileGivesDefaultsAndCreatesNothing()
    {
        JsonCameraWindowStateStore store = Create();

        await store.LoadAsync(Ct);

        Assert.Same(CameraWindowState.Default, store.Get("mk4"));
        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public async Task SavedStateIsLoadedByTheNextRun()
    {
        JsonCameraWindowStateStore first = Create();
        first.Set("mk4", Mini);
        first.Set("xl", CameraWindowState.Default with { Mode = CameraWindowMode.FullScreen });
        Assert.True(await first.WaitForPendingSaveAsync(TestTimeouts.Wait));

        JsonCameraWindowStateStore second = Create();
        await second.LoadAsync(Ct);

        Assert.Equal(Mini, second.Get("MK4"));
        Assert.Equal(CameraWindowMode.FullScreen, second.Get("xl").Mode);
        Assert.Null(second.Get("xl").Normal);
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public async Task StateSetBeforeLoadingWinsOverTheFile()
    {
        JsonCameraWindowStateStore first = Create();
        first.Set("mk4", Mini);
        await first.WaitForPendingSaveAsync(TestTimeouts.Wait);

        JsonCameraWindowStateStore second = Create();
        CameraWindowState newer = CameraWindowState.Default with { Mode = CameraWindowMode.FullScreen };
        second.Set("mk4", newer);
        await second.LoadAsync(Ct);

        Assert.Equal(newer, second.Get("mk4"));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"version\":1,\"windows\":null}")]
    public async Task DamagedFileGivesDefaults(string content)
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(FilePath, content, Ct);
        JsonCameraWindowStateStore store = Create();

        await store.LoadAsync(Ct);

        Assert.Equal(CameraWindowState.Default, store.Get("mk4"));
    }

    [Fact]
    public async Task InvalidValuesAreReplacedByDefaults()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(
            FilePath,
            """
            {
              "version": 1,
              "windows": {
                "mk4": { "mode": "Sideways", "normal": { "x": 1, "y": 2, "width": -5, "height": 100 }, "mini": { "x": 3, "y": 4, "width": 300, "height": 99999 } },
                "xl": { "mode": "mini", "mini": { "x": 3, "y": 4, "width": 300, "height": 170 } }
              }
            }
            """,
            Ct);
        JsonCameraWindowStateStore store = Create();

        await store.LoadAsync(Ct);

        Assert.Equal(CameraWindowState.Default, store.Get("mk4"));
        Assert.Equal(new CameraWindowState(CameraWindowMode.Mini, null, new CameraWindowPlacement(3, 4, 300, 170)), store.Get("xl"));
    }

    [Fact]
    public async Task OnlyTheMostRecentPrintersAreKept()
    {
        JsonCameraWindowStateStore first = Create();
        for (int i = 0; i < 70; i++)
        {
            first.Set($"p{i}", Mini);
        }

        await first.WaitForPendingSaveAsync(TestTimeouts.Wait);
        JsonCameraWindowStateStore second = Create();
        await second.LoadAsync(Ct);

        Assert.Equal(CameraWindowState.Default, second.Get("p0"));
        Assert.Equal(Mini, second.Get("p69"));
        Assert.Equal(Mini, second.Get("p6"));
    }

    [Fact]
    public async Task UnwritableLocationIsReportedOnlyInTheLog()
    {
        // A file where the directory should be: saving fails, nothing throws.
        Directory.CreateDirectory(TestDirectories.Root);
        await File.WriteAllTextAsync(_directory, "blocker", Ct);
        try
        {
            JsonCameraWindowStateStore store = Create();

            store.Set("mk4", Mini);

            Assert.True(await store.WaitForPendingSaveAsync(TestTimeouts.Wait));
            Assert.Equal(Mini, store.Get("mk4"));
        }
        finally
        {
            File.Delete(_directory);
        }
    }

    [Fact]
    public void MemoryStoreRemembersForThisRun()
    {
        MemoryCameraWindowStateStore store = new();

        store.Set("mk4", Mini);

        Assert.Equal(Mini, store.Get("MK4"));
        Assert.Same(CameraWindowState.Default, store.Get("xl"));
    }
}
