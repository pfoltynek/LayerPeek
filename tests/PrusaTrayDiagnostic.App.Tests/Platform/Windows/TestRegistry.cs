using Microsoft.Win32;

namespace PrusaTrayDiagnostic.App.Tests.Platform.Windows;

/// <summary>
/// Registry keys written by tests: always under one per-user test key (never Explorer's settings), removed by the tests
/// and swept by <see cref="CredentialCleanupFixture"/> after a crashed run.
/// </summary>
internal static class TestRegistry
{
    public const string Root = @"Software\PrusaTrayDiagnostic.App.Tests";

    /// <summary>A key path (relative to HKCU) for one test; the key itself is not created.</summary>
    public static string NewSubKey() => Root + @"\" + Guid.NewGuid().ToString("N");

    public static void Delete(string subKey)
    {
        if (OperatingSystem.IsWindows())
        {
            Registry.CurrentUser.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
        }
    }

    public static void DeleteAll() => Delete(Root);
}
