using Microsoft.Extensions.Logging;

using PrusaTrayDiagnostic.App.Cameras;
using PrusaTrayDiagnostic.App.Tests.Settings;
using PrusaTrayDiagnostic.App.Tray;
using PrusaTrayDiagnostic.Core.Cameras;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Settings;

using static PrusaTrayDiagnostic.App.Tests.Tray.TestPrinters;

namespace PrusaTrayDiagnostic.App.Tests.Tray;

/// <summary>T-014 decision: credentials removed from a camera address while loading are removed from the file right away.</summary>
public sealed class MonitoringSessionCameraTests : IAsyncDisposable
{
    private readonly FakePrinterMonitor _monitor = new();
    private readonly ListLogger<MonitoringSession> _logger = new();
    private readonly RemovedCameraCredentials _removed = new();
    private readonly string _directory = TestDirectories.NewDirectoryPath();
    private MonitoringSession? _session;
    private JsonSettingsStore? _file;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        if (_session is not null)
        {
            await _session.DisposeAsync();
        }

        _file?.Dispose();
        TestDirectories.Delete(_directory);
    }

    private MonitoringSession Create(Func<ISettingsStore> store) =>
        _session = new MonitoringSession(store, _monitor, [], _logger, _removed);

    [Fact]
    public async Task CredentialsAreRemovedFromTheFileBeforeMonitoringStarts()
    {
        _file = await SettingsFiles.WithCameraCredentialsAsync(_directory, Ct);
        Create(() => _file);

        _session!.Start();
        (IReadOnlyList<PrinterDefinition> printers, _) = await _monitor.Started.WaitAsync(TestTimeouts.Wait, Ct);

        string content = await File.ReadAllTextAsync(_file.FilePath, Ct);
        Assert.DoesNotContain("secret", content, StringComparison.Ordinal);
        Assert.DoesNotContain("admin", content, StringComparison.Ordinal);
        Assert.Contains("rtsp://192.168.0.227/live", content, StringComparison.Ordinal);
        // The camera stays off until the user reviews it; the other camera is untouched.
        Assert.Equal(new CameraSettings(enabled: false, new Uri("rtsp://192.168.0.227/live")), printers[0].Camera);
        Assert.True(printers[1].Camera.Enabled);
        Assert.Equal(["a"], _removed.PrinterIds);
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("removed from 1 camera", StringComparison.Ordinal));
        Assert.All(_logger.Entries, entry => Assert.DoesNotContain("secret", entry.Message, StringComparison.Ordinal));

        // The next start finds nothing to remove.
        using JsonSettingsStore again = new(_directory);
        SettingsLoadResult reloaded = await again.LoadAsync(Ct);
        Assert.Empty(reloaded.Warnings);
        Assert.False(reloaded.Settings.Printers[0].Camera.CredentialsRemoved);
    }

    [Fact]
    public async Task SettingsWithoutCredentialsAreNotWritten()
    {
        MemorySettingsStore store = new()
        {
            LoadResult = new SettingsLoadResult
            {
                Settings = new AppSettings { Printers = [Definition() with { Camera = new CameraSettings(enabled: true, new Uri("rtsp://192.168.0.227/live")) }] },
                Status = SettingsLoadStatus.Loaded,
            },
        };
        Create(() => store);

        _session!.Start();
        await _monitor.Started.WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Null(store.Saved);
        Assert.Empty(_removed.PrinterIds);
    }

    [Fact]
    public async Task FailedSaveIsLoggedAndMonitoringStartsAnyway()
    {
        _file = await SettingsFiles.WithCameraCredentialsAsync(_directory, Ct);
        FailingSaveStore store = new(_file);
        Create(() => store);

        _session!.Start();
        (IReadOnlyList<PrinterDefinition> printers, _) = await _monitor.Started.WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Equal(2, printers.Count);
        Assert.Equal(["a"], _removed.PrinterIds);
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Error && entry.Exception is IOException);
    }

    private sealed class FailingSaveStore(ISettingsStore inner) : ISettingsStore
    {
        public Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken) => inner.LoadAsync(cancellationToken);

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken) => Task.FromException(new IOException("read-only"));
    }
}
