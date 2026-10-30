using Microsoft.Extensions.Logging;

using LayerPeek.App.Installer;
using LayerPeek.App.Platform;
using LayerPeek.App.Tray;
using LayerPeek.Core.Settings;
using LayerPeek.Core.Updates;

namespace LayerPeek.App.Updates;

/// <summary>What clicking the offered update does (T-058, decisions Q5 a and Q6 a).</summary>
internal enum UpdateAction
{
    /// <summary>Opens the release page in the browser: the <c>.zip</c>, development builds, a translocated <c>.app</c>.</summary>
    OpenReleasePage,

    /// <summary>A Velopack installation: download, quit normally (state saved) and start the new version.</summary>
    InstallAndRestart,
}

/// <summary>
/// The update the app offers (T-058), between <see cref="UpdateCheckService"/> and the UI (the tray menu item, the notification
/// click and the Startup and updates tab of the settings window):
/// <list type="bullet">
/// <item>Follows the settings of every monitoring run (<see cref="IMonitoringObserver"/>): automatic checks (Q2 a), pre-releases
/// (Q3 a, <see cref="PrereleaseUpdates.Offered"/>) and the notification switches.</item>
/// <item>One action for the menu item and the notification: the release page, or in a Velopack installation download, quit
/// and restart (Q6 a, <see cref="AppUpdates.ApplyAsync"/>). A macOS <c>.app</c> that runs translocated or from the
/// <c>.dmg</c> gets no update (Q8 a): it opens the release page and the settings window says to move the app first.</item>
/// <item><see cref="Changed"/> reports a new <see cref="Available"/> or <see cref="IsInstalling"/>, on any thread.</item>
/// </list>
/// </summary>
internal sealed partial class UpdateOffer : IMonitoringObserver, IDisposable
{
    private readonly UpdateCheckService _service;
    private readonly AppUpdates? _installer;
    private readonly IUrlLauncher _urls;
    private readonly ILogger<UpdateOffer> _logger;
    private readonly CancellationTokenSource _disposing = new();
    private Func<Task>? _quit;
    private volatile bool _installing;
    private volatile bool _includePrereleases;

    /// <param name="installer">The Velopack updates of an installation that can update itself; <see langword="null"/> otherwise.</param>
    /// <param name="needsMoveToApplications">
    /// macOS: a Velopack <c>.app</c> that cannot update itself where it runs (translocated, on the <c>.dmg</c>).
    /// </param>
    public UpdateOffer(UpdateCheckService service, AppUpdates? installer, bool needsMoveToApplications, IUrlLauncher urls, ILogger<UpdateOffer> logger)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(urls);
        ArgumentNullException.ThrowIfNull(logger);
        _service = service;
        _installer = installer;
        NeedsMoveToApplications = installer is null && needsMoveToApplications;
        _urls = urls;
        _logger = logger;
        _service.AvailableChanged += OnAvailableChanged;
    }

    /// <summary>The version of the running app.</summary>
    public ReleaseVersion RunningVersion => _service.RunningVersion;

    /// <summary>What a click does.</summary>
    public UpdateAction Action => _installer is null ? UpdateAction.OpenReleasePage : UpdateAction.InstallAndRestart;

    /// <summary>The macOS app has to be moved to Applications first (Q8 a); it still finds new versions and opens their page.</summary>
    public bool NeedsMoveToApplications { get; }

    /// <summary>The newer release on offer, or <see langword="null"/>.</summary>
    public UpdateRelease? Available => _service.Available;

    /// <summary>A Velopack update is being downloaded; the app quits once it is ready.</summary>
    public bool IsInstalling => _installing;

    /// <summary><see cref="Available"/> or <see cref="IsInstalling"/> changed (any thread).</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// How the app quits after a Velopack download: macOS while it waits for a display (T-099) ends the wait; once the UI runs
    /// it replaces that with the tray menu's Quit. Without one (the moment before either attached) a click does nothing.
    /// </summary>
    public void AttachQuit(Func<Task> quit)
    {
        ArgumentNullException.ThrowIfNull(quit);
        Volatile.Write(ref _quit, quit);
    }

    /// <summary>The manual check of the settings window (Q4 a). Never throws except when cancelled.</summary>
    public Task<UpdateCheckResult> CheckNowAsync(CancellationToken cancellationToken) => _service.CheckNowAsync(cancellationToken);

    /// <summary>
    /// The click on the menu item or on the notification: the release page, or the update and restart of a Velopack
    /// installation. Nothing without an offered update or while an update is being installed. Never throws.
    /// </summary>
    /// <returns>Whether the page was opened or the update scheduled.</returns>
    public async Task<bool> RunAsync()
    {
        UpdateRelease? release = _service.Available;
        if (release is null)
        {
            LogNothingOffered();
            return false;
        }

        if (_installer is null)
        {
            return await _urls.OpenAsync(release.ReleasePage).ConfigureAwait(false);
        }

        if (_installing)
        {
            return false;
        }

        if (Volatile.Read(ref _quit) is not { } quit)
        {
            LogNoQuit();
            return false;
        }

        SetInstalling(true);
        try
        {
            bool scheduled = await _installer.ApplyAsync(release, _includePrereleases, quit, _disposing.Token).ConfigureAwait(true);
            if (!scheduled)
            {
                // The download failed (logged): the release page lets the user update by hand.
                await _urls.OpenAsync(release.ReleasePage).ConfigureAwait(true);
            }

            return scheduled;
        }
        catch (Exception ex)
        {
            LogRunFailed(ex);
            return false;
        }
        finally
        {
            SetInstalling(false);
        }
    }

    void IMonitoringObserver.ApplySettings(AppSettings settings) => Apply(settings);

    void IMonitoringObserver.UpdateSettings(AppSettings settings) => Apply(settings);

    /// <summary>Monitoring of the printers failed; the update check does not depend on it.</summary>
    void IMonitoringObserver.ReportMonitoringFailed()
    {
    }

    /// <summary>The preferences the check follows for <paramref name="settings"/> of the app running <paramref name="runningVersion"/>.</summary>
    internal static UpdatePreferences PreferencesFor(AppSettings settings, ReleaseVersion runningVersion)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(runningVersion);
        return new UpdatePreferences(
            settings.CheckForUpdates,
            PrereleaseUpdates.Offered(settings.OfferPrereleaseUpdates, runningVersion.ToString()),
            Notify: settings.Notifications.Enabled,
            // Information only: a sound with the "all notifications" setting, never with "important only".
            PlaySound: settings.Notifications.Sound == NotificationSound.All);
    }

    public void Dispose()
    {
        _service.AvailableChanged -= OnAvailableChanged;
        _disposing.Cancel();
    }

    private void Apply(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        UpdatePreferences preferences = PreferencesFor(settings, _service.RunningVersion);
        _includePrereleases = preferences.IncludePrereleases;
        _service.ApplyPreferences(preferences);
    }

    private void SetInstalling(bool installing)
    {
        _installing = installing;
        RaiseChanged();
    }

    private void OnAvailableChanged(object? sender, EventArgs e) => RaiseChanged();

    private void RaiseChanged()
    {
        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            LogHandlerFailed(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The update was clicked, but no update is offered any more.")]
    private partial void LogNothingOffered();

    [LoggerMessage(Level = LogLevel.Information, Message = "The update was clicked before the app can quit for it; nothing happens.")]
    private partial void LogNoQuit();

    [LoggerMessage(Level = LogLevel.Error, Message = "Starting the offered update failed.")]
    private partial void LogRunFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "A handler of the offered update failed.")]
    private partial void LogHandlerFailed(Exception exception);
}
