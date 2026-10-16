namespace PrusaTrayDiagnostic.App.Tests.Platform.Windows;

/// <summary>
/// Shortcut files written by tests: always under one temp folder (never the Start menu), removed by the tests and
/// swept by <see cref="CredentialCleanupFixture"/> after a crashed run.
/// </summary>
internal static class TestShortcuts
{
    public static readonly string Root = Path.Combine(Path.GetTempPath(), "PrusaTrayDiagnostic.App.Tests.Shortcuts");

    /// <summary>Creates an empty folder for one test.</summary>
    public static string NewDirectory() => Directory.CreateDirectory(Path.Combine(Root, Guid.NewGuid().ToString("N"))).FullName;

    public static void Delete(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    public static void DeleteAll() => Delete(Root);
}
