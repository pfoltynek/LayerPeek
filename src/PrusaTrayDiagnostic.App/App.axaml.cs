using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using PrusaTrayDiagnostic.App.Composition;
using PrusaTrayDiagnostic.App.Diagnostics;
using PrusaTrayDiagnostic.App.Platform;
using PrusaTrayDiagnostic.App.Platform.Windows;
using PrusaTrayDiagnostic.App.Settings;
using PrusaTrayDiagnostic.App.SingleInstance;
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

    // Commands of later starts of the app and the macOS reopen event (T-043); they wait here until the host runs.
    private readonly InstanceCommandQueue _instanceCommands;

    // Every auxiliary window passes through it (taskbar properties, most recently used window; T-043). Set with the host.
    private AuxiliaryWindowTracker? _windowTracker;
    private AuxiliaryWindowFactory? _windows;
    private InstanceActivation? _activation;

    // The OS ended the session (desktop Exit) before the host finished starting.
    private bool _exited;

    /// <summary>Designer and tests: commands of later starts are not received.</summary>
    public App()
        : this(new InstanceCommandQueue())
    {
    }

    /// <param name="instanceCommands">Filled by the single-instance server of <c>Program.Main</c> (T-043).</param>
    internal App(InstanceCommandQueue instanceCommands)
    {
        ArgumentNullException.ThrowIfNull(instanceCommands);
        _instanceCommands = instanceCommands;
    }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && !Design.IsDesignMode)
        {
            TrayIcon? trayIcon = TrayIcon.GetIcons(this) is [TrayIcon first, ..] ? first : null;
            desktop.Exit += OnDesktopExit;
            // macOS: clicking the app (Finder, Dock, Launchpad) while it runs; treated like a second start (T-043).
            if (this.TryGetFeature<IActivatableLifetime>() is { } activatable)
            {
                activatable.Activated += OnLifetimeActivated;
            }

            StartHost(desktop, trayIcon);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnLifetimeActivated(object? sender, ActivatedEventArgs e)
    {
        if (e.Kind == ActivationKind.Reopen)
        {
            _instanceCommands.Post(InstanceCommand.Activate);
        }
    }

    /// <summary>UI thread: a later start of the app (or a reopen on macOS) asked for it (T-043).</summary>
    private void OnInstanceCommand(InstanceCommand command)
    {
        if (command == InstanceCommand.Activate && _quit is not { IsQuitting: true })
        {
            _activation?.Activate();
        }
    }

    private static IWindowShell CreateWindowShell(TrayHost host) =>
        OperatingSystem.IsWindows() ? new WindowsWindowShell(host.CreateLogger<WindowsWindowShell>()) : NoWindowShell.Instance;

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

        _windowTracker = new AuxiliaryWindowTracker(CreateWindowShell(host));
        _windows = new AuxiliaryWindowFactory(_windowTracker.Track);
        _activation = new InstanceActivation(_windowTracker.Recent, () => ((ITrayAppCommands)this).ShowSettings());
        _host = host;
        _quit = new QuitCoordinator(StopHostAsync, () => desktop.Shutdown(), QuitTimeout, QuitGrace, host.CreateLogger<QuitCoordinator>());
        host.LanguageChanged += OnLanguageChanged;
        // Last: starts that came while the settings were loading are handled now.
        _instanceCommands.Attach(OnInstanceCommand, new AvaloniaUiDispatcher(), host.CreateLogger<InstanceCommandQueue>());
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
        if (_host is not { } host || _windows is not { } windows || _quit is { IsQuitting: true })
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
                    return (windows.Diagnostics(viewModel), viewModel);
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
        if (_host is not { } host || _windows is not { } windows || _quit is { IsQuitting: true })
        {
            return;
        }

        SettingsViewModel? created = null;
        // "Apply" places the open camera windows again (T-040).
        CameraWindows cameraWindows = CameraWindowsFor(host);
        _settingsWindow.ShowOrActivate(() =>
        {
            // One editing session per window: the slot disposes the view model (cancelling a running test) when it closes.
            // The window first: its screens are the monitors offered for the camera window (T-035); a failure leaves nothing open.
            (IAuxiliaryWindow window, SettingsViewModel viewModel) = windows.Settings(screens => host.CreateSettingsViewModel(screens, cameraWindows));
            created = viewModel;
            return (window, viewModel);
        });

        // Loads after the window is shown (reading the file and the credential store runs off the UI thread); never throws.
        _ = created?.LoadAsync();
    }

    void ITrayAppCommands.ToggleCamera(string printerId)
    {
        if (_host is not { } host || _quit is { IsQuitting: true })
        {
            return;
        }

        CameraWindowsFor(host).Toggle(printerId);
    }

    /// <summary>
    /// The camera windows, created on first use; the tray menu follows which of them are open (T-040: checked "Camera"
    /// items).
    /// </summary>
    private CameraWindows CameraWindowsFor(TrayHost host)
    {
        if (_cameraWindows is null)
        {
            _cameraWindows = (_windows ?? new AuxiliaryWindowFactory(null)).CameraWindows(host.CreateCameraViewModel);
            _cameraWindows.OpenChanged += (_, e) => host.OnCameraWindowOpenChanged(e.PrinterId, e.IsOpen);
        }

        return _cameraWindows;
    }

    void ITrayAppCommands.ShowAbout()
    {
        if (_host is not { } host || _windows is not { } windows || _quit is { IsQuitting: true })
        {
            return;
        }

        _aboutWindow.ShowOrActivate(() => (windows.About(new AboutViewModel(host.UrlLauncher)), null));
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
        // Later starts no longer open anything (they still exit: the server runs until the process ends).
        _instanceCommands.Close();
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
        _instanceCommands.Close();

        // Exit initiated by the OS (log off, shutdown): only request the stop, the process ends right after.
        if (_quit is not { IsQuitting: true })
        {
            _host?.RequestStop();
        }

        // Last: quitting may still have logged a stop failure or timeout just before the shutdown.
        _host?.ReleaseLogging();
    }
}
