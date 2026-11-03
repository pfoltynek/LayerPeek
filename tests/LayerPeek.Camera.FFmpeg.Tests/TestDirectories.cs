using LayerPeek.Camera.FFmpeg.Tests;
using LayerPeek.Tests.Shared;

[assembly: AssemblyFixture(typeof(TestDirectoriesCleanupFixture))]

namespace LayerPeek.Camera.FFmpeg.Tests;

/// <summary>
/// All files of the recording tests (T-144) live under <see cref="Root"/>, reserved per worktree (T-032), so they are
/// removed even when a previous run crashed before its own cleanup ran.
/// </summary>
internal static class TestDirectories
{
    public static ReservedTempDirectory Reserved { get; } = ReservedTempDirectory.ForCurrentWorktree("LayerPeek.Camera.FFmpeg.Tests");

    public static string Root => Reserved.Path;

    /// <summary>A new, empty folder under <see cref="Root"/>; the test deletes it with <see cref="Delete"/>.</summary>
    public static string CreateDirectory()
    {
        string path = Path.Combine(Root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static void Delete(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
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
