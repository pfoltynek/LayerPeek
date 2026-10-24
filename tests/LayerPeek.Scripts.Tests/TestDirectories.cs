using LayerPeek.Scripts.Tests;
using LayerPeek.Tests.Shared;

[assembly: AssemblyFixture(typeof(TestDirectoriesCleanupFixture))]

namespace LayerPeek.Scripts.Tests;

/// <summary>
/// All temporary files of the script tests (the Pester run gets this folder as its temp root) live under
/// <see cref="Root"/>, reserved per worktree (T-032), so they are removed even when a run or pwsh crashed before its own
/// cleanup ran.
/// </summary>
internal static class TestDirectories
{
    public static ReservedTempDirectory Reserved { get; } = ReservedTempDirectory.ForCurrentWorktree("LayerPeek.Scripts.Tests");

    public static string Root => Reserved.Path;
}

/// <summary>
/// Sweeps leftovers of crashed runs before the first test, creates <see cref="TestDirectories.Root"/> and removes it
/// after the last test. Concurrent runs of this assembly in the same worktree are serialized by its lock.
/// </summary>
public sealed class TestDirectoriesCleanupFixture : IAsyncLifetime
{
    private TestRunLock? _runLock;

    public async ValueTask InitializeAsync()
    {
        _runLock = await TestDirectories.Reserved.AcquireLockAsync(TimeSpan.FromMinutes(5));
        try
        {
            await TestDirectories.Reserved.SweepAsync();
            await TestDirectories.Reserved.CreateAsync();
        }
        catch
        {
            // Do not keep other runs waiting when this one cannot start.
            _runLock.Dispose();
            _runLock = null;
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_runLock is null)
        {
            // Not initialized: without the lock the folder may belong to another run of this worktree.
            return;
        }

        try
        {
            await TestDirectories.Reserved.DeleteAsync();
        }
        finally
        {
            _runLock.Dispose();
            _runLock = null;
        }
    }
}
