using Microsoft.Extensions.Logging;

namespace LayerPeek.App.Installer;

/// <summary>A newer version found by <see cref="AppUpdates.CheckAsync"/>.</summary>
/// <param name="Version">Its version (<c>X.Y.Z</c>).</param>
/// <param name="NotesMarkdown">Its release notes (the <c>CHANGELOG.md</c> section packed with <c>vpk --releaseNotes</c>), or <see langword="null"/>.</param>
internal sealed record AvailableUpdate(string Version, string? NotesMarkdown)
{
    /// <summary>What the engine needs to download and apply it (Velopack's <c>UpdateInfo</c>).</summary>
    internal object? EngineState { get; init; }
}

/// <summary>Finds, downloads and applies updates (Velopack); replaceable by a fake in tests.</summary>
internal interface IUpdateEngine
{
    /// <returns>The newest version above the running one, or <see langword="null"/>.</returns>
    /// <exception cref="Exception">The check failed (network, HTTP 404 of a private repository, rate limit, invalid feed).</exception>
    Task<AvailableUpdate?> CheckAsync(bool prereleases, CancellationToken cancellationToken);

    /// <exception cref="Exception">The download failed or did not match its checksum.</exception>
    Task DownloadAsync(AvailableUpdate update, CancellationToken cancellationToken);

    /// <summary>
    /// Starts the updater, which waits for this process to exit, then applies the downloaded update and starts the app
    /// again.
    /// </summary>
    /// <exception cref="Exception">The updater could not be started.</exception>
    void ApplyOnExit(AvailableUpdate update);
}

/// <summary>
/// Updates of the installed app from GitHub Releases through Velopack (T-017; offering them in the tray menu and in a
/// notification is T-058). Only in a Velopack installation (<see cref="IsAvailable"/>); the <c>.zip</c> of T-055 has none.
/// <list type="bullet">
/// <item>Every failure of a check is "no update", only logged: while the repository is private, GitHub answers 404
/// (docs/velopack.md point 7).</item>
/// <item>Pre-releases are asked for as the setting says (<see cref="Core.Updates.PrereleaseUpdates"/>).</item>
/// <item>An update is applied only after the app quit normally, so that it saved its state: Velopack ends every process of
/// the installation when it applies an update. The app runs only in the primary instance (a second start passes its
/// command on and exits before the app starts, T-043), so only the primary instance ever applies an update.</item>
/// </list>
/// </summary>
internal sealed partial class AppUpdates
{
    /// <summary>The repository whose releases carry <c>releases.&lt;RID&gt;.json</c> and the packages (T-055, T-017).</summary>
    public const string RepositoryUrl = "https://github.com/pfoltynek/LayerPeek";

    private readonly IUpdateEngine? _engine;
    private readonly Func<bool> _offerPrereleases;
    private readonly ILogger<AppUpdates> _logger;

    /// <param name="engine">Velopack; <see langword="null"/> outside an installation.</param>
    /// <param name="offerPrereleases">Whether pre-releases are offered, read at every check.</param>
    public AppUpdates(IUpdateEngine? engine, Func<bool> offerPrereleases, ILogger<AppUpdates> logger)
    {
        ArgumentNullException.ThrowIfNull(offerPrereleases);
        ArgumentNullException.ThrowIfNull(logger);
        _engine = engine;
        _offerPrereleases = offerPrereleases;
        _logger = logger;
    }

    /// <summary>Whether the app can update itself (it runs from a Velopack installation).</summary>
    public bool IsAvailable => _engine is not null;

    /// <summary>The newest version above the running one, or <see langword="null"/>; never fails except when cancelled.</summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public async Task<AvailableUpdate?> CheckAsync(CancellationToken cancellationToken)
    {
        if (_engine is null)
        {
            return null;
        }

        bool prereleases = _offerPrereleases();
        try
        {
            AvailableUpdate? update = await _engine.CheckAsync(prereleases, cancellationToken).ConfigureAwait(false);
            if (update is null)
            {
                LogNoUpdate(prereleases);
            }
            else
            {
                LogUpdateFound(update.Version, prereleases);
            }

            return update;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Not an error of the app: a private repository (404), no network, the GitHub rate limit, a broken feed.
            LogCheckFailed(ex);
            return null;
        }
    }

    /// <summary>
    /// Downloads <paramref name="update"/>, starts the updater that waits for this process to exit, and quits the app
    /// through <paramref name="quit"/> (its normal quit, which saves the state); the updater then applies the update and
    /// starts the app again. Never throws; nothing happens when the download or the updater fails or it is cancelled.
    /// </summary>
    /// <param name="quit">The app's normal quit (the tray menu's Quit).</param>
    /// <returns>Whether the update was scheduled and the app quit.</returns>
    public async Task<bool> ApplyAsync(AvailableUpdate update, Func<Task> quit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(quit);
        if (_engine is null)
        {
            return false;
        }

        try
        {
            await _engine.DownloadAsync(update, cancellationToken).ConfigureAwait(true);
            _engine.ApplyOnExit(update);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The app quits or the user gave up: nothing was scheduled.
            LogApplyCancelled(update.Version);
            return false;
        }
        catch (Exception ex)
        {
            LogApplyFailed(ex, update.Version);
            return false;
        }

        LogApplying(update.Version);
        await quit().ConfigureAwait(true);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "No update available (pre-releases offered: {Prereleases}).")]
    private partial void LogNoUpdate(bool prereleases);

    [LoggerMessage(Level = LogLevel.Information, Message = "Update {Version} available (pre-releases offered: {Prereleases}).")]
    private partial void LogUpdateFound(string version, bool prereleases);

    [LoggerMessage(Level = LogLevel.Information, Message = "Checking for updates failed; no update is offered.")]
    private partial void LogCheckFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Update {Version} could not be downloaded or scheduled; the app keeps running.")]
    private partial void LogApplyFailed(Exception exception, string version);

    [LoggerMessage(Level = LogLevel.Information, Message = "The download of update {Version} was cancelled; nothing was applied.")]
    private partial void LogApplyCancelled(string version);

    [LoggerMessage(Level = LogLevel.Information, Message = "Update {Version} downloaded; the app quits and the updater applies it.")]
    private partial void LogApplying(string version);
}
