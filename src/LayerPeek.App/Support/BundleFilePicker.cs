using Avalonia.Controls;
using Avalonia.Platform.Storage;

using LayerPeek.App.Resources;

namespace LayerPeek.App.Support;

/// <summary>Lets the user choose where the diagnostic bundle goes (T-048). UI thread only.</summary>
internal interface IBundleFilePicker
{
    /// <summary>Shows the save dialog (ZIP); <see langword="null"/> when the user cancelled (then nothing is created).</summary>
    /// <param name="startDirectory">The folder the dialog opens in; <see langword="null"/>: where the OS opens it.</param>
    Task<IBundleTarget?> PickAsync(string suggestedFileName, string? startDirectory = null);
}

/// <summary>The chosen file of the bundle; disposing it releases the storage item (not the file).</summary>
internal interface IBundleTarget : IAsyncDisposable
{
    /// <summary>Opens the file for writing, empty.</summary>
    Task<Stream> OpenWriteAsync();

    /// <summary>Removes the file (a bundle that failed half-way).</summary>
    Task DeleteAsync();
}

/// <summary>The save dialog of the window's <see cref="IStorageProvider"/> (native dialog on Windows and macOS).</summary>
internal sealed class StorageProviderBundleFilePicker(TopLevel owner) : IBundleFilePicker
{
    public async Task<IBundleTarget?> PickAsync(string suggestedFileName, string? startDirectory = null)
    {
        IStorageFolder? start = startDirectory is null ? null : await owner.StorageProvider.TryGetFolderFromPathAsync(startDirectory).ConfigureAwait(true);
        FilePickerFileType zip = new(Strings.SupportBundleFileType)
        {
            Patterns = ["*.zip"],
            MimeTypes = ["application/zip"],
            AppleUniformTypeIdentifiers = ["public.zip-archive"],
        };
        IStorageFile? file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.SupportBundleDialogTitle,
            SuggestedFileName = suggestedFileName,
            SuggestedStartLocation = start,
            DefaultExtension = "zip",
            FileTypeChoices = [zip],
            ShowOverwritePrompt = true,
        }).ConfigureAwait(true);
        return file is null ? null : new StorageFileTarget(file);
    }

    private sealed class StorageFileTarget(IStorageFile file) : IBundleTarget
    {
        public Task<Stream> OpenWriteAsync() => file.OpenWriteAsync();

        public Task DeleteAsync() => file.DeleteAsync();

        public ValueTask DisposeAsync()
        {
            file.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
