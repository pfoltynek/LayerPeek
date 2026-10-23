using Microsoft.Extensions.Logging;

using PrusaTrayDiagnostic.App.Cameras;
using PrusaTrayDiagnostic.App.Tests.Tray;
using PrusaTrayDiagnostic.Core.Cameras;
using PrusaTrayDiagnostic.Core.Settings;

namespace PrusaTrayDiagnostic.App.Tests.Cameras;

/// <summary>
/// The saved camera monitor follows the monitor to another connector (user decision 2026-10-23): once, in the
/// background, through the settings store, with one Information line and no paths in the log.
/// </summary>
public sealed class CameraMonitorPathUpdaterTests
{
    private const string OldPath = @"\\?\DISPLAY#DEL415A#5&224edec0&0&UID160002#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
    private const string NewPath = @"\\?\DISPLAY#DEL415A#5&224edec0&0&UID160004#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";

    private static readonly CameraMonitorId Saved = new("DELL U2719D", 2560, 1440, OldPath);
    private static readonly CameraMonitorId Moved = Saved.WithDevicePath(NewPath);

    private readonly ListLogger<CameraMonitorPathUpdater> _logger = new();
    private readonly MonitorStore _store = new();

    private CameraMonitorPathUpdater Create(Func<ISettingsStore>? store = null) => new(store ?? (() => _store), _logger);

    [Fact]
    public async Task PathIsSavedOnceAndLoggedWithoutPaths()
    {
        CameraMonitorPathUpdater updater = Create();

        updater.Request("mk4", Saved, Moved);
        updater.Request("MK4", Saved, Moved);
        Assert.True(await updater.WaitForPendingAsync(TestTimeouts.Wait));
        updater.Request("mk4", Saved, Moved);
        Assert.True(await updater.WaitForPendingAsync(TestTimeouts.Wait));

        Assert.Equal([("mk4", Saved, Moved)], _store.Calls);
        (LogLevel level, string message, _) = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Information, level);
        Assert.Contains("mk4", message, StringComparison.Ordinal);
        Assert.DoesNotContain("UID16", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OtherPrintersAndOtherPathsAreSavedSeparately()
    {
        CameraMonitorPathUpdater updater = Create();

        updater.Request("mk4", Saved, Moved);
        updater.Request("xl", Saved, Moved);
        updater.Request("mk4", Moved, Saved);
        Assert.True(await updater.WaitForPendingAsync(TestTimeouts.Wait));

        Assert.Equal(3, _store.Calls.Count);
    }

    [Fact]
    public async Task NothingWrittenIsNotReportedAsAnUpdate()
    {
        _store.Result = false;
        CameraMonitorPathUpdater updater = Create();

        updater.Request("mk4", Saved, Moved);
        updater.Request("mk4", Saved, Moved);
        Assert.True(await updater.WaitForPendingAsync(TestTimeouts.Wait));

        Assert.Single(_store.Calls);
        Assert.DoesNotContain(_logger.Entries, static entry => entry.Level >= LogLevel.Information);
    }

    [Fact]
    public async Task FailureIsLoggedAndTriedAgainOnTheNextRequest()
    {
        _store.Failure = new IOException("settings.json is locked");
        CameraMonitorPathUpdater updater = Create();

        updater.Request("mk4", Saved, Moved);
        Assert.True(await updater.WaitForPendingAsync(TestTimeouts.Wait));

        (LogLevel level, string message, Exception? exception) = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Warning, level);
        Assert.IsType<IOException>(exception);
        Assert.DoesNotContain("UID16", message, StringComparison.Ordinal);

        _store.Failure = null;
        updater.Request("mk4", Saved, Moved);
        Assert.True(await updater.WaitForPendingAsync(TestTimeouts.Wait));

        Assert.Equal(2, _store.Calls.Count);
        Assert.Equal(LogLevel.Information, _logger.Entries[^1].Level);
    }

    [Fact]
    public async Task UnavailableStoreIsLogged()
    {
        CameraMonitorPathUpdater updater = Create(static () => throw new InvalidOperationException("No data folder."));

        updater.Request("mk4", Saved, Moved);
        Assert.True(await updater.WaitForPendingAsync(TestTimeouts.Wait));

        Assert.Equal(LogLevel.Warning, Assert.Single(_logger.Entries).Level);
    }

    [Fact]
    public async Task RequestReturnsBeforeTheSave()
    {
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _store.Gate = release.Task;
        CameraMonitorPathUpdater updater = Create();

        updater.Request("mk4", Saved, Moved);

        Assert.False(await updater.WaitForPendingAsync(TimeSpan.FromMilliseconds(50)));
        release.SetResult();
        Assert.True(await updater.WaitForPendingAsync(TestTimeouts.Wait));
        Assert.Single(_store.Calls);
    }

    [Fact]
    public void InvalidRequestsAreRejected()
    {
        CameraMonitorPathUpdater updater = Create();

        Assert.Throws<ArgumentException>(() => updater.Request(" ", Saved, Moved));
        Assert.Throws<ArgumentNullException>(() => updater.Request("mk4", null!, Moved));
        Assert.Throws<ArgumentNullException>(() => updater.Request("mk4", Saved, null!));
    }

    private sealed class MonitorStore : ISettingsStore
    {
        private readonly Lock _lock = new();
        private readonly List<(string, CameraMonitorId, CameraMonitorId)> _calls = [];

        public bool Result { get; set; } = true;

        public Exception? Failure { get; set; }

        public Task Gate { get; set; } = Task.CompletedTask;

        public IReadOnlyList<(string, CameraMonitorId, CameraMonitorId)> Calls
        {
            get
            {
                lock (_lock)
                {
                    return [.. _calls];
                }
            }
        }

        public Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken) => throw new NotSupportedException();

        public async Task<bool> SetCameraMonitorAsync(string printerId, CameraMonitorId expected, CameraMonitorId updated, CancellationToken cancellationToken)
        {
            await Gate.WaitAsync(cancellationToken);
            lock (_lock)
            {
                _calls.Add((printerId, expected, updated));
            }

            return Failure is { } failure ? throw failure : Result;
        }
    }
}
