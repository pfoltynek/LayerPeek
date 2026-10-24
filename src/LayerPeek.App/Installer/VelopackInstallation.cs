namespace LayerPeek.App.Installer;

/// <summary>
/// Whether the running exe belongs to a Velopack installation (T-017): <c>%LOCALAPPDATA%\LayerPeek\current\LayerPeek.exe</c>
/// with the package manifest <c>sq.version</c> next to it and <c>Update.exe</c> in the folder above, the layout Velopack's
/// own locator recognizes (<c>WindowsVelopackLocator</c>, docs/velopack.md point 3). Development builds, test starts and
/// the <c>.zip</c> of T-055 are not installed.
/// </summary>
internal static class VelopackInstallation
{
    /// <summary>Every argument of a Velopack hook start starts with this (<c>--veloapp-install 0.1.0</c>...).</summary>
    public const string HookArgumentPrefix = "--veloapp-";

    public const string UpdateExeName = "Update.exe";

    /// <summary>The package manifest Velopack puts next to the main exe.</summary>
    public const string ManifestFileName = "sq.version";

    /// <param name="executablePath">The running exe, normally <see cref="Environment.ProcessPath"/>.</param>
    /// <param name="fileExists">Whether a file exists (<see cref="File.Exists(string)"/>; tests use their own).</param>
    public static bool IsInstalled(string? executablePath, Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(fileExists);
        if (string.IsNullOrWhiteSpace(executablePath) || !Path.IsPathFullyQualified(executablePath))
        {
            return false;
        }

        string? folder = Path.GetDirectoryName(executablePath);
        string? root = folder is null ? null : Path.GetDirectoryName(folder);
        return root is not null
            && fileExists(Path.Combine(folder!, ManifestFileName))
            && fileExists(Path.Combine(root, UpdateExeName));
    }

    /// <summary>Whether Velopack started the exe for one of its hooks (the first argument names the hook).</summary>
    public static bool IsHookStart(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return arguments.Count > 0 && arguments[0].StartsWith(HookArgumentPrefix, StringComparison.OrdinalIgnoreCase);
    }
}
