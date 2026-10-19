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
        Assert.Contains(_logger.Entries, static entry => entry.Level == LogLevel.Warning && entry.Message == "Credentials were removed from the camera addresses of 1 printer(s) in the settings file; nothing else was changed.");
        Assert.All(_logger.Entries, entry => Assert.DoesNotContain("secret", entry.Message, StringComparison.Ordinal));

        // The next start finds nothing to remove.
        using JsonSettingsStore again = new(_directory);
        SettingsLoadResult reloaded = await again.LoadAsync(Ct);
        Assert.Empty(reloaded.Warnings);
        Assert.False(reloaded.Settings.Printers[0].Camera.CredentialsRemoved);
    }

    [Fact]
    public async Task StartupLoaderRemovesTheCredentialsFromTheFileToo()
    {
        // TrayHost loads the settings before the session starts (T-029: the language comes first) with this loader.
        _file = await SettingsFiles.WithCameraCredentialsAsync(_directory, Ct);

        AppSettings settings = await MonitoringSession.LoadSettingsAsync(() => _file, _logger, Ct, _removed);

        string content = await File.ReadAllTextAsync(_file.FilePath, Ct);
        Assert.DoesNotContain("secret", content, StringComparison.Ordinal);
        Assert.Contains("rtsp://192.168.0.227/live", content, StringComparison.Ordinal);
        Assert.False(settings.Printers[0].Camera.Enabled);
        Assert.True(settings.Printers[0].Camera.CredentialsRemoved);
        Assert.Equal(["a"], _removed.PrinterIds);
    }

    [Fact]
    public async Task StartupLoaderWithoutTheNoticeTargetStillCleansTheFile()
    {
        _file = await SettingsFiles.WithCameraCredentialsAsync(_directory, Ct);

        await MonitoringSession.LoadSettingsAsync(() => _file, _logger, Ct);

        Assert.DoesNotContain("secret", await File.ReadAllTextAsync(_file.FilePath, Ct), StringComparison.Ordinal);
        Assert.Empty(_removed.PrinterIds);
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

    [Fact]
    public async Task OnlyTheCameraAddressChangesInTheFile()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, JsonSettingsStore.FileName);
        await File.WriteAllTextAsync(
            path,
            """
            {
              "version": 1,
              "futureSetting": { "keep": true },
              "printers": [
                { "id": "a", "name": "Alpha", "connectionType": "PrusaLink", "address": "192.168.0.226",
                  "camera": { "enabled": true, "streamUrl": "rtsp://admin:secret@192.168.0.227/live" } },
                { "id": "A", "name": "Duplicate", "connectionType": "PrusaLink", "address": "192.168.0.230" }
              ]
            }
            """,
            Ct);
        _file = new JsonSettingsStore(_directory);
        SaveRecordingStore store = new(_file);
        Create(() => store);

        _session!.Start();
        (IReadOnlyList<PrinterDefinition> printers, _) = await _monitor.Started.WaitAsync(TestTimeouts.Wait, Ct);

        string content = await File.ReadAllTextAsync(path, Ct);
        Assert.DoesNotContain("secret", content, StringComparison.Ordinal);
        Assert.Contains("rtsp://192.168.0.227/live", content, StringComparison.Ordinal);
        // The skipped entry (duplicate id) and the unknown value are still there: nothing but the address was written.
        Assert.Contains("Duplicate", content, StringComparison.Ordinal);
        Assert.Contains("futureSetting", content, StringComparison.Ordinal);
        Assert.Equal(0, store.Saves);
        Assert.Single(printers);
        Assert.Equal(["a"], _removed.PrinterIds);
    }

    [Fact]
    public async Task NoticeOnlyForLoadedPrintersWithACamera()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(
            Path.Combine(_directory, JsonSettingsStore.FileName),
            """
            {
              "version": 1,
              "printers": [
                { "id": "a", "name": "Alpha", "connectionType": "PrusaLink", "address": "192.168.0.226",
                  "camera": { "enabled": true, "streamUrl": "rtsp://admin:secret@192.168.0.227/live" } },
                { "id": "skipped", "name": "No address", "connectionType": "PrusaLink",
                  "camera": { "enabled": true, "streamUrl": "rtsp://admin:secret@192.168.0.228/live" } }
              ]
            }
            """,
            Ct);
        _file = new JsonSettingsStore(_directory);
        Create(() => _file);

        _session!.Start();
        await _monitor.Started.WaitAsync(TestTimeouts.Wait, Ct);

        // Both addresses lost the password, but only the loaded printer can show the notice.
        Assert.DoesNotContain("secret", await File.ReadAllTextAsync(_file.FilePath, Ct), StringComparison.Ordinal);
        Assert.Equal(["a"], _removed.PrinterIds);
    }

    [Fact]
    public async Task CorruptedFileLosesTheCredentialsWithoutANotice()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(
            Path.Combine(_directory, JsonSettingsStore.FileName),
            """
            {
              "version": 1,
              "pollingIntervalSeconds": "fast",
              "printers": [
                { "id": "a", "name": "Alpha", "connectionType": "PrusaLink", "address": "192.168.0.226",
                  "camera": { "enabled": true, "streamUrl": "rtsp://admin:secret@192.168.0.227/live" } }
              ]
            }
            """,
            Ct);
        _file = new JsonSettingsStore(_directory);
        Create(() => _file);

        _session!.Start();
        (IReadOnlyList<PrinterDefinition> printers, _) = await _monitor.Started.WaitAsync(TestTimeouts.Wait, Ct);

        // User decision: a syntactically valid but corrupted file is cleaned too; nothing of it was loaded, so no notice.
        Assert.Empty(printers);
        string content = await File.ReadAllTextAsync(_file.FilePath, Ct);
        Assert.DoesNotContain("secret", content, StringComparison.Ordinal);
        Assert.Contains("\"fast\"", content, StringComparison.Ordinal);
        Assert.Empty(_removed.PrinterIds);
    }

    private sealed class FailingSaveStore(ISettingsStore inner) : ISettingsStore
    {
        public Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken) => inner.LoadAsync(cancellationToken);

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken) => Task.FromException(new IOException("read-only"));

        public Task<IReadOnlyList<string>> RemoveCameraCredentialsAsync(CancellationToken cancellationToken) =>
            Task.FromException<IReadOnlyList<string>>(new IOException("read-only"));
    }

    /// <summary>Records whether the full settings were saved (they must not be at startup).</summary>
    private sealed class SaveRecordingStore(ISettingsStore inner) : ISettingsStore
    {
        public int Saves { get; private set; }

        public Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken) => inner.LoadAsync(cancellationToken);

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            Saves++;
            return inner.SaveAsync(settings, cancellationToken);
        }

        public Task<IReadOnlyList<string>> RemoveCameraCredentialsAsync(CancellationToken cancellationToken) =>
            inner.RemoveCameraCredentialsAsync(cancellationToken);
    }
}
