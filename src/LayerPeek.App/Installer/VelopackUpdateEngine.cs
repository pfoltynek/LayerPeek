using System.Runtime.Versioning;

using Velopack;
using Velopack.Sources;

namespace LayerPeek.App.Installer;

/// <summary>
/// Velopack's <c>UpdateManager</c> with the project's GitHub Releases, anonymous (no token in the app, decided 2026-10-21).
/// The channel is the RID the installation was packed for (<c>win-x64</c>), read by Velopack from its manifest. Created
/// only inside a Velopack installation (<see cref="VelopackInstallation"/>), after <c>VelopackApp.Run()</c>.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class VelopackUpdateEngine : IUpdateEngine
{
    public async Task<AvailableUpdate?> CheckAsync(bool prereleases, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        UpdateManager manager = new(new GithubSource(AppUpdates.RepositoryUrl, accessToken: null, prerelease: prereleases));
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

    public void ApplyOnExit(AvailableUpdate update)
    {
        State state = StateOf(update);
        // Silent (no Velopack window), started again afterwards; the updater waits until this process has exited.
        state.Manager.WaitExitThenApplyUpdates(state.Info.TargetFullRelease, silent: true, restart: true, restartArgs: []);
    }

    private static State StateOf(AvailableUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        return update.EngineState as State ?? throw new ArgumentException("The update was not found by this engine.", nameof(update));
    }

    private sealed record State(UpdateManager Manager, UpdateInfo Info);
}
