using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using PrusaTrayDiagnostic.App.Composition;
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

    private TrayHost? _host;
    private QuitCoordinator? _quit;
    private AboutWindow? _aboutWindow;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && !Design.IsDesignMode)
        {
            TrayIcon? trayIcon = TrayIcon.GetIcons(this) is [TrayIcon first, ..] ? first : null;
            _host = TrayHost.Start(trayIcon, this);
            _quit = new QuitCoordinator(StopHostAsync, () => desktop.Shutdown(), QuitTimeout, QuitGrace, _host.CreateLogger<QuitCoordinator>());
            desktop.Exit += OnDesktopExit;
        }

        base.OnFrameworkInitializationCompleted();
    }

    void ITrayAppCommands.ShowAbout()
    {
        if (_aboutWindow is not null)
        {
            _aboutWindow.Activate();
            return;
        }

        if (_host is null || _quit is { IsQuitting: true })
        {
            return;
        }

        AboutWindow window = new() { DataContext = new AboutViewModel(_host.UrlLauncher) };
        window.Closed += (_, _) => _aboutWindow = null;
        _aboutWindow = window;
        window.Show();
        window.Activate();
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
        // Auxiliary windows go first so that nothing is left on screen while monitoring stops.
        _aboutWindow?.Close();
        return _host?.StopAsync(timeout) ?? Task.CompletedTask;
    }

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        // Exit initiated by the OS (log off, shutdown): only request the stop, the process ends right after.
        if (_quit is not { IsQuitting: true })
        {
            _host?.RequestStop();
        }

        // Last: quitting may still have logged a stop failure or timeout just before the shutdown.
        _host?.ReleaseLogging();
    }
}
