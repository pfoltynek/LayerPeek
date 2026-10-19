using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using PrusaTrayDiagnostic.App.Cameras;
using PrusaTrayDiagnostic.App.Composition;
using PrusaTrayDiagnostic.App.Diagnostics;
using PrusaTrayDiagnostic.App.Settings;
using PrusaTrayDiagnostic.App.Tray;
using PrusaTrayDiagnostic.App.Tray.Menu;
using PrusaTrayDiagnostic.App.Windows;

namespace PrusaTrayDiagnostic.App;

public partial class App : Application, ITrayAppCommands
{
    /// <summary>Quitting never waits longer than this for printer clients to stop.</summary>
    private static readonly TimeSpan QuitTimeout = TimeSpan.FromSeconds(3);

    /// <summary>Extra time for releasing services after <see cref="QuitTimeout"/>; then the app exits regardless.</summary>
    private static readonly TimeSpan QuitGrace = TimeSpan.FromSeconds(2);

    // One instance each; closing a window never quits the app (ShutdownMode.OnExplicitShutdown).
    private readonly AuxiliaryWindowSlot _aboutWindow = new();
    private readonly AuxiliaryWindowSlot _diagnosticsWindow = new();
    private readonly AuxiliaryWindowSlot _settingsWindow = new();

    // One camera window per printer (T-014), created once the host runs; windows of different printers may be open together.
    private CameraWindows? _cameraWindows;
    private TrayHost? _host;
    private QuitCoordinator? _quit;

    // The OS ended the session (desktop Exit) before the host finished starting.
    private bool _exited;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && !Design.IsDesignMode)
        {
            TrayIcon? trayIcon = TrayIcon.GetIcons(this) is [TrayIcon first, ..] ? first : null;
            desktop.Exit += OnDesktopExit;
            StartHost(desktop, trayIcon);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// The settings are loaded before the tray gets its menu (milliseconds, with the static icon from App.axaml
    /// meanwhile), so that every text starts in the saved language. async void: a startup failure crashes the app as
    /// before instead of leaving it silently without a menu.
    /// </summary>
    private async void StartHost(IClassicDesktopStyleApplicationLifetime desktop, TrayIcon? trayIcon)
    {
        TrayHost host = await TrayHost.StartAsync(trayIcon, this).ConfigureAwait(true);
        if (_exited)
        {
            // The OS ended the session while the settings were loading.
            host.RequestStop();
            host.ReleaseLogging();
            return;
        }

        _host = host;
        _quit = new QuitCoordinator(StopHostAsync, () => desktop.Shutdown(), QuitTimeout, QuitGrace, host.CreateLogger<QuitCoordinator>());
        host.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>The tray already switched; open windows format the texts of their view models again.</summary>
    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        _settingsWindow.RefreshTexts();
        _diagnosticsWindow.RefreshTexts();
        _aboutWindow.RefreshTexts();
        _cameraWindows?.RefreshTexts();
    }

    void ITrayAppCommands.ShowDiagnostics(string? printerId)
    {
        if (_host is not { } host || _quit is { IsQuitting: true })
        {
            return;
        }

        _diagnosticsWindow.ShowOrActivate(
            () =>
            {
                // Live data only while the window is open: the slot disposes the view model when the window closes.
                DiagnosticsViewModel viewModel = host.CreateDiagnosticsViewModel(printerId);
                try
                {
                    return (new AvaloniaAuxiliaryWindow(new DiagnosticsWindow { DataContext = viewModel }), viewModel);
                }
                catch
                {
                    viewModel.Dispose();
                    throw;
                }
            },
            owned =>
            {
                // Already open: switch to the printer whose submenu was used; the general item keeps the current one.
                if (printerId is not null && owned is DiagnosticsViewModel viewModel)
                {
                    viewModel.ShowPrinter(printerId);
                }
            });
    }

    void ITrayAppCommands.ShowSettings()
    {
        if (_host is not { } host || _quit is { IsQuitting: true })
        {
            return;
        }

        SettingsViewModel? created = null;
        _settingsWindow.ShowOrActivate(() =>
        {
            // One editing session per window: the slot disposes the view model (cancelling a running test) when it closes.
            // The window first: its screens are the monitors offered for the camera window (T-035).
            SettingsWindow window = new();
            SettingsViewModel viewModel = host.CreateSettingsViewModel(new AvaloniaScreenCatalog(window));
            try
            {
                created = viewModel;
                window.DataContext = viewModel;
                return (new AvaloniaAuxiliaryWindow(window), viewModel);
            }
            catch
            {
                viewModel.Dispose();
                throw;
            }
        });

        // Loads after the window is shown (reading the file and the credential store runs off the UI thread); never throws.
        _ = created?.LoadAsync();
    }

    void ITrayAppCommands.ShowCamera(string printerId)
    {
        if (_host is not { } host || _quit is { IsQuitting: true })
        {
            return;
        }

        _cameraWindows ??= new CameraWindows(host.CreateCameraViewModel);
        _cameraWindows.Show(printerId);
    }

    void ITrayAppCommands.ShowAbout()
    {
        if (_host is not { } host || _quit is { IsQuitting: true })
        {
            return;
        }

        _aboutWindow.ShowOrActivate(() => (new AvaloniaAuxiliaryWindow(new AboutWindow { DataContext = new AboutViewModel(host.UrlLauncher) }), null));
    }

    async void ITrayAppCommands.Quit()
    {
        if (_quit is not null)
        {
            // Never throws: shutdown is requested even if stopping fails or hangs.
            await _quit.QuitAsync();
        }
    }

    private Task StopHostAsync(TimeSpan timeout)
    {
        // Auxiliary windows go first so that nothing is left on screen while monitoring stops (unsaved settings are
        // discarded, a running connection test is cancelled); the diagnostics view
        // model unsubscribes from the monitor before the services are released.
        _settingsWindow.Shutdown();
        _diagnosticsWindow.Shutdown();
        // Stops the streams; the window state is saved and awaited by the host below.
        _cameraWindows?.Shutdown();

        _aboutWindow.Shutdown();
        return _host?.StopAsync(timeout) ?? Task.CompletedTask;
    }

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        _exited = true;

        // Exit initiated by the OS (log off, shutdown): only request the stop, the process ends right after.
        if (_quit is not { IsQuitting: true })
        {
            _host?.RequestStop();
        }

        // Last: quitting may still have logged a stop failure or timeout just before the shutdown.
        _host?.ReleaseLogging();
    }
}
