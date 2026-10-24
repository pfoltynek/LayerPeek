using LayerPeek.App.Cameras;
using LayerPeek.App.Settings;

namespace LayerPeek.App.Windows;

/// <summary>
/// Creates a settings window with its view model (T-035): the window first, because its screens are the monitors offered
/// for the camera window. When creating or attaching the view model fails, the view model is disposed and the window
/// closed, so that neither is left behind. UI thread only.
/// </summary>
internal static class SettingsWindowFactory
{
    /// <param name="createWindow">The window (tests may count or observe it).</param>
    /// <param name="createViewModel">The view model for the monitors of the window.</param>
    public static (SettingsWindow Window, SettingsViewModel ViewModel) Create(
        Func<SettingsWindow> createWindow,
        Func<IScreenCatalog, SettingsViewModel> createViewModel)
    {
        ArgumentNullException.ThrowIfNull(createWindow);
        ArgumentNullException.ThrowIfNull(createViewModel);
        SettingsWindow window = createWindow();
        SettingsViewModel? viewModel = null;
        try
        {
            viewModel = createViewModel(new AvaloniaScreenCatalog(window));
            // The file dialog of custom printer images (T-039) belongs to this window.
            viewModel.ImageFilePicker = new StorageProviderImageFilePicker(window);
            window.DataContext = viewModel;
            return (window, viewModel);
        }
        catch
        {
            viewModel?.Dispose();
            window.Close();
            throw;
        }
    }
}
