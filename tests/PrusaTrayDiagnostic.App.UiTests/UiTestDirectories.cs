using PrusaTrayDiagnostic.App.UiTests;
using PrusaTrayDiagnostic.Tests.Shared;

[assembly: AssemblyFixture(typeof(UiTestDirectoriesCleanupFixture))]

namespace PrusaTrayDiagnostic.App.UiTests;

/// <summary>
/// All temporary files of the UI tests live under <see cref="Root"/>, so they can be removed even when a previous run
/// crashed before its own cleanup ran.
/// </summary>
internal static class UiTestDirectories
{
    public static string Root { get; } = Path.Combine(Path.GetTempPath(), "PrusaTrayDiagnostic.App.UiTests");

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

/// <summary>Removes <see cref="UiTestDirectories.Root"/> before the first test (crash leftovers) and after the last one.</summary>
public sealed class UiTestDirectoriesCleanupFixture : IAsyncLifetime
{
    private TestRunLock? _runLock;

    public async ValueTask InitializeAsync()
    {
        _runLock = await TestRunLock.AcquireAsync("PrusaTrayDiagnostic.App.UiTests", TimeSpan.FromMinutes(5));
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
        TestRunLock.RetryAsync($"delete '{UiTestDirectories.Root}'", static () => UiTestDirectories.Delete(UiTestDirectories.Root));
}
