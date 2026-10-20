namespace PrusaTrayDiagnostic.Core.Images;

/// <summary>
/// Custom printer images (T-039), one PNG per printer id, in <c>printer-images/</c> next to <c>settings.json</c>. The
/// settings only say that a printer uses its custom image (<see cref="Printers.PrinterImageMode.Custom"/>); the file name
/// is derived from the id (<see cref="PrinterImageFileNames"/>), never from user input.
/// </summary>
/// <remarks>
/// Life cycle (T-039 decisions): a new import stays in memory until the settings are saved, then <see cref="SaveAsync"/>;
/// <see cref="DeleteAsync"/> after a saved removal of the printer or a saved switch away from the custom image;
/// <see cref="RemoveOrphansAsync"/> at startup.
/// </remarks>
public interface IPrinterImageStore
{
    /// <summary>Full path of the image folder (it may not exist yet).</summary>
    string DirectoryPath { get; }

    /// <summary>Full path where the image of <paramref name="printerId"/> is (or would be) stored.</summary>
    /// <exception cref="ArgumentException"><paramref name="printerId"/> is empty.</exception>
    string GetFilePath(string printerId);

    /// <summary>
    /// The stored image of <paramref name="printerId"/>, or <see langword="null"/> when there is none or the file is not a
    /// PNG of at most <see cref="PrinterImageFormats.MaxStoredBytes"/> (a foreign or damaged file is never handed out).
    /// </summary>
    /// <exception cref="IOException">The file exists but cannot be read.</exception>
    /// <exception cref="UnauthorizedAccessException">Access to the file was denied.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<byte[]?> ReadAsync(string printerId, CancellationToken cancellationToken);

    /// <summary>
    /// Stores <paramref name="png"/> as the image of <paramref name="printerId"/>, atomically: on failure or cancellation
    /// the previous image stays intact and no temporary file is left behind.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="png"/> is not a PNG of at most <see cref="PrinterImageFormats.MaxStoredBytes"/>.</exception>
    /// <exception cref="IOException">The image could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">Access to the image folder was denied.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task SaveAsync(string printerId, ReadOnlyMemory<byte> png, CancellationToken cancellationToken);

    /// <summary>Deletes the image of <paramref name="printerId"/>; <see langword="false"/> when there was none.</summary>
    /// <exception cref="IOException">The file exists but could not be deleted (e.g. it is open).</exception>
    /// <exception cref="UnauthorizedAccessException">Access to the file was denied.</exception>
    Task<bool> DeleteAsync(string printerId, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes every image that belongs to none of <paramref name="printerIds"/> (case-insensitive), and temporary files
    /// left by an interrupted save. Other files and subfolders are left alone. Best effort: a file that cannot be deleted
    /// is skipped. Call only with the printers of successfully loaded settings, never with the defaults used after a
    /// failed load, which would delete every image.
    /// </summary>
    /// <returns>How many files were deleted.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<int> RemoveOrphansAsync(IEnumerable<string> printerIds, CancellationToken cancellationToken);
}
