using Avalonia.Controls;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using PrusaTrayDiagnostic.App.Diagnostics;
using PrusaTrayDiagnostic.App.Localization;
using PrusaTrayDiagnostic.App.Platform;
using PrusaTrayDiagnostic.App.Settings;
using PrusaTrayDiagnostic.App.Tray;
using PrusaTrayDiagnostic.App.Tray.Icons;
using PrusaTrayDiagnostic.App.Tray.Menu;
using PrusaTrayDiagnostic.Core.Settings;
using PrusaTrayDiagnostic.Core.Web;

namespace PrusaTrayDiagnostic.App.Composition;

/// <summary>
/// Owns everything that runs while the app lives: the service provider, the monitoring session, and the
/// UI-thread-only tray objects (icon set, icon and menu presenters). Created and stopped on the UI thread; the UI
/// objects are released there before anything is awaited.
/// </summary>
internal sealed partial class TrayHost : IAsyncDisposable
{
    private static readonly TimeSpan DefaultStopTimeout = TimeSpan.FromSeconds(3);

    /// <summary>A save writes a few small items in milliseconds; quitting must still stay within the overall limit.</summary>
    private static readonly TimeSpan PendingSaveTimeout = TimeSpan.FromSeconds(1);

    /// <summary>How long quitting waits for requests in progress through the local web proxies (T-028).</summary>
    private static readonly TimeSpan WebProxyStopTimeout = TimeSpan.FromSeconds(1);

    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<TrayHost> _logger;
    private readonly ServiceProvider _services;
    private readonly MonitoringSession _session;
    private readonly ITrayIconSet? _iconSet;
    private readonly TrayIconPresenter<WindowIcon>? _iconPresenter;
    private readonly TrayMenuPresenter? _menuPresenter;
    private readonly UiLanguage _language;
    private readonly TrayStatusViewModel _status;
    private readonly TrayMenuViewModel _menu;
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
        _language = services.GetRequiredService<UiLanguage>();
        _status = services.GetRequiredService<TrayStatusViewModel>();
        _menu = services.GetRequiredService<TrayMenuViewModel>();
        _language.Changed += OnLanguageChanged;
    }

    /// <summary>
    /// UI thread: the language changed (saved settings, T-029) and the tray (menu, tooltip) already shows the new texts;
    /// open auxiliary windows have to format theirs again.
    /// </summary>
    public event EventHandler? LanguageChanged;

    /// <summary>
    /// Loads the settings (off the UI thread), applies their language and only then creates the tray objects, so that the
    /// first menu and tooltip are already in the chosen language; then starts monitoring with the loaded settings. Call on
    /// the UI thread; the returned task continues there.
    /// </summary>
    /// <param name="trayIcon">Tray icon to drive; <see langword="null"/> when the platform has none (monitoring still runs).</param>
    /// <param name="commands">Diagnostics / About / Quit handlers of the tray menu.</param>
    public static async Task<TrayHost> StartAsync(TrayIcon? trayIcon, ITrayAppCommands commands)
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
            ILogger<MonitoringSession> sessionLogger = services.GetRequiredService<ILogger<MonitoringSession>>();
            AppSettings initialSettings = await Task.Run(
                () => MonitoringSession.LoadSettingsAsync(services.GetRequiredService<ISettingsStore>, sessionLogger, CancellationToken.None),
                CancellationToken.None).ConfigureAwait(true);

            // Before the tray view models (resolved below) format their first texts.
            services.GetRequiredService<UiLanguage>().Apply(initialSettings.Language);

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
            session.Start(initialSettings);
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

    /// <summary>A view model for a new settings window; the window's owner disposes it. Valid until <see cref="StopAsync"/>.</summary>
    public SettingsViewModel CreateSettingsViewModel() => AppServices.CreateSettingsViewModel(_services);

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
        // From here on, menu clicks, late monitor updates and language switches no longer reach the tray.
        _language.Changed -= OnLanguageChanged;
        _menuPresenter?.Dispose();
        _iconPresenter?.Dispose();
        _iconSet?.Dispose();

        await WaitForPendingSaveAsync().ConfigureAwait(true);
        // In parallel: open browser tabs of the local web proxies (T-028) stop working, and their ports close.
        await Task.WhenAll(_session.StopAsync(timeout), StopWebProxiesAsync()).ConfigureAwait(true);
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

    /// <summary>
    /// A save from the settings window that is still running finishes first (at most <see cref="PendingSaveTimeout"/>), so
    /// that quitting in the middle of it does not leave a new password behind without its settings.
    /// </summary>
    private async Task WaitForPendingSaveAsync()
    {
        try
        {
            if (!await _services.GetRequiredService<ISettingsEditorStore>().WaitForPendingSaveAsync(PendingSaveTimeout).ConfigureAwait(true))
            {
                LogPendingSaveTimedOut(PendingSaveTimeout);
            }
        }
        catch (Exception ex)
        {
            LogPendingSaveFailed(ex);
        }
    }

    /// <summary>
    /// Stops the local web proxies, waiting at most <see cref="WebProxyStopTimeout"/> for requests in progress (e.g. an
    /// upload); after that they are stopped without waiting. Disposing the container afterwards finds none running.
    /// Never faults.
    /// </summary>
    private async Task StopWebProxiesAsync()
    {
        try
        {
            using CancellationTokenSource timeout = new(WebProxyStopTimeout);
            await _services.GetRequiredService<IPrinterWebGateway>().StopAllAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogWebProxyStopFailed(ex);
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (Volatile.Read(ref _stopped) != 0)
        {
            return;
        }

        _status.RefreshTexts();
        _menu.RefreshTexts();
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A settings save did not finish within {Timeout} while quitting.")]
    private partial void LogPendingSaveTimedOut(TimeSpan timeout);

    [LoggerMessage(Level = LogLevel.Error, Message = "Waiting for a settings save failed while quitting.")]
    private partial void LogPendingSaveFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stopping the web proxies failed while quitting.")]
    private partial void LogWebProxyStopFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Releasing services failed while stopping.")]
    private partial void LogDisposeFailed(Exception exception);

    // macOS menu bar template icons follow in T-018; until then every OS uses the coloured set.
    private static ColorTrayIconSet CreateIconSet() => new();
}
