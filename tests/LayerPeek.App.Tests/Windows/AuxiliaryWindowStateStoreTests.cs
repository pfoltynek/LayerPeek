using System.Text.Json;

using Avalonia;

using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.App.Tests.Tray;
using LayerPeek.App.Windows;

namespace LayerPeek.App.Tests.Windows;

/// <summary><c>windows.json</c>: the remembered size and maximized state of the settings and diagnostics windows (T-094).</summary>
public sealed class AuxiliaryWindowStateStoreTests : IAsyncDisposable
{
    private static readonly AuxiliaryWindowState Large = new(new Size(1200, 760), Maximized: false);

    private static readonly AuxiliaryWindowState Maximized = new(new Size(900, 600), Maximized: true);

    private readonly string _directory = TestDirectories.NewDirectoryPath();

    // Every store of a test: their background saves must end before the folder is deleted.
    private readonly List<JsonAuxiliaryWindowStateStore> _stores = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string FilePath => Path.Combine(_directory, JsonAuxiliaryWindowStateStore.FileName);

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

        Assert.True(unfinished.Count == 0, $"Window state saves did not finish (stores {string.Join(", ", unfinished)}).");
    }

    private JsonAuxiliaryWindowStateStore Create()
    {
        JsonAuxiliaryWindowStateStore store = new(_directory, NullLogger<JsonAuxiliaryWindowStateStore>.Instance);
        _stores.Add(store);
        return store;
    }

    [Fact]
    public async Task CreatingAndLoadingWithoutAFileTouchesNothingAndGivesDefaults()
    {
        JsonAuxiliaryWindowStateStore store = Create();
        Assert.False(Directory.Exists(_directory));

        await store.LoadAsync(Ct);

        Assert.Same(AuxiliaryWindowState.Default, store.Get(AuxiliaryWindowKeys.Settings));
        Assert.Same(AuxiliaryWindowState.Default, store.Get(AuxiliaryWindowKeys.Diagnostics));
        Assert.False(Directory.Exists(_directory));
        Assert.Equal(Path.Combine(_directory, "windows.json"), store.FilePath);
    }

    [Fact]
    public async Task BothWindowsAreRememberedSeparatelyByTheNextRun()
    {
        JsonAuxiliaryWindowStateStore first = Create();
        first.Set(AuxiliaryWindowKeys.Settings, Large);
        first.Set(AuxiliaryWindowKeys.Diagnostics, Maximized);
        Assert.True(await first.WaitForPendingSaveAsync(TestTimeouts.Wait));

        JsonAuxiliaryWindowStateStore second = Create();
        await second.LoadAsync(Ct);

        Assert.Equal(Large, second.Get(AuxiliaryWindowKeys.Settings));
        Assert.Equal(Maximized, second.Get("DIAGNOSTICS"));
        Assert.False(File.Exists(FilePath + ".tmp"));
        Assert.Equal([JsonAuxiliaryWindowStateStore.FileName], Directory.GetFiles(_directory).Select(Path.GetFileName));
    }

    [Fact]
    public async Task FileHasTheDocumentedFormat()
    {
        JsonAuxiliaryWindowStateStore store = Create();
        store.Set(AuxiliaryWindowKeys.Settings, Large);
        store.Set(AuxiliaryWindowKeys.Diagnostics, new AuxiliaryWindowState(null, Maximized: true));
        Assert.True(await store.WaitForPendingSaveAsync(TestTimeouts.Wait));

        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(FilePath, Ct));
        JsonElement root = document.RootElement;
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        JsonElement settings = root.GetProperty("windows").GetProperty("settings");
        Assert.Equal(1200, settings.GetProperty("width").GetDouble());
        Assert.Equal(760, settings.GetProperty("height").GetDouble());
        Assert.False(settings.GetProperty("maximized").GetBoolean());
        JsonElement diagnostics = root.GetProperty("windows").GetProperty("diagnostics");
        Assert.False(diagnostics.TryGetProperty("width", out _));
        Assert.True(diagnostics.GetProperty("maximized").GetBoolean());
    }

    [Fact]
    public async Task UnchangedStateWritesNothing()
    {
        JsonAuxiliaryWindowStateStore store = Create();

        store.Set(AuxiliaryWindowKeys.Settings, AuxiliaryWindowState.Default);

        Assert.True(await store.WaitForPendingSaveAsync(TestTimeouts.Wait));
        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public async Task StateSetBeforeLoadingWinsAndKeepsTheOtherWindowOfTheFile()
    {
        JsonAuxiliaryWindowStateStore first = Create();
        first.Set(AuxiliaryWindowKeys.Settings, Large);
        first.Set(AuxiliaryWindowKeys.Diagnostics, Large);
        Assert.True(await first.WaitForPendingSaveAsync(TestTimeouts.Wait));

        // Not loaded yet: the save merges the file first.
        JsonAuxiliaryWindowStateStore second = Create();
        second.Set(AuxiliaryWindowKeys.Settings, Maximized);
        Assert.True(await second.WaitForPendingSaveAsync(TestTimeouts.Wait));

        JsonAuxiliaryWindowStateStore third = Create();
        await third.LoadAsync(Ct);
        Assert.Equal(Maximized, third.Get(AuxiliaryWindowKeys.Settings));
        Assert.Equal(Large, third.Get(AuxiliaryWindowKeys.Diagnostics));
    }

    [Fact]
    public async Task BothWindowsClosingAtOnceFromDifferentThreadsAreBothSaved()
    {
        JsonAuxiliaryWindowStateStore store = Create();
        await store.LoadAsync(Ct);
        using Barrier start = new(2);

        await Task.WhenAll(
            Task.Run(() => SetMany(store, AuxiliaryWindowKeys.Settings, start), Ct),
            Task.Run(() => SetMany(store, AuxiliaryWindowKeys.Diagnostics, start), Ct));

        Assert.True(await store.WaitForPendingSaveAsync(TestTimeouts.Wait));
        JsonAuxiliaryWindowStateStore next = Create();
        await next.LoadAsync(Ct);
        Assert.Equal(new AuxiliaryWindowState(new Size(899, 600), false), next.Get(AuxiliaryWindowKeys.Settings));
        Assert.Equal(new AuxiliaryWindowState(new Size(899, 600), false), next.Get(AuxiliaryWindowKeys.Diagnostics));
        Assert.False(File.Exists(FilePath + ".tmp"));

        static void SetMany(JsonAuxiliaryWindowStateStore store, string key, Barrier start)
        {
            start.SignalAndWait(TestTimeouts.Wait);
            for (int width = 700; width < 900; width++)
            {
                store.Set(key, new AuxiliaryWindowState(new Size(width, 600), false));
            }
        }
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("")]
    [InlineData("{\"version\":1,\"windows\":null}")]
    [InlineData("{\"version\":1,\"windows\":{\"settings\":{\"width\":\"NaN\",\"height\":600}}}")]
    [InlineData("{\"version\":1,\"windows\":{\"settings\":{\"width\":1e400,\"height\":600}}}")]
    [InlineData("{\"windows\":{\"settings\":{\"width\":900,\"height\":600}}}")]
    public async Task DamagedFileGivesDefaults(string content)
    {
        await WriteFileAsync(content);
        JsonAuxiliaryWindowStateStore store = Create();

        await store.LoadAsync(Ct);

        Assert.Equal(AuxiliaryWindowState.Default, store.Get(AuxiliaryWindowKeys.Settings));
    }

    [Fact]
    public async Task InvalidSizesAreForgottenButTheFlagStays()
    {
        await WriteFileAsync(
            """
            {
              "version": 1,
              "windows": {
                "settings": { "width": -5, "height": 600, "maximized": true },
                "diagnostics": { "width": 900, "height": 99999 },
                "other": { "width": 900 }
              }
            }
            """);
        JsonAuxiliaryWindowStateStore store = Create();

        await store.LoadAsync(Ct);

        Assert.Equal(new AuxiliaryWindowState(null, Maximized: true), store.Get(AuxiliaryWindowKeys.Settings));
        Assert.Equal(AuxiliaryWindowState.Default, store.Get(AuxiliaryWindowKeys.Diagnostics));
        Assert.Equal(AuxiliaryWindowState.Default, store.Get("other"));
    }

    [Fact]
    public async Task TooLargeFileGivesDefaults()
    {
        string padding = new(' ', 70 * 1024);
        await WriteFileAsync("{\"version\":1,\"windows\":{\"settings\":{\"width\":900,\"height\":600}}}" + padding);
        JsonAuxiliaryWindowStateStore store = Create();

        await store.LoadAsync(Ct);

        Assert.Equal(AuxiliaryWindowState.Default, store.Get(AuxiliaryWindowKeys.Settings));
    }

    [Fact]
    public async Task FileOfANewerVersionGivesDefaultsAndIsReplacedByTheNextSave()
    {
        await WriteFileAsync("{\"version\":2,\"windows\":{\"settings\":{\"width\":900,\"height\":600,\"maximized\":true}}}");
        JsonAuxiliaryWindowStateStore store = Create();

        await store.LoadAsync(Ct);
        Assert.Equal(AuxiliaryWindowState.Default, store.Get(AuxiliaryWindowKeys.Settings));

        store.Set(AuxiliaryWindowKeys.Diagnostics, Large);
        Assert.True(await store.WaitForPendingSaveAsync(TestTimeouts.Wait));
        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(FilePath, Ct));
        Assert.Equal(1, document.RootElement.GetProperty("version").GetInt32());
        Assert.False(document.RootElement.GetProperty("windows").TryGetProperty("settings", out _));
    }

    [Fact]
    public async Task FileFullOfUnknownWindowsStillRemembersTheSettingsWindow()
    {
        // A damaged or foreign file with many unknown keys (the load keeps at most a few of them).
        string unknown = string.Join(",", Enumerable.Range(0, 40).Select(static i => $"\"unknown{i:00}\":{{\"width\":900,\"height\":600}}"));
        await WriteFileAsync("{\"version\":1,\"windows\":{" + unknown + "}}");
        JsonAuxiliaryWindowStateStore store = Create();
        await store.LoadAsync(Ct);

        store.Set(AuxiliaryWindowKeys.Settings, Large);
        Assert.True(await store.WaitForPendingSaveAsync(TestTimeouts.Wait));

        JsonAuxiliaryWindowStateStore next = Create();
        await next.LoadAsync(Ct);
        Assert.Equal(Large, next.Get(AuxiliaryWindowKeys.Settings));
    }

    [Fact]
    public async Task KnownWindowAfterManyUnknownOnesIsLoadedAndUnknownOnesStayCapped()
    {
        // The known window comes last in the file, after more unknown keys than the load keeps.
        string unknown = string.Join(",", Enumerable.Range(0, 40).Select(static i => $"\"unknown{i:00}\":{{\"width\":900,\"height\":600}}"));
        await WriteFileAsync("{\"version\":1,\"windows\":{" + unknown + ",\"diagnostics\":{\"width\":900,\"height\":600,\"maximized\":true}}}");
        JsonAuxiliaryWindowStateStore store = Create();
        await store.LoadAsync(Ct);
        Assert.Equal(Maximized, store.Get(AuxiliaryWindowKeys.Diagnostics));

        store.Set(AuxiliaryWindowKeys.Settings, Large);
        Assert.True(await store.WaitForPendingSaveAsync(TestTimeouts.Wait));

        // Unknown windows (another app version) are kept on write, but never more than the load kept.
        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(FilePath, Ct));
        List<string> keys = [.. document.RootElement.GetProperty("windows").EnumerateObject().Select(static property => property.Name)];
        Assert.Contains(AuxiliaryWindowKeys.Settings, keys);
        Assert.Contains(AuxiliaryWindowKeys.Diagnostics, keys);
        Assert.InRange(keys.Count(static key => key.StartsWith("unknown", StringComparison.Ordinal)), 1, 16);
    }

    [Fact]
    public async Task UnknownPropertiesOfTheCurrentVersionAreIgnored()
    {
        await WriteFileAsync("{\"version\":1,\"extra\":true,\"windows\":{\"settings\":{\"width\":900,\"height\":600,\"x\":5}}}");
        JsonAuxiliaryWindowStateStore store = Create();

        await store.LoadAsync(Ct);

        Assert.Equal(new AuxiliaryWindowState(new Size(900, 600), false), store.Get(AuxiliaryWindowKeys.Settings));
    }

    [Fact]
    public async Task UnwritableLocationIsReportedOnlyInTheLog()
    {
        // A file where the directory should be: saving fails, nothing throws.
        Directory.CreateDirectory(TestDirectories.Root);
        await File.WriteAllTextAsync(_directory, "blocker", Ct);
        try
        {
            JsonAuxiliaryWindowStateStore store = Create();

            store.Set(AuxiliaryWindowKeys.Settings, Large);

            Assert.True(await store.WaitForPendingSaveAsync(TestTimeouts.Wait));
            Assert.Equal(Large, store.Get(AuxiliaryWindowKeys.Settings));
        }
        finally
        {
            File.Delete(_directory);
        }
    }

    [Fact]
    public void MemoryStoreRemembersForThisRun()
    {
        MemoryAuxiliaryWindowStateStore store = new();

        store.Set(AuxiliaryWindowKeys.Settings, Large);

        Assert.Equal(Large, store.Get("SETTINGS"));
        Assert.Same(AuxiliaryWindowState.Default, store.Get(AuxiliaryWindowKeys.Diagnostics));
    }

    private async Task WriteFileAsync(string content)
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(FilePath, content, Ct);
    }
}
