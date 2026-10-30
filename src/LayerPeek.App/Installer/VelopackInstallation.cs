namespace LayerPeek.App.Installer;

/// <summary>
/// Whether the running exe belongs to a Velopack installation, the layout Velopack's own locator recognizes:
/// <list type="bullet">
/// <item>Windows (T-017): <c>%LOCALAPPDATA%\LayerPeek\current\LayerPeek.exe</c> with the package manifest <c>sq.version</c>
/// next to it and <c>Update.exe</c> in the folder above (<c>WindowsVelopackLocator</c>, docs/velopack.md point 3).</item>
/// <item>macOS (T-058): <c>&lt;any&gt;.app/Contents/MacOS/LayerPeek</c> with <c>UpdateMac</c> and <c>sq.version</c> next to it
/// (<c>OsxVelopackLocator</c>): every <c>.app</c> of <c>vpk</c>, from the <c>.dmg</c>, the <c>.tar.gz</c> or
/// <c>Portable.zip</c> alike (a macOS installation is always "portable").</item>
/// </list>
/// Development builds, test starts, the <c>.zip</c> of T-055 and the <c>.app</c> of 0.3.0 (T-019, without Velopack) are not
/// installed.
/// </summary>
internal static class VelopackInstallation
{
    /// <summary>Every argument of a Velopack hook start starts with this (<c>--veloapp-install 0.1.0</c>...).</summary>
    public const string HookArgumentPrefix = "--veloapp-";

    public const string UpdateExeName = "Update.exe";

    /// <summary>The updater of a macOS installation, in <c>Contents/MacOS</c> next to the executable.</summary>
    public const string UpdateMacName = "UpdateMac";

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

    /// <summary>Whether <paramref name="executablePath"/> belongs to a Velopack installation on this OS (Windows or macOS).</summary>
    public static bool IsInstalledOnThisOs(string? executablePath, Func<string, bool> fileExists) =>
        OperatingSystem.IsWindows() ? IsInstalled(executablePath, fileExists)
        : OperatingSystem.IsMacOS() && IsInstalledMac(executablePath, fileExists);

    /// <summary>
    /// macOS (T-058): <paramref name="executablePath"/> is the executable of a <c>.app</c> with <c>UpdateMac</c> and
    /// <c>sq.version</c> in its <c>Contents/MacOS</c>.
    /// </summary>
    /// <param name="fileExists">Whether a file exists (<see cref="File.Exists(string)"/>; tests use their own).</param>
    public static bool IsInstalledMac(string? executablePath, Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(fileExists);
        if (Platform.MacOS.MacAppBundle.BundleOf(executablePath) is null)
        {
            return false;
        }

        string folder = Path.GetDirectoryName(executablePath)!;
        return fileExists(Path.Combine(folder, ManifestFileName)) && fileExists(Path.Combine(folder, UpdateMacName));
    }

    /// <summary>Whether Velopack started the exe for one of its hooks (the first argument names the hook).</summary>
    public static bool IsHookStart(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return arguments.Count > 0 && arguments[0].StartsWith(HookArgumentPrefix, StringComparison.OrdinalIgnoreCase);
    }
}
