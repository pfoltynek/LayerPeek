using System.Text.Json;
using System.Text.Json.Serialization;

using Avalonia;

using Microsoft.Extensions.Logging;

using LayerPeek.Core.IO;

namespace LayerPeek.App.Windows;

/// <summary>Remembers the size and maximized state of the settings and diagnostics windows between runs (T-094).</summary>
internal interface IAuxiliaryWindowStateStore
{
    /// <summary>Reads the remembered states once (call early, off the UI thread). Never throws for file problems.</summary>
    Task LoadAsync(CancellationToken cancellationToken);

    /// <summary>The remembered state, or <see cref="AuxiliaryWindowState.Default"/> (also while <see cref="LoadAsync"/> runs).</summary>
    AuxiliaryWindowState Get(string windowKey);

    /// <summary>
    /// Remembers <paramref name="state"/> and saves it in the background when it changed; never blocks and never throws
    /// for file problems.
    /// </summary>
    void Set(string windowKey, AuxiliaryWindowState state);

    /// <summary>Waits at most <paramref name="timeout"/> for background saves; <see langword="true"/> when none is running.</summary>
    Task<bool> WaitForPendingSaveAsync(TimeSpan timeout);
}

/// <summary>
/// <see cref="IAuxiliaryWindowStateStore"/> in <c>windows.json</c> next to the settings file, on the same principles as
/// <c>camera-windows.json</c>: it is app state, not a setting, so a settings save never overwrites it and vice versa.
/// Writes are atomic (temporary file, then replace) and run one after another on the thread pool; while another process
/// holds the file, reading and replacing it are retried briefly (<see cref="FileRetry"/>). A newer state replaces an older
/// one that has not been written yet. A damaged, too large or unknown-format (newer version) file is ignored (defaults)
/// and replaced by the next save. Creating the store touches no files. Thread-safe.
/// </summary>
internal sealed partial class JsonAuxiliaryWindowStateStore : IAuxiliaryWindowStateStore
{
    public const string FileName = "windows.json";

    /// <summary>A real file has well under a hundred bytes per window.</summary>
    private const long MaxFileSizeBytes = 64 * 1024;

    /// <summary>Only a few auxiliary windows exist; anything beyond comes from a damaged file.</summary>
    private const int MaxEntries = 16;

    private const int CurrentVersion = 1;

    private readonly string _directory;
    private readonly ILogger<JsonAuxiliaryWindowStateStore> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _gate = new();

    // Guarded by _gate.
    private readonly Dictionary<string, AuxiliaryWindowState> _states = new(StringComparer.OrdinalIgnoreCase);
    private Task? _loading;
    private Task _pendingSave = Task.CompletedTask;
    private long _saveVersion;

    public JsonAuxiliaryWindowStateStore(string directory, ILogger<JsonAuxiliaryWindowStateStore> logger, TimeProvider? timeProvider = null)
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

    public AuxiliaryWindowState Get(string windowKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(windowKey);
        lock (_gate)
        {
            return _states.GetValueOrDefault(windowKey) ?? AuxiliaryWindowState.Default;
        }
    }

    public void Set(string windowKey, AuxiliaryWindowState state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(windowKey);
        ArgumentNullException.ThrowIfNull(state);
        lock (_gate)
        {
            // Unchanged (a window that was only opened and closed): nothing to write.
            if ((_states.GetValueOrDefault(windowKey) ?? AuxiliaryWindowState.Default) == state)
            {
                return;
            }

            _states[windowKey] = state;
            long version = ++_saveVersion;
            _pendingSave = SaveAfterAsync(_pendingSave, version);
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
        WindowsDocument? document;
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

        if (document is null)
        {
            return;
        }

        if (document.Version != CurrentVersion)
        {
            // A newer app may mean something else by the same names; its file is replaced by the next save.
            LogFileIgnored(document.Version > CurrentVersion ? "a newer format" : "an unknown format");
            return;
        }

        if (document.Windows is not { } windows)
        {
            return;
        }

        lock (_gate)
        {
            // States set before the file was read are newer than the file: they win. The cap applies to unknown keys only,
            // so that a file full of them never crowds out the windows of this app.
            foreach ((string key, WindowDocument? window) in windows)
            {
                if (string.IsNullOrWhiteSpace(key) || window is null || _states.ContainsKey(key)
                    || (_states.Count >= MaxEntries && !IsKnownKey(key)))
                {
                    continue;
                }

                _states[key] = FromDocument(window);
            }
        }
    }

    private static bool IsKnownKey(string key) =>
        string.Equals(key, AuxiliaryWindowKeys.Settings, StringComparison.OrdinalIgnoreCase)
        || string.Equals(key, AuxiliaryWindowKeys.Diagnostics, StringComparison.OrdinalIgnoreCase);

    private static async Task<WindowsDocument?> ReadDocumentAsync(string path, CancellationToken cancellationToken)
    {
        FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 4096, useAsync: true);
        await using (stream.ConfigureAwait(false))
        {
            return await JsonSerializer.DeserializeAsync(stream, AuxiliaryWindowsJsonContext.Default.WindowsDocument, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SaveAfterAsync(Task previous, long version)
    {
        await previous.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ForceYielding);
        // The file is read first (once), so that a state set before the background load does not drop the other windows.
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
            content = JsonSerializer.SerializeToUtf8Bytes(ToDocument(), AuxiliaryWindowsJsonContext.Default.WindowsDocument);
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

    /// <summary>Under <c>_gate</c>; keys in a stable order, so that equal states give equal files.</summary>
    private WindowsDocument ToDocument()
    {
        Dictionary<string, WindowDocument?> windows = new(_states.Count, StringComparer.Ordinal);
        foreach ((string key, AuxiliaryWindowState state) in _states.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            windows[key] = new WindowDocument
            {
                Width = state.ClientSize?.Width,
                Height = state.ClientSize?.Height,
                Maximized = state.Maximized,
            };
        }

        return new WindowsDocument { Version = CurrentVersion, Windows = windows };
    }

    /// <summary>Unusable sizes (missing, not finite, not positive, far too large) mean "never resized".</summary>
    private static AuxiliaryWindowState FromDocument(WindowDocument window)
    {
        Size? size = window is { Width: { } width, Height: { } height } && AuxiliaryWindowPlacement.IsUsable(new Size(width, height))
            ? new Size(width, height)
            : null;
        return new AuxiliaryWindowState(size, window.Maximized);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Window state was not loaded ({Reason}); defaults are used.")]
    private partial void LogFileIgnored(string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Window state could not be loaded; defaults are used.")]
    private partial void LogLoadFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Window state could not be saved.")]
    private partial void LogSaveFailed(Exception exception);
}

/// <summary>Remembers the auxiliary windows for this run only (no per-user data folder on this machine; tests).</summary>
internal sealed class MemoryAuxiliaryWindowStateStore : IAuxiliaryWindowStateStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, AuxiliaryWindowState> _states = new(StringComparer.OrdinalIgnoreCase);

    public Task LoadAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public AuxiliaryWindowState Get(string windowKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(windowKey);
        lock (_gate)
        {
            return _states.GetValueOrDefault(windowKey) ?? AuxiliaryWindowState.Default;
        }
    }

    public void Set(string windowKey, AuxiliaryWindowState state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(windowKey);
        ArgumentNullException.ThrowIfNull(state);
        lock (_gate)
        {
            _states[windowKey] = state;
        }
    }

    public Task<bool> WaitForPendingSaveAsync(TimeSpan timeout) => Task.FromResult(true);
}

internal sealed class WindowsDocument
{
    public int Version { get; set; }

    public Dictionary<string, WindowDocument?>? Windows { get; set; }
}

internal sealed class WindowDocument
{
    /// <summary>Client width in DIP of the normal (not maximized) window; absent: never resized by the user.</summary>
    public double? Width { get; set; }

    /// <summary>Client height in DIP of the normal (not maximized) window; absent: never resized by the user.</summary>
    public double? Height { get; set; }

    public bool Maximized { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    NumberHandling = JsonNumberHandling.Strict)]
[JsonSerializable(typeof(WindowsDocument))]
internal sealed partial class AuxiliaryWindowsJsonContext : JsonSerializerContext;
