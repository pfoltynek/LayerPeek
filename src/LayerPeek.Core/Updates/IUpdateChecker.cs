namespace LayerPeek.Core.Updates;

/// <summary>A published release the app could update to (T-058).</summary>
/// <param name="Version">Its version, read from the release tag (<c>v0.3.1</c>).</param>
/// <param name="ReleasePage">The page of the release on GitHub, opened in the browser; always https.</param>
/// <param name="IsPrerelease">GitHub marks it as a pre-release (every <c>0.x</c> release, T-055) or its version has a suffix.</param>
public sealed record UpdateRelease(ReleaseVersion Version, Uri ReleasePage, bool IsPrerelease)
{
    /// <summary>The repository whose releases the app offers (T-055).</summary>
    public const string RepositoryUrl = "https://github.com/pfoltynek/LayerPeek";

    /// <summary>The release page of <paramref name="version"/>: <c>…/releases/tag/v&lt;version&gt;</c> (the tag of T-055).</summary>
    public static Uri PageFor(ReleaseVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return new Uri($"{RepositoryUrl}/releases/tag/v{version}", UriKind.Absolute);
    }
}

/// <summary>
/// Finds the newest release on offer (T-058). Implemented in App: GitHub REST for the <c>.zip</c> and development builds,
/// Velopack's feed in a Velopack installation (decision Q5 a, 2026-10-30), so that an installation is offered only releases
/// that have a package for it.
/// </summary>
public interface IUpdateChecker
{
    /// <summary>
    /// The newest release on offer, or <see langword="null"/> when there is none. Implementations may return a release that is
    /// not newer than the running app (the caller compares).
    /// </summary>
    /// <param name="includePrereleases">Pre-releases are on offer too (<see cref="PrereleaseUpdates.Offered"/>).</param>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <exception cref="Exception">The check failed (network, HTTP 403 of the rate limit, 404, invalid data); the caller logs it.</exception>
    Task<UpdateRelease?> CheckAsync(bool includePrereleases, CancellationToken cancellationToken);
}
