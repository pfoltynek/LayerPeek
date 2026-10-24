using Avalonia.Controls;
using Avalonia.Platform.Storage;

using LayerPeek.App.Resources;
using LayerPeek.Core.Images;

namespace LayerPeek.App.Settings;

/// <summary>Lets the user choose an image file for a custom printer image (T-039). UI thread only.</summary>
internal interface IImageFilePicker
{
    /// <summary>
    /// Shows the open dialog (PNG and JPG) and returns the content of the chosen file, at most
    /// <see cref="PrinterImageFormats.MaxSourceBytes"/> + 1 bytes (enough to tell that it is too large); <see langword="null"/>
    /// when the user cancelled. The caller disposes the stream.
    /// </summary>
    /// <param name="startDirectory">The folder the dialog opens in; <see langword="null"/>: where the OS opens it.</param>
    /// <exception cref="IOException">The chosen file cannot be read.</exception>
    /// <exception cref="UnauthorizedAccessException">Access to the chosen file was denied.</exception>
    Task<Stream?> PickAsync(string? startDirectory = null);
}

/// <summary>
/// The open dialog of the window's <see cref="IStorageProvider"/> (native dialog on Windows and macOS). The file is read
/// while it is still open (on macOS the sandbox access ends with the storage item), never more than the import limit.
/// </summary>
internal sealed class StorageProviderImageFilePicker(TopLevel owner) : IImageFilePicker
{
    public async Task<Stream?> PickAsync(string? startDirectory = null)
    {
        using IStorageFolder? start = startDirectory is null ? null : await owner.StorageProvider.TryGetFolderFromPathAsync(startDirectory).ConfigureAwait(true);
        FilePickerFileType images = new(Strings.SettingsImagePickerFilter)
        {
            Patterns = ["*.png", "*.jpg", "*.jpeg"],
            MimeTypes = ["image/png", "image/jpeg"],
            AppleUniformTypeIdentifiers = ["public.png", "public.jpeg"],
        };
        IReadOnlyList<IStorageFile> files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.SettingsImagePickerTitle,
            AllowMultiple = false,
            SuggestedStartLocation = start,
            FileTypeFilter = [images],
        }).ConfigureAwait(true);
        if (files is not [IStorageFile file, ..])
        {
            return null;
        }

        using (file)
        {
            Stream source = await file.OpenReadAsync().ConfigureAwait(true);
            await using (source.ConfigureAwait(true))
            {
                return await ReadLimitedAsync(source).ConfigureAwait(true);
            }
        }
    }

    /// <summary>At most one byte more than the import accepts, so that the importer can still report "too large".</summary>
    internal static async Task<MemoryStream> ReadLimitedAsync(Stream source)
    {
        MemoryStream copy = new();
        byte[] buffer = new byte[81920];
        int limit = PrinterImageFormats.MaxSourceBytes + 1;
        while (copy.Length < limit)
        {
            int read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, limit - copy.Length))).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            copy.Write(buffer, 0, read);
        }

        copy.Position = 0;
        return copy;
    }
}
