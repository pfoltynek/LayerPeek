using System.Runtime.InteropServices;

using Avalonia;
using Avalonia.Media;

using LayerPeek.App.Settings;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Secrets;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.Tests.Settings;

/// <summary>In-memory secret store (no OS credential store is touched) that records operations and fails on demand.</summary>
internal sealed class FakeSecretStore(List<string>? log = null) : ISecretStore
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, string> _secrets = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _operations = [];

    /// <summary>Keys whose Set fails (<see cref="SecretStoreException"/>).</summary>
    public HashSet<string> FailSet { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Keys whose Get fails.</summary>
    public HashSet<string> FailGet { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Keys whose Delete fails.</summary>
    public HashSet<string> FailDelete { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>"get key", "set key", "delete key" in call order (secrets are never recorded).</summary>
    public IReadOnlyList<string> Operations
    {
        get
        {
            lock (_lock)
            {
                return [.. _operations];
            }
        }
    }

    public IReadOnlyDictionary<string, string> Secrets
    {
        get
        {
            lock (_lock)
            {
                return new Dictionary<string, string>(_secrets, StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    public void Seed(string printerId, string secret)
    {
        lock (_lock)
        {
            _secrets[SecretKeys.ForPrinter(printerId)] = secret;
        }
    }

    private void Record(string operation)
    {
        _operations.Add(operation);
        log?.Add(operation);
    }

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            Record("get " + key);
            return FailGet.Contains(key)
                ? Task.FromException<string?>(new SecretStoreException("get failed"))
                : Task.FromResult(_secrets.GetValueOrDefault(key));
        }
    }

    public Task SetAsync(string key, string secret, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            Record("set " + key);
            if (FailSet.Contains(key))
            {
                return Task.FromException(new SecretStoreException("set failed"));
            }

            _secrets[key] = secret;
            return Task.CompletedTask;
        }
    }

    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            Record("delete " + key);
            return FailDelete.Contains(key)
                ? Task.FromException<bool>(new SecretStoreException("delete failed"))
                : Task.FromResult(_secrets.Remove(key));
        }
    }
}

/// <summary>In-memory settings store that records saves into a shared operation log and fails on demand.</summary>
internal sealed class MemorySettingsStore(List<string>? log = null) : ISettingsStore
{
    public SettingsLoadResult LoadResult { get; set; } = new() { Settings = AppSettings.Default, Status = SettingsLoadStatus.NotFound };

    public AppSettings? Saved { get; private set; }

    public Exception? SaveFailure { get; set; }

    public int LoadCount { get; private set; }

    public Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken)
    {
        LoadCount++;
        return Task.FromResult(LoadResult);
    }

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        log?.Add("save settings");
        if (SaveFailure is not null)
        {
            return Task.FromException(SaveFailure);
        }

        Saved = settings;
        return Task.CompletedTask;
    }
}

internal sealed class RecordingApplier(List<string>? log = null) : ISettingsApplier
{
    public List<AppSettings> Applied { get; } = [];

    /// <summary>What <see cref="ApplyAsync"/> returns: when the previous monitoring run ended (default: already).</summary>
    public Task OldRunEnded { get; set; } = Task.CompletedTask;

    public List<bool> CredentialsChanged { get; } = [];

    /// <summary>What monitoring runs with (T-039: the latest detected models); <see langword="null"/> = unknown.</summary>
    public AppSettings? CurrentSettings { get; set; }

    public Task ApplyAsync(AppSettings settings, bool credentialsChanged)
    {
        log?.Add("apply");
        Applied.Add(settings);
        CredentialsChanged.Add(credentialsChanged);
        return OldRunEnded.ContinueWith(_ => log?.Add("old run ended"), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}

/// <summary>Editor store for view model tests: answers loads and saves as the test says.</summary>
internal sealed class FakeEditorStore : ISettingsEditorStore
{
    public SettingsEditorData Data { get; set; } = new(AppSettings.Default, SettingsLoadStatus.NotFound, [], new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    /// <summary>When set, loads wait for it (to test closing the window while loading).</summary>
    public TaskCompletionSource? LoadGate { get; set; }

    public SettingsSaveResult SaveResult { get; set; } = new(SettingsSaveStatus.Saved);

    /// <summary>When set, saves wait for it.</summary>
    public TaskCompletionSource? SaveGate { get; set; }

    public List<SettingsChange> Saves { get; } = [];

    public async Task<SettingsEditorData> LoadAsync(CancellationToken cancellationToken)
    {
        if (LoadGate is not null)
        {
            await LoadGate.Task.WaitAsync(cancellationToken);
        }

        return Data;
    }

    public Task<bool> WaitForPendingSaveAsync(TimeSpan timeout) => Task.FromResult(true);

    public async Task<SettingsSaveResult> SaveAsync(SettingsChange change)
    {
        Saves.Add(change);
        if (SaveGate is not null)
        {
            await SaveGate.Task;
        }

        return SaveResult;
    }
}

/// <summary>Connection tester driven by the test: each call waits for <see cref="Respond"/> (or cancellation).</summary>
internal sealed class FakeConnectionTester : IConnectionTester
{
    private readonly Lock _lock = new();
    private TaskCompletionSource<ConnectionTestResult>? _pending;

    public List<(PrinterDefinition Printer, string? Password)> Calls { get; } = [];

    /// <summary>When set, calls complete immediately with it.</summary>
    public ConnectionTestResult? Immediate { get; set; }

    public CancellationToken LastToken { get; private set; }

    public Task<ConnectionTestResult> TestAsync(PrinterDefinition printer, string? password, CancellationToken cancellationToken)
    {
        Calls.Add((printer, password));
        LastToken = cancellationToken;
        if (Immediate is not null)
        {
            return Task.FromResult(Immediate);
        }

        TaskCompletionSource<ConnectionTestResult> pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock)
        {
            _pending = pending;
        }

        cancellationToken.Register(() => pending.TrySetCanceled(cancellationToken));
        return pending.Task;
    }

    public void Respond(ConnectionTestResult result)
    {
        lock (_lock)
        {
            _pending?.TrySetResult(result);
        }
    }
}

/// <summary>
/// Camera tester driven by the test: an immediate result, an exception, a result the test completes later (ignoring
/// cancellation, like a capture that was already finishing), or waiting until cancelled.
/// </summary>
internal sealed class FakeCameraTester : ICameraTester
{
    public TimeSpan Timeout => TimeSpan.FromSeconds(15);

    public List<Uri> Calls { get; } = [];

    public CameraTestResult? Immediate { get; set; }

    /// <summary>When set (and <see cref="Immediate"/> is not), the test ends when the test completes this source.</summary>
    public TaskCompletionSource<CameraTestResult>? Pending { get; set; }

    public Exception? Throw { get; set; }

    public CancellationToken LastToken { get; private set; }

    public Task<CameraTestResult> TestAsync(Uri streamUri, CancellationToken cancellationToken)
    {
        Calls.Add(streamUri);
        LastToken = cancellationToken;
        if (Throw is not null)
        {
            return Task.FromException<CameraTestResult>(Throw);
        }

        if (Immediate is not null)
        {
            return Task.FromResult(Immediate);
        }

        if (Pending is not null)
        {
            return Pending.Task;
        }

        return WaitForCancellationAsync(cancellationToken);
    }

    private static async Task<CameraTestResult> WaitForCancellationAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken);
        return new CameraTestResult(CameraTestOutcome.Failed);
    }
}

/// <summary>Camera images for tests (BGRA, colours given as 0xAARRGGBB).</summary>
internal static class TestCameraImages
{
    public static CameraImage Solid(int width, int height, uint argb) => Quadrants(width, height, argb, argb, argb, argb);

    /// <summary>Four colours, one per quarter (left/right half, top/bottom half).</summary>
    public static CameraImage Quadrants(int width, int height, uint topLeft, uint topRight, uint bottomLeft, uint bottomRight)
    {
        uint[] pixels = new uint[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool left = x < width / 2;
                pixels[(y * width) + x] = y < height / 2 ? (left ? topLeft : topRight) : (left ? bottomLeft : bottomRight);
            }
        }

        // Little-endian: 0xAARRGGBB is stored as B, G, R, A.
        byte[] bytes = MemoryMarshal.AsBytes(pixels.AsSpan()).ToArray();
        return new CameraFrame(width, height, width * CameraFrame.BytesPerPixel, bytes).ToImage();
    }
}

/// <summary>What <see cref="FakePreviewRenderer"/> made of a snapshot; records its disposal.</summary>
internal sealed class FakePreviewImage(CameraImage source) : IImage, IDisposable
{
    public CameraImage Source { get; } = source;

    public int DisposeCount { get; private set; }

    public bool IsDisposed => DisposeCount > 0;

    public Size Size => new(Source.Width, Source.Height);

    public void Draw(DrawingContext context, Rect sourceRect, Rect destRect)
    {
    }

    public void Dispose() => DisposeCount++;
}

/// <summary>Preview renderer without a graphics platform: fake images, or a failure on demand.</summary>
internal sealed class FakePreviewRenderer : ICameraPreviewRenderer
{
    public List<FakePreviewImage> Rendered { get; } = [];

    public Exception? Throw { get; set; }

    public IImage Render(CameraImage image)
    {
        if (Throw is not null)
        {
            throw Throw;
        }

        FakePreviewImage preview = new(image);
        Rendered.Add(preview);
        return preview;
    }
}

internal static class SettingsTestData
{
    public static PrinterDefinition LinkPrinter(string id, string name, string address = "192.168.1.20", string userName = PrinterUserName.Default) =>
        new() { Id = id, Name = name, ConnectionType = ConnectionType.PrusaLink, Address = address, UserName = userName };

    public static PrinterDefinition CloudPrinter(string id, string name) =>
        new() { Id = id, Name = name, ConnectionType = ConnectionType.PrusaConnect, Address = "c0ffee00-0000-4000-8000-000000000001" };

    public static HashSet<string> Ids(params string[] ids) => new(ids, StringComparer.OrdinalIgnoreCase);
}
