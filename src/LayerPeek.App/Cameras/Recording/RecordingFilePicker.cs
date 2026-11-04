using Avalonia.Controls;
using Avalonia.Platform.Storage;

using Microsoft.Extensions.Logging;

namespace LayerPeek.App.Cameras.Recording;

/// <summary>
/// What the open dialog for a recording file asks (T-165: a timelapse for the JPEG export; T-164 reuses it for a recording).
/// </summary>
/// <param name="Title">Title (and macOS message) of the dialog, localized; says "experimental" where the feature is.</param>
/// <param name="FilterName">Name of the file type filter, localized (Windows shows it).</param>
/// <param name="Patterns">Name patterns of the filter, e.g. <c>*_timelapse.mp4</c> (Windows filters by them).</param>
/// <param name="Extension">The extension without the dot, e.g. <c>mp4</c> (macOS filters only by type).</param>
/// <param name="StartFolder">The folder the dialog opens in (an existing full path); <see langword="null"/>: where the OS opens it.</param>
internal sealed record RecordingFilePickRequest(string Title, string FilterName, IReadOnlyList<string> Patterns, string Extension, string? StartFolder);

/// <summary>
/// Lets the user choose one recording file in the OS open dialog, from the tray menu (T-165), where the app has no window of
/// its own. UI thread only.
/// </summary>
internal interface IRecordingFilePicker
{
    /// <summary>
    /// Shows the dialog and returns the full path of the chosen file; <see langword="null"/> when the user cancelled, a dialog
    /// of this picker is open already (it stays as it is), or the dialog failed (logged). Never throws.
    /// </summary>
    Task<string?> PickAsync(RecordingFilePickRequest request);
}

/// <summary>
/// The open dialog of Avalonia's <see cref="IStorageProvider"/> (native on Windows: <c>IFileOpenDialog</c>), owned by a
/// window that is never shown: the dialog needs a top level, a tray app has none. Used where no native picker of its own
/// exists (everything but macOS, see <c>MacRecordingFilePicker</c>, where Avalonia would attach the dialog as a sheet to
/// that invisible window).
/// </summary>
internal sealed partial class StorageProviderRecordingFilePicker(ILogger<StorageProviderRecordingFilePicker> logger) : IRecordingFilePicker
{
    private bool _open;

    public async Task<string?> PickAsync(RecordingFilePickRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_open)
        {
            return null;
        }

        _open = true;
        Window? owner = null;
        try
        {
            owner = new Window { ShowInTaskbar = false, Title = request.Title };
            IStorageProvider storage = owner.StorageProvider;
            using IStorageFolder? start = request.StartFolder is null ? null : await storage.TryGetFolderFromPathAsync(request.StartFolder).ConfigureAwait(true);
            FilePickerFileType type = new(request.FilterName)
            {
                Patterns = [.. request.Patterns],
                MimeTypes = ["video/mp4"],
                AppleUniformTypeIdentifiers = ["public.mpeg-4"],
            };
            IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = request.Title,
                AllowMultiple = false,
                SuggestedStartLocation = start,
                FileTypeFilter = [type],
            }).ConfigureAwait(true);
            if (files is not [IStorageFile file, ..])
            {
                return null;
            }

            using (file)
            {
                return file.TryGetLocalPath();
            }
        }
        catch (Exception ex)
        {
            LogPickFailed(ex);
            return null;
        }
        finally
        {
            owner?.Close();
            _open = false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The open dialog for a recording file failed.")]
    private partial void LogPickFailed(Exception exception);
}
