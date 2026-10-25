using Avalonia.Controls;

using LayerPeek.App.Cameras;
using LayerPeek.App.Diagnostics;
using LayerPeek.App.Settings;
using LayerPeek.Core.Printers;

namespace LayerPeek.App.Windows;

/// <summary>
/// Creates the auxiliary windows of the tray commands, each tracked before it is shown (T-043: taskbar properties and the
/// most recently used window for a second start of the app). UI thread only.
/// </summary>
internal sealed class AuxiliaryWindowFactory
{
    private readonly Func<Window, IDisposable>? _track;
    private readonly IAuxiliaryWindowStateStore? _windowStates;

    /// <param name="track">Normally <see cref="AuxiliaryWindowTracker.Track"/>; <see langword="null"/>: nothing is tracked.</param>
    /// <param name="windowStates">
    /// Where the settings and diagnostics windows remember their size and maximized state (T-094, <c>windows.json</c>);
    /// <see langword="null"/>: they always open at the designed size.
    /// </param>
    public AuxiliaryWindowFactory(Func<Window, IDisposable>? track, IAuxiliaryWindowStateStore? windowStates = null)
    {
        _track = track;
        _windowStates = windowStates;
    }

    /// <summary>A diagnostics window showing <paramref name="viewModel"/> (owned by the caller until the slot takes it).</summary>
    public IAuxiliaryWindow Diagnostics(DiagnosticsViewModel? viewModel)
    {
        DiagnosticsWindow window = new() { DataContext = viewModel };
        if (_windowStates is not null)
        {
            window.RememberSizeIn(_windowStates);
        }

        return new AvaloniaAuxiliaryWindow(window, _track);
    }

    /// <summary>A settings window with its view model (<see cref="SettingsWindowFactory"/>).</summary>
    public (IAuxiliaryWindow Window, SettingsViewModel ViewModel) Settings(
        Func<IScreenCatalog, SettingsViewModel> createViewModel,
        Func<SettingsWindow>? createWindow = null)
    {
        (SettingsWindow window, SettingsViewModel viewModel) = SettingsWindowFactory.Create(createWindow ?? (static () => new SettingsWindow()), createViewModel);
        if (_windowStates is not null)
        {
            window.RememberSizeIn(_windowStates);
        }

        return (new AvaloniaAuxiliaryWindow(window, _track), viewModel);
    }

    /// <summary>The About window; the licences window it opens is tracked as well.</summary>
    public IAuxiliaryWindow About(AboutViewModel viewModel) =>
        new AvaloniaAuxiliaryWindow(new AboutWindow { DataContext = viewModel, TrackChild = _track }, _track);

    /// <summary>The camera windows of all printers.</summary>
    public CameraWindows CameraWindows(
        Func<string, PrinterDefinition?, CameraViewModel?> createViewModel,
        IScreenCatalog? screens = null,
        Func<CameraWindow, WindowChrome?>? measureChrome = null,
        Func<CameraWindow, double, WindowChrome?>? predictChrome = null) =>
        new(createViewModel, screens, measureChrome, _track, predictChrome);
}
