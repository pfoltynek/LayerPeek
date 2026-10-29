using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using LayerPeek.App.Composition;
using LayerPeek.App.Diagnostics;
using LayerPeek.App.Logging;
using LayerPeek.App.Platform;
using LayerPeek.App.Platform.Windows;
using LayerPeek.App.Settings;
using LayerPeek.App.SingleInstance;
using LayerPeek.App.Tray;
using LayerPeek.App.Tray.Menu;
using LayerPeek.App.Windows;

namespace LayerPeek.App;

public partial class App : Application, ITrayAppCommands
{
    /// <summary>Quitting never waits longer than this for printer clients to stop.</summary>
    private static readonly TimeSpan QuitTimeout = TimeSpan.FromSeconds(3);

    /// <summary>Extra time for releasing services after <see cref="QuitTimeout"/>; then the app exits regardless.</summary>
    private static readonly TimeSpan QuitGrace = TimeSpan.FromSeconds(2);

    /// <summary>Writing the log queue at exit never holds the end of the session up longer (a hanging disk).</summary>
    private static readonly TimeSpan LogFlushTimeout = TimeSpan.FromSeconds(2);

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

    // Data folder and isolation of this run (T-044: the test mode of Debug builds).
    private readonly AppEnvironment _environment;

    // The logging of the process, owned by Program.Main (T-024).
    private readonly AppLogging _logging;

    // Records commands that reached the UI (Debug level); set with the host.
    private Microsoft.Extensions.Logging.ILogger? _commandLogger;

    // The services Program.Main started while macOS had no active display (T-099); null on a normal start.
    private readonly EarlyServices? _early;

    // The OS ended the session (desktop Exit) before the host finished starting.
    private bool _exited;

    /// <summary>Designer and tests: commands of later starts are not received.</summary>
    public App()
        : this(new InstanceCommandQueue(), AppEnvironment.Production, AppLogging.Disabled())
    {
    }

    /// <param name="instanceCommands">Filled by the single-instance server of <c>Program.Main</c> (T-043).</param>
    /// <param name="environment">Data folder and isolation of this run (T-044).</param>
    /// <param name="logging">The logging of the process; <c>Program.Main</c> disposes it after the app ends (T-024).</param>
    /// <param name="early">
    /// The services already running (T-099: started while macOS had no active display); the tray attaches to them.
    /// <see langword="null"/>: the app creates them (the normal start).
    /// </param>
    internal App(InstanceCommandQueue instanceCommands, AppEnvironment environment, AppLogging logging, EarlyServices? early = null)
    {
        ArgumentNullException.ThrowIfNull(instanceCommands);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(logging);
        _instanceCommands = instanceCommands;
        _environment = environment;
        _logging = logging;
        _early = early;
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        // The letter alone runs an item of the tray menu that Avalonia draws on Windows (T-069 Q2).
        TrayMenuAccessKeys.Register();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && !Design.IsDesignMode)
        {
            // First (T-099): UI-thread work of the services that ran before Avalonia runs now, in order, then on Avalonia's
            // dispatcher; it is the same (main) thread.
            _early?.Dispatcher.AttachTo(new AvaloniaUiDispatcher());
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
        if (_quit is { IsQuitting: true })
        {
            return;
        }

        if (_commandLogger is { } logger)
        {
            // Debug level (the test mode logs it): the end-to-end test of T-099 sees when a command reached the UI.
            LogInstanceCommandReachedUi(logger, command.Kind);
        }

        if (command == InstanceCommand.Activate)
        {
            _activation?.Activate();
            return;
        }

        if (command.Kind == InstanceCommandKind.Toast && command.Argument is { } launchArgument)
        {
            // A click on a toast in the notification center (T-068): reported by Windows to this instance's toast activator,
            // or forwarded by a start Windows made for it. Opens the printer's web like a click on the banner.
            _host?.ActivateToast(launchArgument);
            return;
        }

#if DEBUG
        // Test commands of the end-to-end tests (T-044), only in the isolated test mode.
        if (_environment.IsIsolated)
        {
            ITrayAppCommands commands = this;
            switch (command.Kind)
            {
                case InstanceCommandKind.Settings:
                    commands.ShowSettings();
                    break;
                case InstanceCommandKind.Diagnostics:
                    commands.ShowDiagnostics(command.Argument);
                    break;
                case InstanceCommandKind.About:
                    commands.ShowAbout();
                    break;
                case InstanceCommandKind.Camera when command.Argument is { } printerId:
                    commands.ToggleCamera(printerId);
                    break;
                case InstanceCommandKind.Quit:
                    commands.Quit();
                    break;
                case InstanceCommandKind.Crash:
                    TestCrash.Run(command.Argument);
                    break;
            }
        }
#endif
    }

    private IWindowShell CreateWindowShell(TrayHost host) =>
        OperatingSystem.IsWindows() ? new WindowsWindowShell(host.CreateLogger<WindowsWindowShell>(), _environment.AppUserModelId)
        : OperatingSystem.IsMacOS() ? new Platform.MacOS.MacWindowShell()
        : NoWindowShell.Instance;

    private static IAppPresence CreateAppPresence() =>
        OperatingSystem.IsMacOS() ? new Platform.MacOS.MacAppPresence() : NoAppPresence.Instance;

    /// <summary>
    /// The settings are loaded before the tray gets its menu (milliseconds, with the static icon from App.axaml
    /// meanwhile), so that every text starts in the saved language. async void: a startup failure crashes the app as
    /// before instead of leaving it silently without a menu.
    /// </summary>
    private async void StartHost(IClassicDesktopStyleApplicationLifetime desktop, TrayIcon? trayIcon)
    {
        // T-099: the tray attaches to the services that already run (the same monitoring session), else creates them.
        TrayHost host = _early is { } early
            ? AttachToEarlyServices(await early.Services.ConfigureAwait(true), trayIcon)
            : await TrayHost.StartAsync(trayIcon, this, _environment, _logging).ConfigureAwait(true);
        if (_exited)
        {
            // The OS ended the session while the settings were loading.
            host.RequestStop();
            _logging.Flush(LogFlushTimeout);
            return;
        }

        // macOS (T-102): a Dock icon while a window that is not always on top is open.
        _windowTracker = new AuxiliaryWindowTracker(CreateWindowShell(host), presence: new AppPresence(CreateAppPresence()));
        // macOS (T-019): the menu bar app is not active when its menu opens a window; the window comes with the app to the front.
        _windows = new AuxiliaryWindowFactory(_windowTracker.Track, host.WindowStates, OperatingSystem.IsMacOS() ? Platform.MacOS.MacAppActivation.Activate : null);
        _activation = new InstanceActivation(_windowTracker.Recent, () => ((ITrayAppCommands)this).ShowSettings());
        _host = host;
        _quit = new QuitCoordinator(StopHostAsync, () => desktop.Shutdown(), QuitTimeout, QuitGrace, host.CreateLogger<QuitCoordinator>());
        host.LanguageChanged += OnLanguageChanged;
        _commandLogger = host.CreateLogger<App>();
        // Last: starts that came while the settings were loading are handled now.
        _instanceCommands.Attach(OnInstanceCommand, new AvaloniaUiDispatcher(), host.CreateLogger<InstanceCommandQueue>());
    }

    /// <summary>
    /// T-099: the tray over the services that ran while macOS had no active display. A failure releases them (as
    /// <see cref="TrayHost.StartAsync"/> does) and crashes the app like any failed start.
    /// </summary>
    private TrayHost AttachToEarlyServices(ServiceHost services, TrayIcon? trayIcon)
    {
        try
        {
            return TrayHost.Attach(services, trayIcon, this, _environment);
        }
        catch
        {
            services.DisposeAfterFailedStart();
            throw;
        }
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
                // Already open: switch to the requested printer (instance commands). The tray menu item has no printer
                // (2026-10-21, the window has its own printer combo): the window keeps the current one.
                if (printerId is not null && owned is DiagnosticsViewModel viewModel)
                {
                    viewModel.ShowPrinter(printerId);
                }
            });
    }

    void ITrayAppCommands.ShowSettings() => ShowSettings(addPrinter: false);

    void ITrayAppCommands.AddPrinter() => ShowSettings(addPrinter: true);

    /// <param name="addPrinter">"Add printer…" (T-042), see <see cref="SettingsWindowCommand.Show"/>.</param>
    private void ShowSettings(bool addPrinter)
    {
        if (_host is not { } host || _windows is not { } windows || _quit is { IsQuitting: true })
        {
            return;
        }

        // "Apply" places the open camera windows again (T-040); "Edit layout in camera window…" starts their layout mode (T-092).
        CameraWindows cameraWindows = CameraWindowsFor(host);

        // The window first: its screens are the monitors offered for the camera window (T-035); a failure leaves nothing open.
        SettingsWindowCommand.Show(
            _settingsWindow,
            () => windows.Settings(screens => host.CreateSettingsViewModel(screens, cameraWindows, cameraWindows)),
            addPrinter);
    }

    void ITrayAppCommands.ToggleCamera(string printerId)
    {
        if (_host is not { } host || _quit is { IsQuitting: true })
        {
            return;
        }

        CameraWindowsFor(host).Toggle(printerId);
    }

    void ITrayAppCommands.BringCameraToFront(string printerId)
    {
        if (_host is not { } host || _quit is { IsQuitting: true })
        {
            return;
        }

        CameraWindowsFor(host).BringToFront(printerId);
    }

    /// <summary>
    /// The camera windows, created on first use; the tray menu follows which of them are open (T-040: their camera
    /// items read "Hide camera" while open, 2026-10-22).
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

        _aboutWindow.ShowOrActivate(() => (windows.About(host.CreateAboutViewModel()), null));
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

        // Last: quitting may still have logged a stop failure or timeout just before the shutdown. Main may not get to
        // dispose the logging when the OS ends the session, so the queue is written now.
        _logging.Flush(LogFlushTimeout);
    }

    [Microsoft.Extensions.Logging.LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "The instance command {Kind} reached the menu bar app.")]
    private static partial void LogInstanceCommandReachedUi(Microsoft.Extensions.Logging.ILogger logger, InstanceCommandKind kind);
}
