using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.App.Cameras;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Cameras;

namespace LayerPeek.App.Tests.Cameras;

public sealed class CameraWindowStateStoreTests : IAsyncDisposable
{
    private static readonly CameraWindowState Mini = new(
        CameraWindowMode.Mini,
        new CameraWindowPlacement(-1900, 40, 960, 540),
        new CameraWindowPlacement(1500, 860, 320, 180));

    private readonly string _directory = TestDirectories.NewDirectoryPath();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Every store of a test: their background saves must end before the folder is deleted, otherwise a late write can
    // fail the deletion or recreate the folder after it (seen once in a full-solution run).
    private readonly List<JsonCameraWindowStateStore> _stores = [];

    public async ValueTask DisposeAsync()
    {
        List<int> unfinished = [];
        try
        {
            for (int i = 0; i < _stores.Count; i++)
            {
                if (!await _stores[i].WaitForPendingSaveAsync(TestTimeouts.Wait))
                {
                    unfinished.Add(i);
                }
            }
        }
        finally
        {
            // Deleted even when a save timed out; a later write stays in the reserved folder, removed at the end of the run.
            TestDirectories.Delete(_directory);
        }

        Assert.True(unfinished.Count == 0, $"Camera window state saves did not finish (stores {string.Join(", ", unfinished)}).");
    }

    private JsonCameraWindowStateStore Create()
    {
        JsonCameraWindowStateStore store = new(_directory, NullLogger<JsonCameraWindowStateStore>.Instance);
        _stores.Add(store);
        return store;
    }

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
        Assert.True(await first.WaitForPendingSaveAsync(TestTimeouts.Wait));

        JsonCameraWindowStateStore second = Create();
        CameraWindowState newer = CameraWindowState.Default with { Mode = CameraWindowMode.FullScreen };
        second.Set("mk4", newer);
        await second.LoadAsync(Ct);

        Assert.Equal(newer, second.Get("mk4"));
        // The save started by Set merges the file and writes the newer state.
        Assert.True(await second.WaitForPendingSaveAsync(TestTimeouts.Wait));
        JsonCameraWindowStateStore third = Create();
        await third.LoadAsync(Ct);
        Assert.Equal(newer, third.Get("mk4"));
    }

    [Fact]
    public async Task SetBeforeLoadingKeepsTheOtherPrintersOfTheFile()
    {
        JsonCameraWindowStateStore first = Create();
        first.Set("a", Mini);
        first.Set("b", Mini);
        Assert.True(await first.WaitForPendingSaveAsync(TestTimeouts.Wait));

        // Not loaded yet (e.g. a window closed before the background load ran): the save merges the file first.
        JsonCameraWindowStateStore second = Create();
        CameraWindowState full = CameraWindowState.Default with { Mode = CameraWindowMode.FullScreen };
        second.Set("c", full);
        second.Set("a", full);
        Assert.True(await second.WaitForPendingSaveAsync(TestTimeouts.Wait));

        JsonCameraWindowStateStore third = Create();
        await third.LoadAsync(Ct);
        Assert.Equal(full, third.Get("a"));
        Assert.Equal(Mini, third.Get("b"));
        Assert.Equal(full, third.Get("c"));
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

        Assert.True(await first.WaitForPendingSaveAsync(TestTimeouts.Wait));
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

    // ---- Remove (T-035: changed window defaults forget the state of that printer) ----

    [Fact]
    public async Task RemovedStateIsGoneFromTheFileAndOtherPrintersStay()
    {
        JsonCameraWindowStateStore first = Create();
        first.Set("mk4", Mini);
        first.Set("xl", Mini);
        Assert.True(await first.WaitForPendingSaveAsync(TestTimeouts.Wait));

        first.Remove("MK4");

        Assert.Same(CameraWindowState.Default, first.Get("mk4"));
        Assert.Equal(1, first.GetRemoveCount("mk4"));
        Assert.Equal(0, first.GetRemoveCount("xl"));
        Assert.True(await first.WaitForPendingSaveAsync(TestTimeouts.Wait));
        JsonCameraWindowStateStore second = Create();
        await second.LoadAsync(Ct);
        Assert.Same(CameraWindowState.Default, second.Get("mk4"));
        Assert.Equal(Mini, second.Get("xl"));
        Assert.DoesNotContain("mk4", await File.ReadAllTextAsync(FilePath, Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task StateRemovedBeforeTheFileIsReadDoesNotComeBack()
    {
        JsonCameraWindowStateStore first = Create();
        first.Set("mk4", Mini);
        first.Set("xl", Mini);
        Assert.True(await first.WaitForPendingSaveAsync(TestTimeouts.Wait));

        // Not loaded yet (the settings were saved before the background load ran).
        JsonCameraWindowStateStore second = Create();
        second.Remove("mk4");
        await second.LoadAsync(Ct);

        Assert.Same(CameraWindowState.Default, second.Get("mk4"));
        Assert.Equal(Mini, second.Get("xl"));
        Assert.True(await second.WaitForPendingSaveAsync(TestTimeouts.Wait));
        JsonCameraWindowStateStore third = Create();
        await third.LoadAsync(Ct);
        Assert.Same(CameraWindowState.Default, third.Get("mk4"));
        Assert.Equal(Mini, third.Get("xl"));
    }

    [Fact]
    public async Task RemovingAnUnknownPrinterAfterLoadingWritesNothing()
    {
        JsonCameraWindowStateStore store = Create();
        await store.LoadAsync(Ct);

        store.Remove("mk4");

        Assert.True(await store.WaitForPendingSaveAsync(TestTimeouts.Wait));
        Assert.False(Directory.Exists(_directory));
        Assert.Equal(1, store.GetRemoveCount("mk4"));
    }

    [Fact]
    public async Task StateSetAfterRemovingIsRememberedAgain()
    {
        JsonCameraWindowStateStore store = Create();
        await store.LoadAsync(Ct);
        store.Set("mk4", Mini);
        store.Remove("mk4");

        store.Set("mk4", Mini);

        Assert.Equal(Mini, store.Get("mk4"));
        Assert.True(await store.WaitForPendingSaveAsync(TestTimeouts.Wait));
        JsonCameraWindowStateStore second = Create();
        await second.LoadAsync(Ct);
        Assert.Equal(Mini, second.Get("mk4"));
    }

    [Fact]
    public void MemoryStoreRemovesAndCounts()
    {
        MemoryCameraWindowStateStore store = new();
        store.Set("mk4", Mini);

        store.Remove("MK4");
        store.Remove("mk4");

        Assert.Same(CameraWindowState.Default, store.Get("mk4"));
        Assert.Equal(2, store.GetRemoveCount("mk4"));
        Assert.Equal(0, store.GetRemoveCount("xl"));
    }
}
