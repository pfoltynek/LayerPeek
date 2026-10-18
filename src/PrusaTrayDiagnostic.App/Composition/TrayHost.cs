using Avalonia.Controls;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using PrusaTrayDiagnostic.App.Diagnostics;
using PrusaTrayDiagnostic.App.Platform;
using PrusaTrayDiagnostic.App.Tray;
using PrusaTrayDiagnostic.App.Tray.Icons;
using PrusaTrayDiagnostic.App.Tray.Menu;

namespace PrusaTrayDiagnostic.App.Composition;

/// <summary>
/// Owns everything that runs while the app lives: the service provider, the monitoring session, and the
/// UI-thread-only tray objects (icon set, icon and menu presenters). Created and stopped on the UI thread; the UI
/// objects are released there before anything is awaited.
/// </summary>
internal sealed partial class TrayHost : IAsyncDisposable
{
    private static readonly TimeSpan DefaultStopTimeout = TimeSpan.FromSeconds(3);

    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<TrayHost> _logger;
    private readonly ServiceProvider _services;
    private readonly MonitoringSession _session;
    private readonly ITrayIconSet? _iconSet;
    private readonly TrayIconPresenter<WindowIcon>? _iconPresenter;
    private readonly TrayMenuPresenter? _menuPresenter;
    private int _stopped;

    private TrayHost(
        ILoggerFactory loggerFactory,
        ServiceProvider services,
        MonitoringSession session,
        ITrayIconSet? iconSet,
        TrayIconPresenter<WindowIcon>? iconPresenter,
        TrayMenuPresenter? menuPresenter)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<TrayHost>();
        _services = services;
        _session = session;
        _iconSet = iconSet;
        _iconPresenter = iconPresenter;
        _menuPresenter = menuPresenter;
    }

    /// <param name="trayIcon">Tray icon to drive; <see langword="null"/> when the platform has none (monitoring still runs).</param>
    /// <param name="commands">Diagnostics / About / Quit handlers of the tray menu.</param>
    public static TrayHost Start(TrayIcon? trayIcon, ITrayAppCommands commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        // Owned here, not by the container: it must outlive the container dispose during quit (see ReleaseLogging).
        ILoggerFactory loggerFactory = LoggerFactory.Create(static _ => { });
        ServiceProvider services = AppServices.Create(loggerFactory);
        // Concrete type here; the field is ITrayIconSet so that T-018 can select the macOS set by OS.
        ColorTrayIconSet? iconSet = null;
        TrayIconPresenter<WindowIcon>? iconPresenter = null;
        TrayMenuPresenter? menuPresenter = null;
        try
        {
            if (trayIcon is not null)
            {
                iconSet = CreateIconSet();
                iconPresenter = new TrayIconPresenter<WindowIcon>(
                    new AvaloniaTrayIconTarget(trayIcon),
                    services.GetRequiredService<TrayStatusViewModel>(),
                    iconSet.GetIcon,
                    services.GetRequiredService<ILogger<TrayIconPresenter<WindowIcon>>>());
                menuPresenter = new TrayMenuPresenter(
                    new TrayMenuTarget(new AvaloniaNativeTrayMenu(trayIcon)),
                    services.GetRequiredService<TrayMenuViewModel>(),
                    commands,
                    services.GetRequiredService<ILogger<TrayMenuPresenter>>());
            }

            MonitoringSession session = services.GetRequiredService<MonitoringSession>();
            session.Start();
            return new TrayHost(loggerFactory, services, session, iconSet, iconPresenter, menuPresenter);
        }
        catch
        {
            menuPresenter?.Dispose();
            iconPresenter?.Dispose();
            iconSet?.Dispose();
            services.Dispose();
            loggerFactory.Dispose();
            throw;
        }
    }

    /// <summary>Requests the monitoring to stop without waiting (e.g. the OS is ending the session).</summary>
    public void RequestStop() => _session.RequestStop();

    /// <summary>Opens web pages (About window link). Valid until <see cref="StopAsync"/>.</summary>
    public IUrlLauncher UrlLauncher => _services.GetRequiredService<IUrlLauncher>();

    /// <summary>A view model for a new diagnostics window; the window's owner disposes it. Valid until <see cref="StopAsync"/>.</summary>
    public DiagnosticsViewModel CreateDiagnosticsViewModel(string? initialPrinterId) => AppServices.CreateDiagnosticsViewModel(_services, initialPrinterId);

    /// <summary>Creates loggers for UI objects outside the container; valid until <see cref="ReleaseLogging"/>.</summary>
    public ILogger<T> CreateLogger<T>() => _loggerFactory.CreateLogger<T>();

    /// <summary>
    /// Stops monitoring (waiting at most <paramref name="timeout"/>) and releases everything. Call on the UI thread.
    /// </summary>
    public async Task StopAsync(TimeSpan timeout)
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;
        }

        // UI-thread-only objects first, synchronously, before any continuation could move to another thread.
        // From here on, menu clicks and late monitor updates no longer reach the tray.
        _menuPresenter?.Dispose();
        _iconPresenter?.Dispose();
        _iconSet?.Dispose();

        await _session.StopAsync(timeout).ConfigureAwait(true);
        try
        {
            // The session was stopped above, so disposing it (via the container) does not wait again.
            await _services.DisposeAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // Recorded here, while the (caller-owned) logger factory is certainly alive; quitting goes on.
            LogDisposeFailed(ex);
        }
    }

    /// <summary>
    /// Disposes the logger factory (flushing providers, T-024). Call last, after the application shut down; loggers
    /// created by <see cref="CreateLogger{T}"/> must not be used afterwards.
    /// </summary>
    public void ReleaseLogging() => _loggerFactory.Dispose();

    public ValueTask DisposeAsync() => new(StopAsync(DefaultStopTimeout));

    [LoggerMessage(Level = LogLevel.Error, Message = "Releasing services failed while stopping.")]
    private partial void LogDisposeFailed(Exception exception);

    // macOS menu bar template icons follow in T-018; until then every OS uses the coloured set.
    private static ColorTrayIconSet CreateIconSet() => new();
}
