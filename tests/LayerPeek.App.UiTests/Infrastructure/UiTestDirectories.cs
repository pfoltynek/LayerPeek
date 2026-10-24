using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.Tests.Shared;

[assembly: AssemblyFixture(typeof(UiTestDirectoriesCleanupFixture))]

namespace LayerPeek.App.UiTests.Infrastructure;

/// <summary>
/// Temporary files of the UI tests (camera window state, T-014) live under <see cref="Root"/>, a folder reserved per
/// worktree (T-032), so they can be removed even when a previous run crashed before its own cleanup ran. Separate from
/// the snapshot failure folder (<see cref="SnapshotAssert.FailureDirectory"/>), which keeps only failure images.
/// </summary>
internal static class UiTestDirectories
{
    /// <summary>
    /// No legacy folder: the un-hashed <c>%TEMP%\LayerPeek.App.UiTests</c> of older T-014 runs is guarded by
    /// <c>LayerPeek.App.UiTests.lock</c>, which belongs to the snapshot reservation here, so this reservation
    /// cannot prove that no older run uses it (those runs delete it themselves when they end).
    /// </summary>
    public static ReservedTempDirectory Reserved { get; } = ReservedTempDirectory.ForCurrentWorktree("LayerPeek.App.UiTests.Files");

    public static string Root => Reserved.Path;

    /// <summary>Path of a new, not yet created, unique directory under <see cref="Root"/>.</summary>
    public static string NewDirectoryPath() => Path.Combine(Root, Guid.NewGuid().ToString("N"));

    public static void Delete(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}

/// <summary>
/// Sweeps leftovers of crashed runs before the first test, creates <see cref="UiTestDirectories.Root"/> and removes it
/// after the last test. Concurrent runs of this assembly in the same worktree are serialized by its lock.
/// </summary>
public sealed class UiTestDirectoriesCleanupFixture : IAsyncLifetime
{
    private TestRunLock? _runLock;

    public async ValueTask InitializeAsync()
    {
        _runLock = await UiTestDirectories.Reserved.AcquireLockAsync(TimeSpan.FromMinutes(5));
        try
        {
            await UiTestDirectories.Reserved.SweepAsync();
            await UiTestDirectories.Reserved.CreateAsync();
        }
        catch
        {
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
            await UiTestDirectories.Reserved.DeleteAsync();
        }
        finally
        {
            _runLock.Dispose();
            _runLock = null;
        }
    }
}
