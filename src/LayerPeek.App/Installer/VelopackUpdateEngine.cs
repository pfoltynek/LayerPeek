using System.Net;
using System.Runtime.Versioning;

using Velopack;
using Velopack.Sources;

namespace LayerPeek.App.Installer;

/// <summary>
/// Velopack's <c>UpdateManager</c> with the project's GitHub Releases, anonymous (no token in the app, decided 2026-10-21).
/// The channel is the RID the installation was packed for (<c>win-x64</c>, <c>osx-arm64</c>), read by Velopack from its
/// manifest. Created only inside a Velopack installation (<see cref="VelopackInstallation"/>), after <c>VelopackApp.Run()</c>:
/// on Windows since T-017, on macOS since T-058 (the <c>.app</c> of <c>vpk</c>).
/// </summary>
[SupportedOSPlatform("windows")]
[SupportedOSPlatform("macos")]
internal sealed class VelopackUpdateEngine : IUpdateEngine
{
    private readonly UpdateSource? _testSource;

    /// <param name="testSource">
    /// Only the test mode (Q10 a, T-058): the feed of the end-to-end test instead of GitHub, on the loopback or in a local
    /// folder (<see cref="UpdateSource.TryCreateForTest"/>); <see langword="null"/>: GitHub Releases.
    /// </param>
    public VelopackUpdateEngine(UpdateSource? testSource = null) => _testSource = testSource;

    public async Task<AvailableUpdate?> CheckAsync(bool prereleases, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        UpdateManager manager = new(CreateSource(prereleases));
        // CheckForUpdatesAsync takes no token: on cancellation WaitAsync only stops waiting and abandons the request, which
        // ends by itself (its result is dropped).
        UpdateInfo? info = await manager.CheckForUpdatesAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        return info is null
            ? null
            : new AvailableUpdate(info.TargetFullRelease.Version.ToString(), info.TargetFullRelease.NotesMarkdown)
            {
                EngineState = new State(manager, info),
            };
    }

    public Task DownloadAsync(AvailableUpdate update, CancellationToken cancellationToken)
    {
        State state = StateOf(update);
        return state.Manager.DownloadUpdatesAsync(state.Info, progress: null, cancellationToken);
    }

    /// <summary>Whether the updater starts the app again after applying (always, except with the feed of a test).</summary>
    internal bool RestartsAfterUpdate => _testSource is null;

    public void ApplyOnExit(AvailableUpdate update)
    {
        State state = StateOf(update);
        // Silent (no Velopack window), started again afterwards; the updater waits until this process has exited. The test
        // mode (Q10 a) never restarts: on macOS the updater starts the app through LaunchServices (open -n), which would start
        // it without the isolated test mode; the end-to-end test starts the updated app itself.
        state.Manager.WaitExitThenApplyUpdates(state.Info.TargetFullRelease, silent: true, restart: RestartsAfterUpdate, restartArgs: []);
    }

    private IUpdateSource CreateSource(bool prereleases) => _testSource switch
    {
        { Folder: { } folder } => new SimpleFileSource(new DirectoryInfo(folder)),
        { Address: { } address } => new SimpleWebSource(address),
        _ => new GithubSource(AppUpdates.RepositoryUrl, accessToken: null, prerelease: prereleases),
    };

    private static State StateOf(AvailableUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        return update.EngineState as State ?? throw new ArgumentException("The update was not found by this engine.", nameof(update));
    }

    private sealed record State(UpdateManager Manager, UpdateInfo Info);
}

/// <summary>
/// Where the end-to-end test of the updates (Q10 a, T-058) serves its feed: an http address on the loopback
/// (<c>http://127.0.0.1:&lt;port&gt;/</c>) or an existing local folder. Accepted only in the test mode (the composition root
/// asks only there); anything else (another host, a relative path, a missing folder) is refused.
/// </summary>
internal sealed record UpdateSource(Uri? Address, string? Folder)
{
    /// <summary>The environment variable of the test mode that names the feed.</summary>
    public const string TestVariable = "LAYERPEEK_TEST_UPDATE_SOURCE";

    /// <param name="value">The value of <see cref="TestVariable"/>.</param>
    /// <param name="directoryExists">Whether a folder exists (<see cref="Directory.Exists(string)"/>; tests use their own).</param>
    public static UpdateSource? TryCreateForTest(string? value, Func<string, bool> directoryExists)
    {
        ArgumentNullException.ThrowIfNull(directoryExists);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttp)
        {
            return IPAddress.TryParse(uri.Host, out IPAddress? address) && IPAddress.IsLoopback(address) && string.IsNullOrEmpty(uri.UserInfo)
                ? new UpdateSource(uri, null)
                : null;
        }

        return Path.IsPathFullyQualified(value) && directoryExists(value) ? new UpdateSource(null, Path.GetFullPath(value)) : null;
    }
}
