using PrusaTrayDiagnostic.App.Tests;
using PrusaTrayDiagnostic.Tests.Shared;

[assembly: AssemblyFixture(typeof(TestDirectoriesCleanupFixture))]

namespace PrusaTrayDiagnostic.App.Tests;

/// <summary>
/// All temporary files of this test assembly live under <see cref="Root"/> (reserved per worktree, T-032), so they can be
/// removed even when a previous test run crashed before its own cleanup ran.
/// </summary>
internal static class TestDirectories
{
    /// <summary>
    /// The reservation of this assembly's files in %TEMP%. The un-hashed folder of older runs (T-014 before T-032, guarded
    /// by the same legacy lock <c>PrusaTrayDiagnostic.App.Tests.Files.lock</c>) is swept too. The credential tests keep
    /// their own global lock (T-032 decision); this reservation does not touch them.
    /// </summary>
    public static ReservedTempDirectory Reserved { get; } =
        ReservedTempDirectory.ForCurrentWorktree("PrusaTrayDiagnostic.App.Tests.Files", legacyDirectoryName: "PrusaTrayDiagnostic.App.Tests");

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
/// Sweeps leftovers of crashed runs before the first test (<see cref="ReservedTempDirectory.SweepAsync"/>), creates
/// <see cref="TestDirectories.Root"/> and removes it after the last test. Concurrent runs of this assembly in the same
/// worktree are serialized by its lock; runs in other worktrees use their own folder and lock.
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
