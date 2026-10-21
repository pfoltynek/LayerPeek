using Avalonia.Controls;

using PrusaTrayDiagnostic.App.Cameras;
using PrusaTrayDiagnostic.App.Diagnostics;
using PrusaTrayDiagnostic.App.Settings;
using PrusaTrayDiagnostic.Core.Printers;

namespace PrusaTrayDiagnostic.App.Windows;

/// <summary>
/// Creates the auxiliary windows of the tray commands, each tracked before it is shown (T-043: taskbar properties and the
/// most recently used window for a second start of the app). UI thread only.
/// </summary>
internal sealed class AuxiliaryWindowFactory
{
    private readonly Func<Window, IDisposable>? _track;

    /// <param name="track">Normally <see cref="AuxiliaryWindowTracker.Track"/>; <see langword="null"/>: nothing is tracked.</param>
    public AuxiliaryWindowFactory(Func<Window, IDisposable>? track) => _track = track;

    /// <summary>A diagnostics window showing <paramref name="viewModel"/> (owned by the caller until the slot takes it).</summary>
    public IAuxiliaryWindow Diagnostics(DiagnosticsViewModel? viewModel) =>
        new AvaloniaAuxiliaryWindow(new DiagnosticsWindow { DataContext = viewModel }, _track);

    /// <summary>A settings window with its view model (<see cref="SettingsWindowFactory"/>).</summary>
    public (IAuxiliaryWindow Window, SettingsViewModel ViewModel) Settings(
        Func<IScreenCatalog, SettingsViewModel> createViewModel,
        Func<SettingsWindow>? createWindow = null)
    {
        (SettingsWindow window, SettingsViewModel viewModel) = SettingsWindowFactory.Create(createWindow ?? (static () => new SettingsWindow()), createViewModel);
        return (new AvaloniaAuxiliaryWindow(window, _track), viewModel);
    }

    /// <summary>The About window; the licences window it opens is tracked as well.</summary>
    public IAuxiliaryWindow About(AboutViewModel viewModel) =>
        new AvaloniaAuxiliaryWindow(new AboutWindow { DataContext = viewModel, TrackChild = _track }, _track);

    /// <summary>The camera windows of all printers.</summary>
    public CameraWindows CameraWindows(
        Func<string, PrinterDefinition?, CameraViewModel?> createViewModel,
        IScreenCatalog? screens = null,
        Func<CameraWindow, WindowChrome?>? measureChrome = null) =>
        new(createViewModel, screens, measureChrome, _track);
}
