using System.Text.Json;

using PrusaTrayDiagnostic.Core.Settings.Storage;

namespace PrusaTrayDiagnostic.Core.Settings;

/// <summary>
/// Stores <see cref="AppSettings"/> as <c>settings.json</c> in a directory (by default <see cref="AppDataPaths.GetDefaultDirectory"/>).
/// Thread-safe; saves are atomic (write to a temporary file, then replace).
/// Dispose only after all pending loads and saves have completed.
/// </summary>
public sealed class JsonSettingsStore : ISettingsStore, IDisposable
{
    public const string FileName = "settings.json";

    // Real settings are a few kB; anything this large is not a settings file and is not worth parsing.
    private const long MaxFileSizeBytes = 1024 * 1024;

    private const int BufferSize = 16 * 1024;

    private readonly string _directory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonSettingsStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = Path.GetFullPath(directory);
        FilePath = Path.Combine(_directory, FileName);
    }

    /// <summary>Full path of the settings file.</summary>
    public string FilePath { get; }

    /// <summary>Creates a store in the default per-user application data directory.</summary>
    public static JsonSettingsStore CreateDefault() => new(AppDataPaths.GetDefaultDirectory());

    public async Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        SettingsDocument document = SettingsMapper.ToDocument(settings);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SaveCoreAsync(document, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<string>> RemoveCameraCredentialsAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            byte[] content;
            try
            {
                FileInfo file = new(FilePath);
                if (!file.Exists || file.Length > MaxFileSizeBytes)
                {
                    return [];
                }

                content = await File.ReadAllBytesAsync(FilePath, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or IOException or UnauthorizedAccessException)
            {
                // Such a file is not loaded either; whatever is in it, it is not ours to change.
                return [];
            }

            List<string> changed = [];
            byte[]? cleaned;
            try
            {
                cleaned = CameraCredentialScrubber.Scrub(content, changed);
            }
            catch (JsonException)
            {
                return [];
            }

            if (cleaned is null)
            {
                return [];
            }

            await WriteAtomicallyAsync(cleaned, cancellationToken).ConfigureAwait(false);
            return changed;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private async Task<SettingsLoadResult> LoadCoreAsync(CancellationToken cancellationToken)
    {
        FileStream stream;
        try
        {
            // FileShare.Delete lets another process atomically replace the file while we read it.
            stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, BufferSize, useAsync: true);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return Fallback(SettingsLoadStatus.NotFound, errorMessage: null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fallback(SettingsLoadStatus.Unreadable, ex.Message);
        }

        SettingsDocument? document;
        await using (stream.ConfigureAwait(false))
        {
            try
            {
                if (stream.Length > MaxFileSizeBytes)
                {
                    return Fallback(SettingsLoadStatus.Corrupted, $"Settings file is larger than {MaxFileSizeBytes} bytes.");
                }

                document = await JsonSerializer.DeserializeAsync(stream, SettingsJsonContext.Default.SettingsDocument, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                return Fallback(SettingsLoadStatus.Corrupted, ex.Message);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Fallback(SettingsLoadStatus.Unreadable, ex.Message);
            }
        }

        if (document is null)
        {
            return Fallback(SettingsLoadStatus.Corrupted, "Settings file contains no settings object.");
        }

        List<string> warnings = [];
        AppSettings settings = SettingsMapper.ToSettings(document, warnings);
        return new SettingsLoadResult { Settings = settings, Status = SettingsLoadStatus.Loaded, Warnings = warnings };
    }

    private Task SaveCoreAsync(SettingsDocument document, CancellationToken cancellationToken) =>
        WriteAtomicallyAsync(
            (stream, token) => JsonSerializer.SerializeAsync(stream, document, SettingsJsonContext.Default.SettingsDocument, token),
            cancellationToken);

    private Task WriteAtomicallyAsync(byte[] content, CancellationToken cancellationToken) =>
        WriteAtomicallyAsync((stream, token) => stream.WriteAsync(content, token).AsTask(), cancellationToken);

    private async Task WriteAtomicallyAsync(Func<Stream, CancellationToken, Task> write, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_directory);

        // Unique name so that concurrent writers (e.g. a second app instance) never share a temporary file.
        string tempPath = $"{FilePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            FileStream stream = new(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
            await using (stream.ConfigureAwait(false))
            {
                await write(stream, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                // Data must be on disk before the rename, otherwise a crash could leave an empty settings file.
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(tempPath, FilePath, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private static SettingsLoadResult Fallback(SettingsLoadStatus status, string? errorMessage) =>
        new() { Settings = AppSettings.Default, Status = status, ErrorMessage = errorMessage };

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: a leftover temporary file is harmless and never read.
        }
    }
}
