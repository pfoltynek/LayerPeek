using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Logging;

using PrusaTrayDiagnostic.Core.Cameras;
using PrusaTrayDiagnostic.Core.IO;

namespace PrusaTrayDiagnostic.App.Cameras;

/// <summary>Remembers the camera windows between runs (T-014): mode, normal window and mini preview placement per printer.</summary>
internal interface ICameraWindowStateStore
{
    /// <summary>Reads the remembered states once (call early, off the UI thread). Never throws for file problems.</summary>
    Task LoadAsync(CancellationToken cancellationToken);

    /// <summary>The remembered state, or <see cref="CameraWindowState.Default"/> (also while <see cref="LoadAsync"/> runs).</summary>
    CameraWindowState Get(string printerId);

    /// <summary>Remembers <paramref name="state"/> and saves it in the background; never blocks and never throws for file problems.</summary>
    void Set(string printerId, CameraWindowState state);

    /// <summary>
    /// Forgets the remembered state of the printer (T-035: its window defaults changed in the settings) and saves in the
    /// background, also when it was only in the file so far. Increments <see cref="GetRemoveCount"/>. Never throws for
    /// file problems.
    /// </summary>
    void Remove(string printerId);

    /// <summary>
    /// How many times the state of the printer was removed in this run. A camera window keeps the value from when it
    /// opened and does not write its state back when it changed meanwhile, so that the new defaults apply next time.
    /// </summary>
    int GetRemoveCount(string printerId);

    /// <summary>Waits at most <paramref name="timeout"/> for background saves; <see langword="true"/> when none is running.</summary>
    Task<bool> WaitForPendingSaveAsync(TimeSpan timeout);
}

/// <summary>
/// <see cref="ICameraWindowStateStore"/> in <c>camera-windows.json</c> next to the settings file. It is app state, not a
/// setting: the settings window never shows or saves it, so a settings save cannot overwrite a window position and vice
/// versa. Writes are atomic (temporary file, then replace) and run one after another on the thread pool; while another
/// process holds the file (antivirus, backup), reading and replacing it are retried briefly (<see cref="FileRetry"/>). A newer state
/// replaces an older one that has not been written yet. A damaged file is ignored (defaults) and replaced by the next save.
/// Creating the store touches no files. Thread-safe.
/// </summary>
internal sealed partial class JsonCameraWindowStateStore : ICameraWindowStateStore
{
    public const string FileName = "camera-windows.json";

    /// <summary>A real file has a few hundred bytes per printer.</summary>
    private const long MaxFileSizeBytes = 256 * 1024;

    /// <summary>Printers removed long ago must not make the file grow forever.</summary>
    private const int MaxEntries = 64;

    private const int CurrentVersion = 1;

    private readonly string _directory;
    private readonly ILogger<JsonCameraWindowStateStore> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _gate = new();

    // Guarded by _gate.
    private readonly Dictionary<string, CameraWindowState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _order = [];

    // Removed in this run (how often): an entry of the file that is read later must not come back.
    private readonly Dictionary<string, int> _removed = new(StringComparer.OrdinalIgnoreCase);
    private Task? _loading;
    private Task _pendingSave = Task.CompletedTask;
    private long _saveVersion;

    public JsonCameraWindowStateStore(string directory, ILogger<JsonCameraWindowStateStore> logger, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(logger);
        _directory = Path.GetFullPath(directory);
        FilePath = Path.Combine(_directory, FileName);
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public string FilePath { get; }

    public Task LoadAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return _loading ??= Task.Run(() => LoadCoreAsync(cancellationToken), cancellationToken);
        }
    }

    public CameraWindowState Get(string printerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerId);
        lock (_gate)
        {
            return _states.GetValueOrDefault(printerId) ?? CameraWindowState.Default;
        }
    }

    public void Set(string printerId, CameraWindowState state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerId);
        ArgumentNullException.ThrowIfNull(state);
        lock (_gate)
        {
            // Unchanged (including a window that was never moved): nothing to write.
            if ((_states.GetValueOrDefault(printerId) ?? CameraWindowState.Default) == state)
            {
                return;
            }

            Remember(printerId, state);
            long version = ++_saveVersion;
            _pendingSave = SaveAfterAsync(_pendingSave, version);
        }
    }

    public void Remove(string printerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerId);
        lock (_gate)
        {
            _removed[printerId] = _removed.GetValueOrDefault(printerId) + 1;
            if (_states.Remove(printerId))
            {
                _order.RemoveAll(id => string.Equals(id, printerId, StringComparison.OrdinalIgnoreCase));
            }
            else if (_loading is { IsCompletedSuccessfully: true })
            {
                // Read already and not there: the file does not have it either.
                return;
            }

            long version = ++_saveVersion;
            _pendingSave = SaveAfterAsync(_pendingSave, version);
        }
    }

    public int GetRemoveCount(string printerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerId);
        lock (_gate)
        {
            return _removed.GetValueOrDefault(printerId);
        }
    }

    public async Task<bool> WaitForPendingSaveAsync(TimeSpan timeout)
    {
        Task pending;
        lock (_gate)
        {
            pending = _pendingSave;
        }

        // SaveAfterAsync never faults; only the timeout matters here.
        await pending.WaitAsync(timeout).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        return pending.IsCompleted;
    }

    private async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        CameraWindowsDocument? document;
        try
        {
            FileInfo file = new(FilePath);
            if (!file.Exists)
            {
                return;
            }

            if (file.Length > MaxFileSizeBytes)
            {
                LogFileIgnored("the file is too large");
                return;
            }

            document = await FileRetry.RunAsync(static (path, token) => ReadDocumentAsync(path, token), FilePath, replacing: false, _timeProvider, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            // Deleted since the check above: nothing to load.
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            LogLoadFailed(ex);
            return;
        }

        if (document?.Windows is not { } windows)
        {
            return;
        }

        lock (_gate)
        {
            // States set before the file was read are newer than the file: they win and stay the most recent ones. States
            // removed before it was read stay removed.
            List<string> loaded = [];
            foreach ((string printerId, CameraWindowDocument? window) in windows)
            {
                if (_states.Count >= MaxEntries || string.IsNullOrWhiteSpace(printerId) || window is null || _states.ContainsKey(printerId) || _removed.ContainsKey(printerId))
                {
                    continue;
                }

                _states[printerId] = FromDocument(window);
                loaded.Add(printerId);
            }

            _order.InsertRange(0, loaded);
        }
    }

    private static async Task<CameraWindowsDocument?> ReadDocumentAsync(string path, CancellationToken cancellationToken)
    {
        FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 4096, useAsync: true);
        await using (stream.ConfigureAwait(false))
        {
            return await JsonSerializer.DeserializeAsync(stream, CameraWindowsJsonContext.Default.CameraWindowsDocument, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Under <c>_gate</c>; the most recently used printer last, the oldest dropped beyond <see cref="MaxEntries"/>.</summary>
    private void Remember(string printerId, CameraWindowState state)
    {
        if (_states.ContainsKey(printerId))
        {
            _order.RemoveAll(id => string.Equals(id, printerId, StringComparison.OrdinalIgnoreCase));
        }

        _states[printerId] = state;
        _order.Add(printerId);
        while (_order.Count > MaxEntries)
        {
            _states.Remove(_order[0]);
            _order.RemoveAt(0);
        }
    }

    private async Task SaveAfterAsync(Task previous, long version)
    {
        await previous.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ForceYielding);
        // The file is read first (once), so that a state set before the background load does not drop the other printers.
        await LoadAsync(CancellationToken.None).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        byte[] content;
        lock (_gate)
        {
            if (version != _saveVersion)
            {
                // A newer state was set meanwhile; its save writes everything.
                return;
            }

            // Serialized now (not when set): includes what the load merged in.
            content = JsonSerializer.SerializeToUtf8Bytes(ToDocument(), CameraWindowsJsonContext.Default.CameraWindowsDocument);
        }

        string temporary = FilePath + ".tmp";
        try
        {
            Directory.CreateDirectory(_directory);
            await FileRetry.RunAsync(
                static async (args, token) =>
                {
                    await File.WriteAllBytesAsync(args.Path, args.Content, token).ConfigureAwait(false);
                    return true;
                },
                (Path: temporary, Content: content),
                replacing: false,
                _timeProvider,
                CancellationToken.None).ConfigureAwait(false);
            // Only the replace is retried here: the complete temporary file stays ready meanwhile.
            await FileRetry.RunAsync(
                static paths => File.Move(paths.Source, paths.Target, overwrite: true),
                (Source: temporary, Target: FilePath),
                replacing: true,
                _timeProvider,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogSaveFailed(ex);
            TryDelete(temporary);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Replaced by the next save.
        }
    }

    private CameraWindowsDocument ToDocument()
    {
        Dictionary<string, CameraWindowDocument?> windows = new(_order.Count, StringComparer.Ordinal);
        foreach (string printerId in _order)
        {
            CameraWindowState state = _states[printerId];
            windows[printerId] = new CameraWindowDocument
            {
                Mode = state.Mode.ToString(),
                Normal = ToDocument(state.Normal),
                Mini = ToDocument(state.Mini),
            };
        }

        return new CameraWindowsDocument { Version = CurrentVersion, Windows = windows };
    }

    private static PlacementDocument? ToDocument(CameraWindowPlacement? placement) =>
        placement is { } p ? new PlacementDocument { X = p.X, Y = p.Y, Width = p.Width, Height = p.Height } : null;

    private static CameraWindowState FromDocument(CameraWindowDocument window) => new(
        Enum.TryParse(window.Mode, ignoreCase: true, out CameraWindowMode mode) && Enum.IsDefined(mode) ? mode : CameraWindowMode.Normal,
        FromDocument(window.Normal),
        FromDocument(window.Mini));

    private static CameraWindowPlacement? FromDocument(PlacementDocument? document) =>
        document is not null && new CameraWindowPlacement(document.X, document.Y, document.Width, document.Height) is { IsValid: true } placement
            ? placement
            : null;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Camera window state was not loaded ({Reason}); defaults are used.")]
    private partial void LogFileIgnored(string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Camera window state could not be loaded; defaults are used.")]
    private partial void LogLoadFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Camera window state could not be saved.")]
    private partial void LogSaveFailed(Exception exception);
}

/// <summary>Remembers camera windows for this run only (no per-user data folder on this machine).</summary>
internal sealed class MemoryCameraWindowStateStore : ICameraWindowStateStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, CameraWindowState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _removed = new(StringComparer.OrdinalIgnoreCase);

    public Task LoadAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public CameraWindowState Get(string printerId)
    {
        lock (_gate)
        {
            return _states.GetValueOrDefault(printerId) ?? CameraWindowState.Default;
        }
    }

    public void Set(string printerId, CameraWindowState state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerId);
        ArgumentNullException.ThrowIfNull(state);
        lock (_gate)
        {
            _states[printerId] = state;
        }
    }

    public void Remove(string printerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerId);
        lock (_gate)
        {
            _states.Remove(printerId);
            _removed[printerId] = _removed.GetValueOrDefault(printerId) + 1;
        }
    }

    public int GetRemoveCount(string printerId)
    {
        lock (_gate)
        {
            return _removed.GetValueOrDefault(printerId);
        }
    }

    public Task<bool> WaitForPendingSaveAsync(TimeSpan timeout) => Task.FromResult(true);
}

internal sealed class CameraWindowsDocument
{
    public int Version { get; set; }

    public Dictionary<string, CameraWindowDocument?>? Windows { get; set; }
}

internal sealed class CameraWindowDocument
{
    public string? Mode { get; set; }

    public PlacementDocument? Normal { get; set; }

    public PlacementDocument? Mini { get; set; }
}

internal sealed class PlacementDocument
{
    public int X { get; set; }

    public int Y { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CameraWindowsDocument))]
internal sealed partial class CameraWindowsJsonContext : JsonSerializerContext;
