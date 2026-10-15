namespace PrusaTrayDiagnostic.Core.Settings;

/// <summary>
/// Per-user application data location:
/// <c>%APPDATA%\PrusaTrayDiagnostic</c> on Windows, <c>~/Library/Application Support/PrusaTrayDiagnostic</c> on macOS.
/// </summary>
public static class AppDataPaths
{
    public const string AppFolderName = "PrusaTrayDiagnostic";

    /// <exception cref="InvalidOperationException">The OS does not provide a per-user data folder.</exception>
    public static string GetDefaultDirectory()
    {
        // Built explicitly on macOS: SpecialFolder.ApplicationData maps to ~/.config there.
        string root = OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support")
            : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        if (!Path.IsPathFullyQualified(root))
        {
            throw new InvalidOperationException("The per-user application data folder is not available.");
        }

        return Path.Combine(root, AppFolderName);
    }
}
