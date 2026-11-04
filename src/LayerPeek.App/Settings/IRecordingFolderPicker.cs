using Avalonia.Controls;
using Avalonia.Platform.Storage;

using LayerPeek.App.Resources;

namespace LayerPeek.App.Settings;

/// <summary>The folder the user chose in the folder dialog (T-146).</summary>
/// <param name="LocalPath">
/// The folder on this computer; <see langword="null"/> when the chosen item has no local path (a cloud or virtual location).
/// </param>
internal readonly record struct PickedFolder(string? LocalPath);

/// <summary>Lets the user choose the folder of the recordings (T-146). UI thread only.</summary>
internal interface IRecordingFolderPicker
{
    /// <summary>Shows the folder dialog; <see langword="null"/> when the user cancelled.</summary>
    /// <param name="startDirectory">The folder the dialog opens in; <see langword="null"/>: where the OS opens it.</param>
    Task<PickedFolder?> PickAsync(string? startDirectory);
}

/// <summary>The folder dialog of the window's <see cref="IStorageProvider"/> (native dialog on Windows and macOS, AOT safe).</summary>
internal sealed class StorageProviderRecordingFolderPicker(TopLevel owner) : IRecordingFolderPicker
{
    public async Task<PickedFolder?> PickAsync(string? startDirectory)
    {
        using IStorageFolder? start = startDirectory is not null
            ? await owner.StorageProvider.TryGetFolderFromPathAsync(startDirectory).ConfigureAwait(true)
            : null;
        IReadOnlyList<IStorageFolder> folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Strings.SettingsRecordingFolderPickerTitle,
            AllowMultiple = false,
            SuggestedStartLocation = start,
        }).ConfigureAwait(true);
        if (folders is not [IStorageFolder folder, ..])
        {
            return null;
        }

        using (folder)
        {
            return new PickedFolder(folder.TryGetLocalPath());
        }
    }
}
