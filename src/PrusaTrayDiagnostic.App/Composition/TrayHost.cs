using Avalonia.Controls;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using PrusaTrayDiagnostic.App.Tray;
using PrusaTrayDiagnostic.App.Tray.Icons;

namespace PrusaTrayDiagnostic.App.Composition;

/// <summary>
/// Owns everything that runs while the app lives: the service provider, the monitoring session, and the
/// UI-thread-only tray objects (icon set, presenter). Created and stopped on the UI thread; the UI objects are
/// released there before anything is awaited.
/// </summary>
internal sealed class TrayHost : IAsyncDisposable
{
    private static readonly TimeSpan DefaultStopTimeout = TimeSpan.FromSeconds(3);

    private readonly ServiceProvider _services;
    private readonly MonitoringSession _session;
    private readonly ITrayIconSet? _iconSet;
    private readonly TrayIconPresenter<WindowIcon>? _presenter;
    private int _stopped;

    private TrayHost(ServiceProvider services, MonitoringSession session, ITrayIconSet? iconSet, TrayIconPresenter<WindowIcon>? presenter)
    {
        _services = services;
        _session = session;
        _iconSet = iconSet;
        _presenter = presenter;
    }

    /// <param name="trayIcon">Tray icon to drive; <see langword="null"/> when the platform has none (monitoring still runs).</param>
    public static TrayHost Start(TrayIcon? trayIcon)
    {
        ServiceProvider services = AppServices.Create();
        // Concrete type here; the field is ITrayIconSet so that T-018 can select the macOS set by OS.
        ColorTrayIconSet? iconSet = null;
        TrayIconPresenter<WindowIcon>? presenter = null;
        try
        {
            if (trayIcon is not null)
            {
                iconSet = CreateIconSet();
                presenter = new TrayIconPresenter<WindowIcon>(
                    new AvaloniaTrayIconTarget(trayIcon),
                    services.GetRequiredService<TrayStatusViewModel>(),
                    iconSet.GetIcon,
                    services.GetRequiredService<ILogger<TrayIconPresenter<WindowIcon>>>());
            }

            MonitoringSession session = services.GetRequiredService<MonitoringSession>();
            session.Start();
            return new TrayHost(services, session, iconSet, presenter);
        }
        catch
        {
            presenter?.Dispose();
            iconSet?.Dispose();
            services.Dispose();
            throw;
        }
    }

    /// <summary>Requests the monitoring to stop without waiting (e.g. the OS is ending the session).</summary>
    public void RequestStop() => _session.RequestStop();

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
        _presenter?.Dispose();
        _iconSet?.Dispose();

        await _session.StopAsync(timeout).ConfigureAwait(true);
        // The session was stopped above, so disposing it (via the container) does not wait again.
        await _services.DisposeAsync().ConfigureAwait(true);
    }

    public ValueTask DisposeAsync() => new(StopAsync(DefaultStopTimeout));

    // macOS menu bar template icons follow in T-018; until then every OS uses the coloured set.
    private static ColorTrayIconSet CreateIconSet() => new();
}
