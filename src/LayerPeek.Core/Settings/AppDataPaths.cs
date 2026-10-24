namespace LayerPeek.Core.Settings;

/// <summary>
/// Per-user application data location:
/// <c>%APPDATA%\LayerPeek</c> on Windows, <c>~/Library/Application Support/LayerPeek</c> on macOS;
/// logs live elsewhere (<see cref="GetDefaultLogDirectory"/>).
/// </summary>
public static class AppDataPaths
{
    public const string AppFolderName = "LayerPeek";

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

    /// <summary>
    /// The standard per-user log folder of the OS (T-024): <c>%LOCALAPPDATA%\LayerPeek\logs</c> on Windows (logs
    /// stay on the machine, never roam), <c>~/Library/Logs/LayerPeek</c> on macOS (where Console.app looks).
    /// </summary>
    /// <exception cref="InvalidOperationException">The OS does not provide a per-user folder for logs.</exception>
    public static string GetDefaultLogDirectory()
    {
        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(FullyQualified(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)), "Library", "Logs", AppFolderName);
        }

        return Path.Combine(FullyQualified(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)), AppFolderName, "logs");
    }

    private static string FullyQualified(string root) =>
        Path.IsPathFullyQualified(root) ? root : throw new InvalidOperationException("The per-user folder for logs is not available.");
}
