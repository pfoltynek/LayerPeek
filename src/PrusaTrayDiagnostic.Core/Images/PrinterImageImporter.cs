namespace PrusaTrayDiagnostic.Core.Images;

public enum PrinterImageImportStatus
{
    /// <summary><see cref="PrinterImageImportResult.Png"/> holds the image to store.</summary>
    Imported,

    /// <summary>The file does not exist (any more).</summary>
    NotFound,

    /// <summary>The file could not be read (access denied, locked, I/O error).</summary>
    Unreadable,

    /// <summary>Larger than <see cref="PrinterImageFormats.MaxSourceBytes"/>.</summary>
    TooLarge,

    /// <summary>Neither PNG nor JPEG by its signature (whatever the file name says), including an empty file.</summary>
    UnsupportedFormat,

    /// <summary>A PNG/JPEG signature, but the image cannot be decoded (corrupt or truncated).</summary>
    Undecodable,
}

/// <summary>Outcome of <see cref="PrinterImageImporter"/>.</summary>
/// <param name="Png">The converted image (PNG, at most <see cref="PrinterImageFormats.MaxPixelSize"/> px) for <see cref="PrinterImageImportStatus.Imported"/>.</param>
public sealed record PrinterImageImportResult(PrinterImageImportStatus Status, byte[]? Png = null);

/// <summary>
/// Imports a custom printer image chosen by the user: checks the size and the signature, converts it with
/// <see cref="IPrinterImageProcessor"/> and checks the result. Nothing is written: the caller keeps the PNG in memory
/// until the settings are saved (so that Cancel simply drops it) and then stores it with
/// <see cref="IPrinterImageStore.SaveAsync"/>. Nothing is logged, so the user's path never reaches a log.
/// </summary>
public sealed class PrinterImageImporter(IPrinterImageProcessor processor)
{
    private const int ChunkSize = 81920;

    private readonly IPrinterImageProcessor _processor = processor ?? throw new ArgumentNullException(nameof(processor));

    /// <summary>Imports the file at <paramref name="path"/> (see <see cref="ImportAsync(Stream, CancellationToken)"/>).</summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public async Task<PrinterImageImportResult> ImportFileAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        FileStream stream;
        try
        {
            stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, ChunkSize, useAsync: true);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new PrinterImageImportResult(PrinterImageImportStatus.NotFound);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // ArgumentException / NotSupportedException: a path the OS cannot open (e.g. invalid characters).
            return new PrinterImageImportResult(PrinterImageImportStatus.Unreadable);
        }

        await using (stream.ConfigureAwait(false))
        {
            return await ImportAsync(stream, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Reads at most <see cref="PrinterImageFormats.MaxSourceBytes"/> + 1 bytes of <paramref name="source"/> (e.g. a file
    /// picked through the Avalonia storage provider), validates and converts it. The stream is not disposed.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public async Task<PrinterImageImportResult> ImportAsync(Stream source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        byte[]? content;
        try
        {
            content = await ReadLimitedAsync(source, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ObjectDisposedException)
        {
            return new PrinterImageImportResult(PrinterImageImportStatus.Unreadable);
        }

        if (content is null)
        {
            return new PrinterImageImportResult(PrinterImageImportStatus.TooLarge);
        }

        if (PrinterImageFormats.Detect(content) is not { } format)
        {
            return new PrinterImageImportResult(PrinterImageImportStatus.UnsupportedFormat);
        }

        byte[] png;
        try
        {
            png = await _processor.ConvertToPngAsync(content, format, PrinterImageFormats.MaxPixelSize, cancellationToken).ConfigureAwait(false);
        }
        catch (PrinterImageDecodeException)
        {
            return new PrinterImageImportResult(PrinterImageImportStatus.Undecodable);
        }

        // Defensive: whatever the processor returned must be storable.
        return png is { Length: > 0 and <= PrinterImageFormats.MaxStoredBytes } && PrinterImageFormats.Detect(png) == PrinterImageFormat.Png
            ? new PrinterImageImportResult(PrinterImageImportStatus.Imported, png)
            : new PrinterImageImportResult(PrinterImageImportStatus.Undecodable);
    }

    /// <summary>The whole content, or <see langword="null"/> when it is larger than the limit (reading stops there).</summary>
    private static async Task<byte[]?> ReadLimitedAsync(Stream source, CancellationToken cancellationToken)
    {
        if (source.CanSeek && source.Length - source.Position > PrinterImageFormats.MaxSourceBytes)
        {
            return null;
        }

        using MemoryStream buffer = new();
        byte[] chunk = new byte[ChunkSize];
        while (true)
        {
            int read = await source.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return buffer.ToArray();
            }

            if (buffer.Length + read > PrinterImageFormats.MaxSourceBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }
    }
}
