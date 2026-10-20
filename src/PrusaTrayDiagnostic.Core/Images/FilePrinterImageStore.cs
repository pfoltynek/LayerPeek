using PrusaTrayDiagnostic.Core.Settings;

namespace PrusaTrayDiagnostic.Core.Images;

/// <summary>
/// <see cref="IPrinterImageStore"/> in <c>&lt;directory&gt;/printer-images/</c>, where the directory is the one of
/// <c>settings.json</c> (by default <see cref="AppDataPaths.GetDefaultDirectory"/>). Thread-safe: every operation is a
/// single file operation and saves replace the file atomically.
/// </summary>
public sealed class FilePrinterImageStore : IPrinterImageStore
{
    public const string DirectoryName = "printer-images";

    private const string TemporaryExtension = ".tmp";
    private const int BufferSize = 16 * 1024;

    /// <param name="settingsDirectory">The folder of <c>settings.json</c>.</param>
    public FilePrinterImageStore(string settingsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsDirectory);
        DirectoryPath = Path.Combine(Path.GetFullPath(settingsDirectory), DirectoryName);
    }

    public string DirectoryPath { get; }

    /// <summary>Creates a store next to the settings in the default per-user application data directory.</summary>
    public static FilePrinterImageStore CreateDefault() => new(AppDataPaths.GetDefaultDirectory());

    public string GetFilePath(string printerId) => Path.Combine(DirectoryPath, PrinterImageFileNames.For(printerId));

    public async Task<byte[]?> ReadAsync(string printerId, CancellationToken cancellationToken)
    {
        string path = GetFilePath(printerId);
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
            File.Move(tempPath, path, overwrite: true);
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

        File.Delete(path);
        return Task.FromResult(true);
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
