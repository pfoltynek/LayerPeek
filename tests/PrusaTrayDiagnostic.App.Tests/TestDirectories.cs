using PrusaTrayDiagnostic.App.Tests;
using PrusaTrayDiagnostic.Tests.Shared;

[assembly: AssemblyFixture(typeof(TestDirectoriesCleanupFixture))]

namespace PrusaTrayDiagnostic.App.Tests;

/// <summary>
/// All temporary files of this test assembly live under <see cref="Root"/>, so they can be removed even when a previous
/// test run crashed before its own cleanup ran.
/// </summary>
internal static class TestDirectories
{
    public static string Root { get; } = Path.Combine(Path.GetTempPath(), "PrusaTrayDiagnostic.App.Tests");

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
/// Removes <see cref="TestDirectories.Root"/> before the first test (leftovers of a crashed run) and after the last one.
/// Concurrent runs are serialized by their own <see cref="TestRunLock"/> (the credential fixture holds another one).
/// </summary>
public sealed class TestDirectoriesCleanupFixture : IAsyncLifetime
{
    private TestRunLock? _runLock;

    public async ValueTask InitializeAsync()
    {
        _runLock = await TestRunLock.AcquireAsync("PrusaTrayDiagnostic.App.Tests.Files", TimeSpan.FromMinutes(5));
        try
        {
            await SweepAsync();
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
        try
        {
            await SweepAsync();
        }
        finally
        {
            _runLock?.Dispose();
        }
    }

    private static Task SweepAsync() =>
        TestRunLock.RetryAsync($"delete '{TestDirectories.Root}'", static () => TestDirectories.Delete(TestDirectories.Root));
}
