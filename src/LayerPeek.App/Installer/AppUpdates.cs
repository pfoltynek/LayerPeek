using Microsoft.Extensions.Logging;

using LayerPeek.Core.Updates;

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
    /// <exception cref="Exception">The check failed (network, HTTP 404, rate limit, invalid feed).</exception>
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
/// Updates of the installed app from GitHub Releases through Velopack (T-017), the <see cref="IUpdateChecker"/> of a Velopack
/// installation (T-058 Q5 a: the feed of the installation decides, so that only releases with a package for its RID are
/// offered). Created only in a Velopack installation; the <c>.zip</c> and development builds use the GitHub REST checker.
/// <list type="bullet">
/// <item>A failed check throws for <see cref="UpdateCheckService"/> to log (network, HTTP 404, rate limit, a broken feed).</item>
/// <item>An update is applied only after the app quit normally, so that it saved its state: Velopack ends every process of
/// the installation when it applies an update. The app runs only in the primary instance (a second start passes its
/// command on and exits before the app starts, T-043), so only the primary instance ever applies an update.</item>
/// </list>
/// </summary>
internal sealed partial class AppUpdates : IUpdateChecker
{
    /// <summary>The repository whose releases carry <c>releases.&lt;RID&gt;.json</c> and the packages (T-055, T-017).</summary>
    public const string RepositoryUrl = UpdateRelease.RepositoryUrl;

    private readonly IUpdateEngine _engine;
    private readonly ILogger<AppUpdates> _logger;
    private readonly Lock _gate = new();

    // Guarded by _gate: what the last check found, kept for the download.
    private AvailableUpdate? _found;

    public AppUpdates(IUpdateEngine engine, ILogger<AppUpdates> logger)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(logger);
        _engine = engine;
        _logger = logger;
    }

    /// <summary>The newest version above the running one in the feed of the installation, or <see langword="null"/>.</summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <exception cref="Exception">The check failed, or the feed named a version that is no semantic version.</exception>
    public async Task<UpdateRelease?> CheckAsync(bool includePrereleases, CancellationToken cancellationToken)
    {
        AvailableUpdate? update = await _engine.CheckAsync(includePrereleases, cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            _found = update;
        }

        if (update is null)
        {
            return null;
        }

        ReleaseVersion version = ReleaseVersion.TryParse(update.Version, out ReleaseVersion? parsed)
            ? parsed
            : throw new FormatException($"The update feed names the version '{update.Version}', which is no semantic version.");
        // The feed has no pre-release flag of GitHub: it offered what the source was asked for.
        return new UpdateRelease(version, UpdateRelease.PageFor(version), version.IsPrerelease);
    }

    /// <summary>
    /// Downloads <paramref name="release"/> (found by the last check, or by a new one when the app started with it remembered),
    /// starts the updater that waits for this process to exit, and quits the app through <paramref name="quit"/> (its normal
    /// quit, which saves the state); the updater then applies the update and starts the app again. Never throws; nothing
    /// happens when the release is no longer in the feed, the download or the updater fails, or it is cancelled.
    /// </summary>
    /// <param name="includePrereleases">For a new check: whether pre-releases are on offer.</param>
    /// <param name="quit">The app's normal quit (the tray menu's Quit).</param>
    /// <returns>Whether the update was scheduled and the app quit.</returns>
    public async Task<bool> ApplyAsync(UpdateRelease release, bool includePrereleases, Func<Task> quit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(quit);
        string version = release.Version.ToString();
        try
        {
            AvailableUpdate? update;
            lock (_gate)
            {
                update = _found;
            }

            if (update is null || !Same(update, release))
            {
                // Remembered from an earlier run: the engine needs its own check first.
                update = await _engine.CheckAsync(includePrereleases, cancellationToken).ConfigureAwait(true);
                if (update is null || !Same(update, release))
                {
                    LogNoLongerOffered(version, update?.Version);
                    return false;
                }
            }

            await _engine.DownloadAsync(update, cancellationToken).ConfigureAwait(true);
            _engine.ApplyOnExit(update);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The app quits or the user gave up: nothing was scheduled.
            LogApplyCancelled(version);
            return false;
        }
        catch (Exception ex)
        {
            LogApplyFailed(ex, version);
            return false;
        }

        LogApplying(version);
        await quit().ConfigureAwait(true);
        return true;
    }

    private static bool Same(AvailableUpdate update, UpdateRelease release) =>
        ReleaseVersion.TryParse(update.Version, out ReleaseVersion? version) && version == release.Version;

    [LoggerMessage(Level = LogLevel.Information, Message = "Update {Version} is no longer in the feed (it offers {Offered}); nothing was downloaded.")]
    private partial void LogNoLongerOffered(string version, string? offered);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Update {Version} could not be downloaded or scheduled; the app keeps running.")]
    private partial void LogApplyFailed(Exception exception, string version);

    [LoggerMessage(Level = LogLevel.Information, Message = "The download of update {Version} was cancelled; nothing was applied.")]
    private partial void LogApplyCancelled(string version);

    [LoggerMessage(Level = LogLevel.Information, Message = "Update {Version} downloaded; the app quits and the updater applies it.")]
    private partial void LogApplying(string version);
}
