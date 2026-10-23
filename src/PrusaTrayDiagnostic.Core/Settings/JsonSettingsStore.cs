using System.Text.Json;

using PrusaTrayDiagnostic.Core.IO;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Settings.Storage;

namespace PrusaTrayDiagnostic.Core.Settings;

/// <summary>
/// Stores <see cref="AppSettings"/> as <c>settings.json</c> in a directory (by default <see cref="AppDataPaths.GetDefaultDirectory"/>).
/// Thread-safe; saves are atomic (write to a temporary file, then replace).
/// While another process holds the file open (antivirus, backup, an editor), reading it and replacing it are retried a
/// few times with short, growing delays (<see cref="RetryDelays"/>); other errors are not retried.
/// Dispose only after all pending loads and saves have completed.
/// </summary>
public sealed class JsonSettingsStore : ISettingsStore, IDisposable
{
    public const string FileName = "settings.json";

    // Real settings are a few kB; anything this large is not a settings file and is not worth parsing.
    private const long MaxFileSizeBytes = 1024 * 1024;

    private const int BufferSize = 16 * 1024;

    private readonly string _directory;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <param name="directory">Folder of <see cref="FileName"/>; created by the first save.</param>
    /// <param name="timeProvider">Clock of the waits between retries; <see cref="TimeProvider.System"/> by default.</param>
    public JsonSettingsStore(string directory, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = Path.GetFullPath(directory);
        FilePath = Path.Combine(_directory, FileName);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>The waits before the retries of a file held by another process, in order.</summary>
    internal static IReadOnlyList<TimeSpan> RetryDelays => FileRetry.Delays;

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
            byte[]? content;
            try
            {
                content = await ReadAllBytesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsPermanentReadFailure(ex))
            {
                // Such a file is not loaded either; whatever is in it, it is not ours to change. A file still held by
                // another process after the retries is thrown: the caller logs that the credentials stay on disk.
                return [];
            }

            if (content is null)
            {
                return [];
            }

            List<string> changed = [];
            byte[]? cleaned;
            try
            {
                cleaned = CameraCredentialScrubber.Scrub(content, changed);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                // Not syntactically valid JSON, or (InvalidOperationException) invalid UTF-8 in a string the scrubber
                // has to read: a property name, a printer id or a camera address. Invalid bytes in other values are
                // copied through unchanged.
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

    public async Task<bool> SetDetectedModelAsync(string printerId, PrinterModelCode code, string expectedAddress, ConnectionType expectedConnectionType, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerId);
        ArgumentNullException.ThrowIfNull(expectedAddress);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            byte[]? content;
            try
            {
                content = await ReadAllBytesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsPermanentReadFailure(ex))
            {
                // A file still held by another process after the retries is thrown instead: "nothing written" would hide
                // that the model was not saved.
                return false;
            }

            if (content is null)
            {
                return false;
            }

            byte[]? updated;
            try
            {
                updated = DetectedModelWriter.Write(content, printerId, code, expectedAddress, expectedConnectionType);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                // Not valid JSON, or invalid UTF-8 in a property name or printer id: not ours to change.
                return false;
            }

            if (updated is null)
            {
                return false;
            }

            await WriteAtomicallyAsync(updated, cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    /// <summary>
    /// The file content, or <see langword="null"/> when there is no file or it is too large. Opened like
    /// <see cref="LoadCoreAsync"/>, and the size is checked on the open stream, so it cannot change in between.
    /// </summary>
    /// <remarks>Retried while another process holds the file (<see cref="FileRetry"/>).</remarks>
    private Task<byte[]?> ReadAllBytesAsync(CancellationToken cancellationToken) =>
        FileRetry.RunAsync(static (store, token) => store.ReadAllBytesOnceAsync(token), this, replacing: false, _timeProvider, cancellationToken);

    private async Task<byte[]?> ReadAllBytesOnceAsync(CancellationToken cancellationToken)
    {
        FileStream? stream = OpenForReading();
        if (stream is null)
        {
            return null;
        }

        await using (stream.ConfigureAwait(false))
        {
            long length = stream.Length;
            if (length > MaxFileSizeBytes)
            {
                return null;
            }

            byte[] content = new byte[length];
            await stream.ReadExactlyAsync(content, cancellationToken).ConfigureAwait(false);
            return content;
        }
    }

    private async Task<SettingsLoadResult> LoadCoreAsync(CancellationToken cancellationToken)
    {
        StoredDocument stored;
        try
        {
            stored = await FileRetry.RunAsync(static (store, token) => store.ReadDocumentOnceAsync(token), this, replacing: false, _timeProvider, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            return Fallback(SettingsLoadStatus.Corrupted, ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Also a file still held by another process after the retries.
            return Fallback(SettingsLoadStatus.Unreadable, ex.Message);
        }

        if (stored.Status != SettingsLoadStatus.Loaded)
        {
            return stored.Status == SettingsLoadStatus.NotFound
                ? Fallback(SettingsLoadStatus.NotFound, errorMessage: null)
                : Fallback(SettingsLoadStatus.Corrupted, $"Settings file is larger than {MaxFileSizeBytes} bytes.");
        }

        SettingsDocument? document = stored.Document;
        if (document is null)
        {
            return Fallback(SettingsLoadStatus.Corrupted, "Settings file contains no settings object.");
        }

        List<string> warnings = [];
        AppSettings settings = SettingsMapper.ToSettings(document, warnings);
        string[] storedIds = [.. (document.Printers ?? []).Select(static entry => entry?.Id?.Trim()).OfType<string>().Where(static id => id.Length > 0)];
        return new SettingsLoadResult { Settings = settings, Status = SettingsLoadStatus.Loaded, Warnings = warnings, StoredPrinterIds = storedIds };
    }

    /// <summary>
    /// One attempt of <see cref="LoadCoreAsync"/>: <see cref="SettingsLoadStatus.NotFound"/> without a file,
    /// <see cref="SettingsLoadStatus.Corrupted"/> when it is too large, otherwise the parsed document.
    /// </summary>
    /// <exception cref="JsonException">The file is not valid JSON.</exception>
    private async Task<StoredDocument> ReadDocumentOnceAsync(CancellationToken cancellationToken)
    {
        FileStream? stream = OpenForReading();
        if (stream is null)
        {
            return new StoredDocument(SettingsLoadStatus.NotFound, Document: null);
        }

        await using (stream.ConfigureAwait(false))
        {
            if (stream.Length > MaxFileSizeBytes)
            {
                return new StoredDocument(SettingsLoadStatus.Corrupted, Document: null);
            }

            SettingsDocument? document = await JsonSerializer.DeserializeAsync(stream, SettingsJsonContext.Default.SettingsDocument, cancellationToken)
                .ConfigureAwait(false);
            return new StoredDocument(SettingsLoadStatus.Loaded, document);
        }
    }

    /// <summary>The settings file opened for reading, or <see langword="null"/> when it (or its folder) does not exist.</summary>
    private FileStream? OpenForReading()
    {
        try
        {
            // FileShare.Delete lets another process atomically replace the file while we read it.
            return new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, BufferSize, useAsync: true);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// A read error after which the file is left alone (no file to change). A file another process still holds after the
    /// retries is not: the change it was read for is lost unless the caller learns about it.
    /// </summary>
    private static bool IsPermanentReadFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException && !FileRetry.IsTransient(exception, replacing: false);

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

            // Only the replace is retried: the complete temporary file stays ready meanwhile. Windows refuses it while
            // another process has settings.json open without delete sharing (sharing violation or access denied).
            await FileRetry.RunAsync(
                static paths => File.Move(paths.Source, paths.Target, overwrite: true),
                (Source: tempPath, Target: FilePath),
                replacing: true,
                _timeProvider,
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    /// <summary>One read of the settings file: <see cref="SettingsLoadStatus.Loaded"/> carries the parsed document.</summary>
    private readonly record struct StoredDocument(SettingsLoadStatus Status, SettingsDocument? Document);

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
