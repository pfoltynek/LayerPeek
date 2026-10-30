using Microsoft.Extensions.Logging;

using LayerPeek.Core.Notifications;

namespace LayerPeek.Core.Updates;

/// <summary>What the update check follows from the settings (T-058).</summary>
/// <param name="AutomaticChecks">"Check for updates automatically" (Q2 a: on by default); a manual check works either way.</param>
/// <param name="IncludePrereleases">Pre-releases are on offer (<see cref="PrereleaseUpdates.Offered"/>, Q3 a).</param>
/// <param name="Notify">The notifications of the app are on (their master switch); otherwise no notification is shown.</param>
/// <param name="PlaySound">The notification plays a sound.</param>
public sealed record UpdatePreferences(bool AutomaticChecks, bool IncludePrereleases, bool Notify, bool PlaySound);

/// <summary>How a check ended (<see cref="UpdateCheckService.CheckNowAsync"/>).</summary>
public enum UpdateCheckOutcome
{
    /// <summary>No newer release is on offer.</summary>
    UpToDate,

    /// <summary>A newer release is on offer (<see cref="UpdateCheckResult.Release"/>).</summary>
    UpdateAvailable,

    /// <summary>The check failed (no network, the rate limit of GitHub, invalid data); details in the log.</summary>
    Failed,
}

/// <param name="Release">The release on offer for <see cref="UpdateCheckOutcome.UpdateAvailable"/>; otherwise <see langword="null"/>.</param>
public sealed record UpdateCheckResult(UpdateCheckOutcome Outcome, UpdateRelease? Release);

/// <summary>When the service checks (Q1 a, 2026-10-30); tests use short or fake times.</summary>
/// <param name="StartDelay">After the start, so that the check never delays the start or the first poll of the printers.</param>
/// <param name="Interval">Between two successful checks, measured from the saved time of the last one (survives restarts and sleep).</param>
/// <param name="Tick">How often the service looks whether a check is due; also the wait before trying a failed check again.</param>
public sealed record UpdateCheckTimings(TimeSpan StartDelay, TimeSpan Interval, TimeSpan Tick)
{
    /// <summary>90 s after the start, then once a day; looked at every hour.</summary>
    public static UpdateCheckTimings Default { get; } = new(TimeSpan.FromSeconds(90), TimeSpan.FromHours(24), TimeSpan.FromHours(1));
}

/// <summary>
/// Checks for a new version of the app (T-058) through an <see cref="IUpdateChecker"/> and reports it:
/// <list type="bullet">
/// <item>Automatically (while <see cref="UpdatePreferences.AutomaticChecks"/> is on) <see cref="UpdateCheckTimings.StartDelay"/>
/// after the start when the last successful check is <see cref="UpdateCheckTimings.Interval"/> ago or older, then whenever that
/// is the case again (looked at every <see cref="UpdateCheckTimings.Tick"/>, so a failed check is tried again an hour later and
/// a sleeping laptop checks after waking). Manually at any time (<see cref="CheckNowAsync"/>). One check at a time: a manual
/// check during an automatic one gets its result.</item>
/// <item><see cref="Available"/> is the newest release above the running version that may be offered (pre-releases only when
/// they are on offer), also the one remembered from the last run; <see cref="AvailableChanged"/> reports every change.</item>
/// <item>One notification per new version: for a version above the one notified last (remembered in
/// <see cref="IUpdateStateStore"/>); with notifications switched off the version counts as notified.</item>
/// <item>Failures of a check are only logged (no network, 403 rate limit, 404, invalid data): the release on offer stays.</item>
/// </list>
/// Thread-safe; events are raised on the thread that made the change (a timer thread or the caller of a check).
/// </summary>
public sealed partial class UpdateCheckService : IDisposable
{
    private readonly IUpdateChecker _checker;
    private readonly ReleaseVersion _running;
    private readonly IUpdateStateStore _store;
    private readonly INotificationService _notifications;
    private readonly TimeProvider _time;
    private readonly ILogger<UpdateCheckService> _logger;
    private readonly UpdateCheckTimings _timings;
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _stopping = new();

    // Guarded by _gate.
    private UpdatePreferences? _preferences;
    private UpdateState _state = UpdateState.Empty;
    private UpdateRelease? _available;
    private Task? _loading;
    private Task<UpdateCheckResult>? _check;
    private ITimer? _timer;
    private bool _disposed;

    public UpdateCheckService(
        IUpdateChecker checker,
        ReleaseVersion runningVersion,
        IUpdateStateStore store,
        INotificationService notifications,
        TimeProvider timeProvider,
        ILogger<UpdateCheckService> logger,
        UpdateCheckTimings? timings = null)
    {
        ArgumentNullException.ThrowIfNull(checker);
        ArgumentNullException.ThrowIfNull(runningVersion);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _checker = checker;
        _running = runningVersion;
        _store = store;
        _notifications = notifications;
        _time = timeProvider;
        _logger = logger;
        _timings = timings ?? UpdateCheckTimings.Default;
    }

    /// <summary>The version of the running app.</summary>
    public ReleaseVersion RunningVersion => _running;

    /// <summary>The newest release above the running version on offer now, or <see langword="null"/>.</summary>
    public UpdateRelease? Available
    {
        get
        {
            lock (_gate)
            {
                return _available;
            }
        }
    }

    /// <summary><see cref="Available"/> changed; raised outside the lock, on the thread that changed it.</summary>
    public event EventHandler? AvailableChanged;

    /// <summary>
    /// The settings of the current run: the first call starts the service (reads the remembered state, then the timer), later
    /// ones follow a saved change. Turning pre-releases on or off takes effect at once: a pre-release no longer on offer is
    /// withdrawn, and with automatic checks on a check runs right away. Never throws.
    /// </summary>
    public void ApplyPreferences(UpdatePreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        bool start;
        bool checkNow = false;
        bool changed = false;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            UpdatePreferences? previous = _preferences;
            _preferences = preferences;
            start = previous is null;
            if (!start && previous!.IncludePrereleases != preferences.IncludePrereleases)
            {
                changed = SetAvailableLocked(Offered(_available));
                checkNow = preferences.AutomaticChecks;
            }
        }

        if (changed)
        {
            RaiseAvailableChanged();
        }

        if (start)
        {
            Start();
        }
        else if (checkNow)
        {
            _ = RunInBackgroundAsync();
        }
    }

    /// <summary>
    /// Checks now (the button of the settings window, Q4 a), also while automatic checks are off; during a running check its
    /// result. Never throws except when cancelled.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled (the check itself goes on).</exception>
    public Task<UpdateCheckResult> CheckNowAsync(CancellationToken cancellationToken) => StartCheck().WaitAsync(cancellationToken);

    /// <summary>Stops the timer and a running check; nothing is reported afterwards. Safe to repeat.</summary>
    public void Dispose()
    {
        ITimer? timer;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            timer = _timer;
            _timer = null;
        }

        timer?.Dispose();
        _stopping.Cancel();
    }

    private void Start()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _loading = LoadAsync();
            // The callback looks at the clock: a check is due only when the saved last check is old enough.
            _timer = _time.CreateTimer(static state => ((UpdateCheckService)state!).OnTick(), this, _timings.StartDelay, _timings.Tick);
        }
    }

    private async Task LoadAsync()
    {
        UpdateState state;
        try
        {
            state = await _store.LoadAsync(_stopping.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Cancelled when stopping, or a store that broke its promise: start afresh.
            if (!_stopping.IsCancellationRequested)
            {
                LogStateUnreadable(ex);
            }

            state = UpdateState.Empty;
        }

        bool changed;
        UpdatePreferences? preferences;
        lock (_gate)
        {
            _state = state;
            preferences = _preferences;
            changed = !_disposed && SetAvailableLocked(Offered(state.Latest));
        }

        if (changed)
        {
            LogRemembered(state.Latest!.Version);
            RaiseAvailableChanged();
            await NotifyIfNewAsync(Available, preferences).ConfigureAwait(false);
        }
    }

    private void OnTick()
    {
        lock (_gate)
        {
            if (_disposed || _preferences is not { AutomaticChecks: true } || _check is not null || _loading is not { IsCompleted: true })
            {
                return;
            }

            DateTimeOffset now = _time.GetUtcNow();
            DateTimeOffset? last = _state.LastCheck;
            // A clock set back far behind the saved time would wait for days: such a time counts as no check.
            bool due = last is not { } time || now - time >= _timings.Interval || time - now > _timings.Tick;
            if (!due)
            {
                return;
            }
        }

        _ = RunInBackgroundAsync();
    }

    private async Task RunInBackgroundAsync()
    {
        try
        {
            await StartCheck().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Never seen: CheckCoreAsync ends every failure as a result.
            LogCheckFailed(ex);
        }
    }

    private Task<UpdateCheckResult> StartCheck()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return Task.FromResult(new UpdateCheckResult(UpdateCheckOutcome.Failed, null));
            }

            // On the thread pool: the checker may block briefly before its first await, and the caller may be the UI thread.
            return _check ??= Task.Run(CheckCoreAsync);
        }
    }

    private async Task<UpdateCheckResult> CheckCoreAsync()
    {
        try
        {
            Task? loading;
            lock (_gate)
            {
                loading = _loading;
            }

            if (loading is not null)
            {
                await loading.ConfigureAwait(false);
            }

            return await CheckOnceAsync().ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                _check = null;
            }
        }
    }

    private async Task<UpdateCheckResult> CheckOnceAsync()
    {
        UpdatePreferences preferences;
        lock (_gate)
        {
            // A manual check before the settings were read: no pre-releases unless the running version offers them.
            preferences = _preferences ?? new UpdatePreferences(AutomaticChecks: false, PrereleaseUpdates.IsDefaultFor(_running.ToString()), Notify: false, PlaySound: false);
        }

        UpdateRelease? newest;
        try
        {
            newest = await _checker.CheckAsync(preferences.IncludePrereleases, _stopping.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            return new UpdateCheckResult(UpdateCheckOutcome.Failed, null);
        }
        catch (Exception ex)
        {
            // Not an error of the app: no network, the rate limit (403), a missing repository (404), invalid data.
            LogCheckFailed(ex);
            return new UpdateCheckResult(UpdateCheckOutcome.Failed, null);
        }

        UpdateRelease? offered = newest is not null && newest.Version > _running && (preferences.IncludePrereleases || !newest.IsPrerelease)
            ? newest
            : null;
        bool changed;
        UpdateState state;
        lock (_gate)
        {
            if (_disposed)
            {
                return new UpdateCheckResult(UpdateCheckOutcome.Failed, null);
            }

            _state = _state with { LastCheck = _time.GetUtcNow(), Latest = offered };
            state = _state;
            changed = SetAvailableLocked(offered);
        }

        if (offered is null)
        {
            LogUpToDate(_running, preferences.IncludePrereleases);
        }
        else
        {
            LogAvailable(offered.Version, _running, preferences.IncludePrereleases);
        }

        if (changed)
        {
            RaiseAvailableChanged();
        }

        if (!await NotifyIfNewAsync(offered, preferences).ConfigureAwait(false))
        {
            await SaveAsync(state).ConfigureAwait(false);
        }

        return offered is null
            ? new UpdateCheckResult(UpdateCheckOutcome.UpToDate, null)
            : new UpdateCheckResult(UpdateCheckOutcome.UpdateAvailable, offered);
    }

    /// <summary>
    /// The notification for <paramref name="release"/> when its version is above the one notified last; then the state with the
    /// new notified version is saved.
    /// </summary>
    /// <returns>Whether the state was saved.</returns>
    private async Task<bool> NotifyIfNewAsync(UpdateRelease? release, UpdatePreferences? preferences)
    {
        if (release is null || preferences is null)
        {
            return false;
        }

        UpdateState state;
        lock (_gate)
        {
            if (_disposed || (_state.NotifiedVersion is { } notified && release.Version <= notified))
            {
                return false;
            }

            _state = _state with { NotifiedVersion = release.Version };
            state = _state;
        }

        if (preferences.Notify)
        {
            try
            {
                _notifications.ShowUpdateAvailable(new UpdateNotification(release.Version.ToString(), preferences.PlaySound));
                LogNotified(release.Version);
            }
            catch (Exception ex)
            {
                LogNotificationFailed(ex, release.Version);
            }
        }
        else
        {
            LogNotNotified(release.Version);
        }

        await SaveAsync(state).ConfigureAwait(false);
        return true;
    }

    private async Task SaveAsync(UpdateState state)
    {
        try
        {
            await _store.SaveAsync(state, _stopping.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (!_stopping.IsCancellationRequested)
        {
            LogStateNotSaved(ex);
        }
        catch (OperationCanceledException)
        {
            // Stopping: the state of this run is not needed any more.
        }
    }

    /// <summary><paramref name="release"/> when it may be offered with the current preferences (above the running version).</summary>
    private UpdateRelease? Offered(UpdateRelease? release) =>
        release is not null && release.Version > _running && ((_preferences?.IncludePrereleases ?? false) || !release.IsPrerelease)
            ? release
            : null;

    private bool SetAvailableLocked(UpdateRelease? release)
    {
        if (Equals(_available, release))
        {
            return false;
        }

        _available = release;
        return true;
    }

    private void RaiseAvailableChanged()
    {
        try
        {
            AvailableChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            LogHandlerFailed(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Update check: no newer version than {Running} (pre-releases offered: {Prereleases}).")]
    private partial void LogUpToDate(ReleaseVersion running, bool prereleases);

    [LoggerMessage(Level = LogLevel.Information, Message = "Update check: version {Version} is available (running {Running}, pre-releases offered: {Prereleases}).")]
    private partial void LogAvailable(ReleaseVersion version, ReleaseVersion running, bool prereleases);

    [LoggerMessage(Level = LogLevel.Information, Message = "Update check: version {Version} found by an earlier check is offered.")]
    private partial void LogRemembered(ReleaseVersion version);

    [LoggerMessage(Level = LogLevel.Information, Message = "Checking for updates failed; nothing changes.")]
    private partial void LogCheckFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Notification shown for the new version {Version}.")]
    private partial void LogNotified(ReleaseVersion version);

    [LoggerMessage(Level = LogLevel.Information, Message = "No notification for the new version {Version}: notifications are switched off.")]
    private partial void LogNotNotified(ReleaseVersion version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The notification for the new version {Version} could not be shown.")]
    private partial void LogNotificationFailed(Exception exception, ReleaseVersion version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The update state could not be read; the next check starts afresh.")]
    private partial void LogStateUnreadable(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The update state could not be saved.")]
    private partial void LogStateNotSaved(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "A handler of the available update failed.")]
    private partial void LogHandlerFailed(Exception exception);
}
