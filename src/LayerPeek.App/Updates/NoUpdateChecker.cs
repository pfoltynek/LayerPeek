using LayerPeek.Core.Updates;

namespace LayerPeek.App.Updates;

/// <summary>
/// The checker of the test mode without the update feed of a test (T-058): it never asks anyone (the tests never reach
/// GitHub) and reports every check as failed.
/// </summary>
internal sealed class NoUpdateChecker : IUpdateChecker
{
    public static NoUpdateChecker Instance { get; } = new();

    private NoUpdateChecker()
    {
    }

    public Task<UpdateRelease?> CheckAsync(bool includePrereleases, CancellationToken cancellationToken) =>
        Task.FromException<UpdateRelease?>(new InvalidOperationException("The test mode checks for updates only with the update feed of a test."));
}

/// <summary>The update state for this run only (no per-user data folder on this machine; tests).</summary>
internal sealed class MemoryUpdateStateStore : IUpdateStateStore
{
    private UpdateState _state = UpdateState.Empty;

    public Task<UpdateState> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Volatile.Read(ref _state));

    public Task SaveAsync(UpdateState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        Volatile.Write(ref _state, state);
        return Task.CompletedTask;
    }
}
