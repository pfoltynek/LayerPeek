using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using PrusaTrayDiagnostic.App.Composition;

namespace PrusaTrayDiagnostic.App;

public partial class App : Application
{
    /// <summary>Quitting never waits longer than this for printer clients to stop.</summary>
    private static readonly TimeSpan QuitTimeout = TimeSpan.FromSeconds(3);

    private TrayHost? _host;
    private bool _quitting;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && !Design.IsDesignMode)
        {
            TrayIcon? trayIcon = TrayIcon.GetIcons(this) is [TrayIcon first, ..] ? first : null;
            _host = TrayHost.Start(trayIcon);
            desktop.Exit += OnDesktopExit;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async void OnQuitClicked(object? sender, EventArgs e)
    {
        if (_quitting)
        {
            return;
        }

        _quitting = true;
        try
        {
            if (_host is not null)
            {
                await _host.StopAsync(QuitTimeout);
            }
        }
        catch (Exception)
        {
            // Quitting must succeed even if a service fails to shut down cleanly.
        }
        finally
        {
            (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        }
    }

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        // Exit initiated by the OS (log off, shutdown): only request the stop, the process ends right after.
        if (!_quitting)
        {
            _host?.RequestStop();
        }
    }
}
