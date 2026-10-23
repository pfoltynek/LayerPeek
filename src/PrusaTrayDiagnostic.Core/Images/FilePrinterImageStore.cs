using PrusaTrayDiagnostic.Core.IO;
using PrusaTrayDiagnostic.Core.Settings;

namespace PrusaTrayDiagnostic.Core.Images;

/// <summary>
/// <see cref="IPrinterImageStore"/> in <c>&lt;directory&gt;/printer-images/</c>, where the directory is the one of
/// <c>settings.json</c> (by default <see cref="AppDataPaths.GetDefaultDirectory"/>). Thread-safe: every operation is a
/// single file operation and saves replace the file atomically. While another process holds an image open (antivirus,
/// backup), reading, replacing and deleting it are retried a few times with short, growing delays (<see cref="FileRetry"/>).
/// </summary>
public sealed class FilePrinterImageStore : IPrinterImageStore
{
    public const string DirectoryName = "printer-images";

    private const string TemporaryExtension = ".tmp";
    private const int BufferSize = 16 * 1024;

    private readonly TimeProvider _timeProvider;

    /// <param name="settingsDirectory">The folder of <c>settings.json</c>.</param>
    /// <param name="timeProvider">Clock of the waits between retries; <see cref="TimeProvider.System"/> by default.</param>
    public FilePrinterImageStore(string settingsDirectory, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsDirectory);
        DirectoryPath = Path.Combine(Path.GetFullPath(settingsDirectory), DirectoryName);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public string DirectoryPath { get; }

    /// <summary>Creates a store next to the settings in the default per-user application data directory.</summary>
    public static FilePrinterImageStore CreateDefault() => new(AppDataPaths.GetDefaultDirectory());

    public string GetFilePath(string printerId) => Path.Combine(DirectoryPath, PrinterImageFileNames.For(printerId));

    public Task<byte[]?> ReadAsync(string printerId, CancellationToken cancellationToken) =>
        FileRetry.RunAsync(static (path, token) => ReadOnceAsync(path, token), GetFilePath(printerId), replacing: false, _timeProvider, cancellationToken);

    private static async Task<byte[]?> ReadOnceAsync(string path, CancellationToken cancellationToken)
    {
        FileStream stream;
        try
        {
            // FileShare.Delete: a concurrent save may replace the file while it is read.
            stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, BufferSize, useAsync: true);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }

        await using (stream.ConfigureAwait(false))
        {
            long length = stream.Length;
            if (length is 0 or > PrinterImageFormats.MaxStoredBytes)
            {
                return null;
            }

            byte[] content = new byte[length];
            await stream.ReadExactlyAsync(content, cancellationToken).ConfigureAwait(false);
            return PrinterImageFormats.Detect(content) == PrinterImageFormat.Png ? content : null;
        }
    }

    public async Task SaveAsync(string printerId, ReadOnlyMemory<byte> png, CancellationToken cancellationToken)
    {
        string path = GetFilePath(printerId);
        if (png.Length > PrinterImageFormats.MaxStoredBytes || PrinterImageFormats.Detect(png.Span) != PrinterImageFormat.Png)
        {
            throw new ArgumentException($"A PNG image of at most {PrinterImageFormats.MaxStoredBytes} bytes is required.", nameof(png));
        }

        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(DirectoryPath);

        // Unique name so that concurrent writers (e.g. a second app instance) never share a temporary file.
        string tempPath = $"{path}.{Guid.NewGuid():N}{TemporaryExtension}";
        try
        {
            FileStream stream = new(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
            await using (stream.ConfigureAwait(false))
            {
                await stream.WriteAsync(png, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                // Data must be on disk before the rename, otherwise a crash could leave an empty image.
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            // Only the replace is retried: the complete temporary file stays ready meanwhile.
            await FileRetry.RunAsync(
                static paths => File.Move(paths.Source, paths.Target, overwrite: true),
                (Source: tempPath, Target: path),
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

    public Task<bool> DeleteAsync(string printerId, CancellationToken cancellationToken)
    {
        string path = GetFilePath(printerId);
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(path))
        {
            return Task.FromResult(false);
        }

        return DeleteExistingAsync(path, cancellationToken);
    }

    private async Task<bool> DeleteExistingAsync(string path, CancellationToken cancellationToken)
    {
        // Windows refuses to delete a file another process holds open without delete sharing.
        await FileRetry.RunAsync(static target => File.Delete(target), path, replacing: true, _timeProvider, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public Task<int> RemoveOrphansAsync(IEnumerable<string> printerIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(printerIds);
        HashSet<string> keep = new(StringComparer.OrdinalIgnoreCase);
        foreach (string id in printerIds)
        {
            keep.Add(PrinterImageFileNames.For(id));
        }

        string[] files;
        try
        {
            files = Directory.GetFiles(DirectoryPath);
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(0);
        }

        int deleted = 0;
        foreach (string file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name = Path.GetFileName(file);
            bool orphan = name.EndsWith(TemporaryExtension, StringComparison.OrdinalIgnoreCase)
                || (name.EndsWith(PrinterImageFileNames.Extension, StringComparison.OrdinalIgnoreCase) && !keep.Contains(name));
            if (orphan && TryDelete(file))
            {
                deleted++;
            }
        }

        return Task.FromResult(deleted);
    }

    private static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: a leftover file is never read as an image and is retried at the next startup.
            return false;
        }
    }
}
