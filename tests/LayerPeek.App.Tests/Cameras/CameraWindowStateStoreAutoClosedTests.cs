using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.App.Cameras;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Cameras;

namespace LayerPeek.App.Tests.Cameras;

/// <summary>
/// The "closed automatically" flags of the camera windows (T-150) in <c>camera-windows.json</c>: app state like the
/// remembered placement, read leniently (a damaged flag list gives no flags, never lost placements).
/// </summary>
public sealed class CameraWindowStateStoreAutoClosedTests : IAsyncDisposable
{
    private static readonly CameraWindowState Mini = new(
        CameraWindowMode.Mini,
        new CameraWindowPlacement(-1900, 40, 960, 540),
        new CameraWindowPlacement(1500, 860, 320, 180));

    private readonly string _directory = TestDirectories.NewDirectoryPath();
    private readonly List<JsonCameraWindowStateStore> _stores = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string FilePath => Path.Combine(_directory, JsonCameraWindowStateStore.FileName);

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

    private async Task<JsonCameraWindowStateStore> LoadedAsync()
    {
        JsonCameraWindowStateStore store = Create();
        await store.LoadAsync(Ct);
        return store;
    }

    private void CopyFixture(string name)
    {
        Directory.CreateDirectory(_directory);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", name), FilePath);
    }

    [Fact]
    public async Task NoFlagsWithoutAFile()
    {
        JsonCameraWindowStateStore store = await LoadedAsync();

        Assert.Empty(store.GetAutoClosedPrinters());
        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public async Task FlagsAreSavedAndLoadedByTheNextRun()
    {
        JsonCameraWindowStateStore first = Create();
        first.Set("mk4", Mini);
        first.SetAutoClosed("mk4", autoClosed: true);
        first.SetAutoClosed("xl", autoClosed: true);
        Assert.True(await first.WaitForPendingSaveAsync(TestTimeouts.Wait));

        JsonCameraWindowStateStore second = await LoadedAsync();

        Assert.Equal(["mk4", "xl"], second.GetAutoClosedPrinters());
        Assert.Equal(Mini, second.Get("mk4"));
        Assert.Contains("\"autoClosed\": [", await File.ReadAllTextAsync(FilePath, Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClearedFlagIsGoneFromTheFile()
    {
        JsonCameraWindowStateStore first = Create();
        first.Set("mk4", Mini);
        first.SetAutoClosed("mk4", autoClosed: true);
        Assert.True(await first.WaitForPendingSaveAsync(TestTimeouts.Wait));

        first.SetAutoClosed("MK4", autoClosed: false);
        Assert.True(await first.WaitForPendingSaveAsync(TestTimeouts.Wait));

        Assert.Empty(first.GetAutoClosedPrinters());
        Assert.DoesNotContain("autoClosed", await File.ReadAllTextAsync(FilePath, Ct), StringComparison.Ordinal);
        Assert.Empty((await LoadedAsync()).GetAutoClosedPrinters());
    }

    [Fact]
    public async Task UnchangedFlagWritesNothing()
    {
        JsonCameraWindowStateStore store = await LoadedAsync();

        store.SetAutoClosed("mk4", autoClosed: false);

        Assert.True(await store.WaitForPendingSaveAsync(TestTimeouts.Wait));
        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public async Task FlagsAreCaseInsensitiveAndKeepTheFirstSpelling()
    {
        JsonCameraWindowStateStore store = await LoadedAsync();

        store.SetAutoClosed("mk4", autoClosed: true);
        store.SetAutoClosed("MK4", autoClosed: true);

        Assert.Equal(["mk4"], store.GetAutoClosedPrinters());
    }

    [Fact]
    public async Task FixtureFlagsAreLoaded()
    {
        CopyFixture("camera-windows-auto-closed.json");

        JsonCameraWindowStateStore store = await LoadedAsync();

        Assert.Equal(["mk4", "xl"], store.GetAutoClosedPrinters());
        Assert.Equal(Mini, store.Get("mk4"));
    }

    [Fact]
    public async Task InvalidFlagItemsAreSkippedAndTheRestOfTheFileIsKept()
    {
        CopyFixture("camera-windows-auto-closed-invalid-items.json");

        JsonCameraWindowStateStore store = await LoadedAsync();

        Assert.Equal(["mk4"], store.GetAutoClosedPrinters());
        Assert.Equal(Mini, store.Get("mk4"));
    }

    [Fact]
    public async Task FlagListOfTheWrongTypeGivesNoFlagsAndKeepsThePlacements()
    {
        CopyFixture("camera-windows-auto-closed-wrong-type.json");

        JsonCameraWindowStateStore store = await LoadedAsync();

        Assert.Empty(store.GetAutoClosedPrinters());
        Assert.Equal(Mini, store.Get("mk4"));
    }

    [Theory]
    [InlineData("""{ "version": 1, "windows": { "mk4": { "mode": "Mini" } }, "autoClosed": [ "mk4", "x""")]
    [InlineData("not json")]
    [InlineData("""[ "mk4" ]""")]
    [InlineData("""{ "version": 1, "windows": null, "autoClosed": null }""")]
    public async Task DamagedFileGivesNoFlags(string content)
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(FilePath, content, Ct);

        JsonCameraWindowStateStore store = await LoadedAsync();

        Assert.Empty(store.GetAutoClosedPrinters());
    }

    [Fact]
    public async Task FlagsWithoutWindowsAreLoaded()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(FilePath, """{ "version": 1, "autoClosed": [ "mk4" ] }""", Ct);

        JsonCameraWindowStateStore store = await LoadedAsync();

        Assert.Equal(["mk4"], store.GetAutoClosedPrinters());
        Assert.Same(CameraWindowState.Default, store.Get("mk4"));
    }

    [Fact]
    public async Task FileOfThePreviousVersionHasNoFlags()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(FilePath, """{ "version": 1, "windows": { "mk4": { "mode": "Mini" } } }""", Ct);

        JsonCameraWindowStateStore store = await LoadedAsync();

        Assert.Empty(store.GetAutoClosedPrinters());
        Assert.Equal(CameraWindowMode.Mini, store.Get("mk4").Mode);
    }

    [Fact]
    public async Task FlagChangedBeforeLoadingWinsOverTheFile()
    {
        CopyFixture("camera-windows-auto-closed.json");

        // Not loaded yet: the cleared flag stays cleared, a new one is added, the untouched one comes from the file.
        JsonCameraWindowStateStore second = Create();
        second.SetAutoClosed("mk4", autoClosed: false);
        second.SetAutoClosed("mini", autoClosed: true);
        await second.LoadAsync(Ct);

        Assert.Equal(["xl", "mini"], second.GetAutoClosedPrinters());
        Assert.True(await second.WaitForPendingSaveAsync(TestTimeouts.Wait));
        JsonCameraWindowStateStore third = await LoadedAsync();
        Assert.Equal(["xl", "mini"], third.GetAutoClosedPrinters());
        Assert.Equal(Mini, third.Get("mk4"));
    }

    [Fact]
    public async Task ForgettingTheRememberedStateKeepsTheFlag()
    {
        // T-035: changed window defaults forget the placement, not the "closed automatically" flag.
        JsonCameraWindowStateStore first = Create();
        first.Set("mk4", Mini);
        first.SetAutoClosed("mk4", autoClosed: true);
        first.Remove("mk4");
        Assert.True(await first.WaitForPendingSaveAsync(TestTimeouts.Wait));

        JsonCameraWindowStateStore second = await LoadedAsync();

        Assert.Same(CameraWindowState.Default, second.Get("mk4"));
        Assert.Equal(["mk4"], second.GetAutoClosedPrinters());
    }

    [Fact]
    public async Task OnlyTheMostRecentFlagsAreKept()
    {
        JsonCameraWindowStateStore first = Create();
        for (int i = 0; i < 70; i++)
        {
            first.SetAutoClosed($"p{i}", autoClosed: true);
        }

        Assert.True(await first.WaitForPendingSaveAsync(TestTimeouts.Wait));
        IReadOnlyList<string> loaded = (await LoadedAsync()).GetAutoClosedPrinters();

        Assert.Equal(64, loaded.Count);
        Assert.Equal("p6", loaded[0]);
        Assert.Equal("p69", loaded[^1]);
    }

    [Fact]
    public void MemoryStoreRemembersFlagsForThisRun()
    {
        MemoryCameraWindowStateStore store = new();

        store.SetAutoClosed("mk4", autoClosed: true);
        store.SetAutoClosed("MK4", autoClosed: true);
        store.SetAutoClosed("xl", autoClosed: true);
        store.SetAutoClosed("XL", autoClosed: false);

        Assert.Equal(["mk4"], store.GetAutoClosedPrinters());
    }

    [Fact]
    public void InvalidPrinterIdIsRejected()
    {
        JsonCameraWindowStateStore store = Create();

        Assert.ThrowsAny<ArgumentException>(() => store.SetAutoClosed(" ", autoClosed: true));
        Assert.ThrowsAny<ArgumentException>(() => new MemoryCameraWindowStateStore().SetAutoClosed("", autoClosed: true));
    }
}
