using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using PrusaTrayDiagnostic.App.Cameras;
using PrusaTrayDiagnostic.App.Tests.Tray;
using PrusaTrayDiagnostic.Core.Cameras;
using PrusaTrayDiagnostic.Core.IO;
using PrusaTrayDiagnostic.Tests.Shared;

namespace PrusaTrayDiagnostic.App.Tests.Cameras;

/// <summary>
/// <c>camera-windows.json</c> held open by another process (user decision 2026-10-22): reading and replacing it wait a few
/// short, growing delays (<see cref="FileRetry"/>) and try again; a save that still fails is only logged. The clock is
/// scripted, so nothing really waits. Replacing conflicts with an open file only on Windows; those tests run there only.
/// </summary>
public sealed class CameraWindowStateStoreRetryTests : IAsyncDisposable
{
    private const string WindowsOnly = "Only Windows refuses to replace a file another process holds open.";

    private static readonly CameraWindowState Mini = new(
        CameraWindowMode.Mini,
        new CameraWindowPlacement(-1900, 40, 960, 540),
        new CameraWindowPlacement(1500, 860, 320, 180));

    private static readonly CameraWindowState FullScreen = CameraWindowState.Default with { Mode = CameraWindowMode.FullScreen };

    private readonly string _directory = TestDirectories.NewDirectoryPath();
    private readonly HeldFiles _held = new();
    private readonly List<JsonCameraWindowStateStore> _stores = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string FilePath => Path.Combine(_directory, JsonCameraWindowStateStore.FileName);

    public async ValueTask DisposeAsync()
    {
        _held.Dispose();
        bool allSaved = true;
        try
        {
            foreach (JsonCameraWindowStateStore store in _stores)
            {
                allSaved &= await store.WaitForPendingSaveAsync(TestTimeouts.Wait);
            }
        }
        finally
        {
            TestDirectories.Delete(_directory);
        }

        Assert.True(allSaved, "Camera window state saves did not finish.");
    }

    [Fact]
    public async Task SaveWaitsWhileTheFileIsHeldOpenAndSucceedsOnceReleased()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        await SaveInitialAsync();
        ScriptedDelayTimeProvider time = new() { OnDelay = index => _held.ReleaseWhen(index == 2) };
        JsonCameraWindowStateStore store = Create(NullLogger<JsonCameraWindowStateStore>.Instance, time);
        await store.LoadAsync(Ct);
        _held.Hold(FilePath);

        store.Set("mk4", FullScreen);

        Assert.True(await store.WaitForPendingSaveAsync(TestTimeouts.Wait));
        Assert.Equal(FileRetry.Delays.Take(2), time.Delays);
        Assert.Equal(FullScreen, await LoadAsync("mk4"));
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public async Task SaveThatStillFailsAfterTheRetriesIsOnlyLogged()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        await SaveInitialAsync();
        ListLogger<JsonCameraWindowStateStore> logger = new();
        ScriptedDelayTimeProvider time = new();
        JsonCameraWindowStateStore store = Create(logger, time);
        await store.LoadAsync(Ct);
        _held.Hold(FilePath);

        store.Set("mk4", FullScreen);

        Assert.True(await store.WaitForPendingSaveAsync(TestTimeouts.Wait));
        Assert.Equal(FileRetry.Delays, time.Delays);
        Assert.Contains(logger.Entries, static entry => entry.Level == LogLevel.Warning && entry.Exception is IOException or UnauthorizedAccessException);
        _held.ReleaseAll();
        Assert.Equal(Mini, await LoadAsync("mk4"));
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public async Task LoadWaitsWhileTheFileIsHeldOpen()
    {
        await SaveInitialAsync();
        _held.Hold(FilePath);
        ListLogger<JsonCameraWindowStateStore> logger = new();
        ScriptedDelayTimeProvider time = new() { OnDelay = index => _held.ReleaseWhen(index == 1) };
        JsonCameraWindowStateStore store = Create(logger, time);

        await store.LoadAsync(Ct);

        Assert.Equal(Mini, store.Get("mk4"));
        Assert.Equal(FileRetry.Delays.Take(1), time.Delays);
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task LoadOfAFileStillHeldAfterTheRetriesGivesDefaults()
    {
        await SaveInitialAsync();
        _held.Hold(FilePath);
        ListLogger<JsonCameraWindowStateStore> logger = new();
        ScriptedDelayTimeProvider time = new();
        JsonCameraWindowStateStore store = Create(logger, time);

        await store.LoadAsync(Ct);

        Assert.Same(CameraWindowState.Default, store.Get("mk4"));
        Assert.Equal(FileRetry.Delays, time.Delays);
        Assert.Contains(logger.Entries, static entry => entry.Exception is IOException);
    }

    private JsonCameraWindowStateStore Create(ILogger<JsonCameraWindowStateStore> logger, TimeProvider? time = null)
    {
        JsonCameraWindowStateStore store = new(_directory, logger, time);
        _stores.Add(store);
        return store;
    }

    private async Task SaveInitialAsync()
    {
        JsonCameraWindowStateStore store = Create(NullLogger<JsonCameraWindowStateStore>.Instance);
        store.Set("mk4", Mini);
        Assert.True(await store.WaitForPendingSaveAsync(TestTimeouts.Wait));
        Assert.True(File.Exists(FilePath));
    }

    private async Task<CameraWindowState> LoadAsync(string printerId)
    {
        JsonCameraWindowStateStore store = Create(NullLogger<JsonCameraWindowStateStore>.Instance);
        await store.LoadAsync(Ct);
        return store.Get(printerId);
    }
}
