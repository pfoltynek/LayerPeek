using LayerPeek.App.E2E.Tests.Infrastructure;
using LayerPeek.Tests.Shared;

[assembly: AssemblyFixture(typeof(UnitTestDirectoriesFixture))]

namespace LayerPeek.App.E2E.Tests.Infrastructure;

/// <summary>
/// Temporary files of the unit tests of this project (the journal tests of T-065, which run in every <c>dotnet test</c>,
/// unlike the end-to-end tests): under <see cref="Root"/>, reserved per worktree (T-032), so a crashed run leaves nothing
/// behind after the next one.
/// </summary>
internal static class UnitTestDirectories
{
    public static ReservedTempDirectory Reserved { get; } = ReservedTempDirectory.ForCurrentWorktree("LayerPeek.App.E2E.UnitTests");

    public static string Root => Reserved.Path;

    /// <summary>A new empty folder under <see cref="Root"/>.</summary>
    public static string Create()
    {
        string folder = Path.Combine(Root, Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(folder);
        return folder;
    }
}

/// <summary>Sweeps leftovers of crashed runs, creates <see cref="UnitTestDirectories.Root"/> and removes it after the last test.</summary>
public sealed class UnitTestDirectoriesFixture : IAsyncLifetime
{
    private TestRunLock? _runLock;

    public async ValueTask InitializeAsync()
    {
        if (E2EFixture.IsAuxiliaryProcess)
        {
            // A child or restore process of a running test run, which holds this lock: it runs one method, no unit test.
            return;
        }

        _runLock =await UnitTestDirectories.Reserved.AcquireLockAsync(TimeSpan.FromMinutes(5));
        try
        {
            await UnitTestDirectories.Reserved.SweepAsync();
            await UnitTestDirectories.Reserved.CreateAsync();
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
            return;
        }

        try
        {
            await UnitTestDirectories.Reserved.DeleteAsync();
        }
        finally
        {
            _runLock.Dispose();
            _runLock = null;
        }
    }
}
