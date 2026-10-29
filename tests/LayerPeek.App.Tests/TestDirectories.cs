using LayerPeek.App.Tests;
using LayerPeek.Tests.Shared;

[assembly: AssemblyFixture(typeof(TestDirectoriesCleanupFixture))]

namespace LayerPeek.App.Tests;

/// <summary>
/// All temporary files of this test assembly live under <see cref="Root"/> (reserved per worktree, T-032), so they can be
/// removed even when a previous test run crashed before its own cleanup ran.
/// </summary>
internal static class TestDirectories
{
    /// <summary>
    /// The reservation of this assembly's files in %TEMP%. The un-hashed folder of older runs (T-014 before T-032, guarded
    /// by the same legacy lock <c>LayerPeek.App.Tests.Files.lock</c>) is swept too. The credential tests keep
    /// their own global lock (T-032 decision); this reservation does not touch them.
    /// </summary>
    public static ReservedTempDirectory Reserved { get; } =
        ReservedTempDirectory.ForCurrentWorktree("LayerPeek.App.Tests.Files", legacyDirectoryName: "LayerPeek.App.Tests");

    public static string Root => Reserved.Path;

    /// <summary>
    /// A second reservation with a short name (T-053): Unix socket paths are limited to 103 bytes, which a data folder under
    /// <see cref="Root"/> already exceeds on macOS. Only for folders that must hold a socket; swept and removed like
    /// <see cref="Reserved"/>.
    /// </summary>
    public static ReservedTempDirectory ShortReserved { get; } = ReservedTempDirectory.ForCurrentWorktree("lpt");

    public static string ShortRoot => ShortReserved.Path;

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
/// <see cref="TestDirectories.Root"/> and <see cref="TestDirectories.ShortRoot"/> and removes them after the last test. Concurrent runs of this assembly in the same
/// worktree are serialized by its lock; runs in other worktrees use their own folder and lock.
/// </summary>
public sealed class TestDirectoriesCleanupFixture : IAsyncLifetime
{
    private static readonly ReservedTempDirectory[] Reservations = [TestDirectories.Reserved, TestDirectories.ShortReserved];

    private readonly List<TestRunLock> _runLocks = [];

    public async ValueTask InitializeAsync()
    {
        try
        {
            // Always in the same order, so that two runs never wait for each other's second lock.
            foreach (ReservedTempDirectory reservation in Reservations)
            {
                _runLocks.Add(await reservation.AcquireLockAsync(TimeSpan.FromMinutes(5)));
                await reservation.SweepAsync();
                await reservation.CreateAsync();
            }
        }
        catch
        {
            // Do not keep other runs waiting when this one cannot start.
            ReleaseLocks();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            // Only the reservations whose lock this run holds: without it the folder may belong to another run.
            for (int i = 0; i < _runLocks.Count; i++)
            {
                await Reservations[i].DeleteAsync();
            }
        }
        finally
        {
            ReleaseLocks();
        }
    }

    private void ReleaseLocks()
    {
        foreach (TestRunLock runLock in _runLocks)
        {
            runLock.Dispose();
        }

        _runLocks.Clear();
    }
}
