using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

using Microsoft.Win32;

namespace LayerPeek.App.E2E.Tests.Infrastructure;

/// <summary>
/// What a real install and uninstall of the Velopack installer touch (T-017), as it was before: the installation folder
/// <c>%LOCALAPPDATA%\LayerPeek</c> (never an installation, otherwise the test does not run, but it may hold the logs of
/// T-024, which stay there by decision 2026-10-24: copied to <see cref="InstallerKind.BackupFolder"/> before the install),
/// the Start menu shortcut <c>LayerPeek.lnk</c> (the developer's shortcut, byte for byte), the autostart shortcut, the
/// registry keys of the app (Uninstall entry, production toast activator, Windows' notification settings of the app),
/// Explorer's tray entries of the installed exe and Velopack's logs and temporary folders.
/// </summary>
/// <param name="FolderEntries">The top-level names in the installation folder (e.g. <c>logs</c>).</param>
/// <param name="FolderFiles">Every file below the installation folder as <c>relative path|size|SHA-256</c>.</param>
/// <param name="SetupLeftovers">
/// Folders <c>LayerPeek.&lt;16 letters or digits&gt;</c> next to it: Setup.exe moves an existing folder there before it
/// installs and deletes it afterwards.
/// </param>
/// <param name="BackupExists">Whether the copy of the folder made before the install exists.</param>
/// <param name="StartMenuShortcut">The Start menu shortcut as Base64, or <see langword="null"/> when there was none.</param>
/// <param name="StartupShortcut">The autostart shortcut as Base64, or <see langword="null"/>.</param>
/// <param name="TrayEntries">Names of Explorer's tray entries of an exe inside the installation folder.</param>
/// <param name="ReadUtc">When the state was read (for the original: when the change began; never compared).</param>
internal sealed record InstallerState(
    bool InstallFolderExists,
    IReadOnlyList<string> FolderEntries,
    IReadOnlyList<string> FolderFiles,
    IReadOnlyList<string> SetupLeftovers,
    bool BackupExists,
    string? StartMenuShortcut,
    string? StartupShortcut,
    RegistryKeyState? Uninstall,
    RegistryKeyState? Activator,
    RegistryKeyState? NotificationSettings,
    IReadOnlyList<string> TrayEntries,
    bool VelopackFolderExists,
    IReadOnlyList<string> VelopackLogs,
    bool VelopackTempFolderExists,
    DateTime ReadUtc = default)
{
    public override string ToString() =>
        $"installation folder: {(InstallFolderExists ? $"exists ({string.Join(", ", FolderEntries)}; {FolderFiles.Count} files)" : "missing")}; "
        + $"Start menu shortcut: {(StartMenuShortcut is null ? "none" : "present")}; autostart shortcut: {(StartupShortcut is null ? "none" : "present")}; "
        + $"Uninstall key: {Describe(Uninstall)}; toast activator: {Describe(Activator)}; notification settings: {Describe(NotificationSettings)}; "
        + $"tray entries of the installation: {(TrayEntries.Count == 0 ? "none" : string.Join(", ", TrayEntries))}; "
        + $"Velopack logs: {(VelopackLogs.Count == 0 ? "none" : string.Join(", ", VelopackLogs))}";

    private static string Describe(RegistryKeyState? key) => key is null ? "missing" : "present";
}

/// <summary>
/// A real silent install and uninstall of the Velopack installer of the release build (T-017). Restoring uninstalls a
/// leftover installation with Velopack's own <c>Update.exe --uninstall --silent</c>, removes what the installation left
/// in the folder (all of it when the folder did not exist), copies the files of the folder back from the backup (a log a
/// running developer build started again keeps its new lines after the old ones), writes the registry keys and the
/// shortcuts back exactly as they were (a key that did not exist is deleted), deletes tray entries of the deleted exe and
/// the Velopack logs and temporary folders the test created. Nothing of an existing installation is ever touched: the
/// test runs only without one.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed partial class InstallerKind() : SystemChangeKind<InstallerState>("VelopackInstall", SystemChangeTraits.Installer, SystemChangeJson.Default.InstallerState)
{
    public const string UninstallSubKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\LayerPeek";

    public const string NotificationSettingsSubKey = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\LayerPeek";

    public const string NotifyIconSettingsSubKey = @"Control Panel\NotifyIconSettings";

    private static readonly TimeSpan UninstallTimeout = TimeSpan.FromMinutes(2);

    public static string ActivatorSubKey { get; } = TestToastsKind.ActivatorSubKey(AppSwitches.ProductionToastActivatorClsid);

    public static string LocalAppData { get; } = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);

    /// <summary><c>%LOCALAPPDATA%\LayerPeek</c>, Velopack's installation folder of the package id LayerPeek (also the log folder's parent).</summary>
    public static string InstallFolder { get; } = Path.Combine(LocalAppData, "LayerPeek");

    public static string InstalledExe { get; } = Path.Combine(InstallFolder, "current", "LayerPeek.exe");

    public static string UpdateExe { get; } = Path.Combine(InstallFolder, "Update.exe");

    /// <summary>The copy of the installation folder (the developer's logs) made before the install; deleted by the restore.</summary>
    public static string BackupFolder { get; } = Path.Combine(LocalAppData, "LayerPeek.E2E.InstallerBackup");

    public static string StartMenuShortcut { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppSwitches.ProductionShortcutFileName);

    public static string StartupShortcut { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), AppSwitches.ProductionShortcutFileName);

    /// <summary>Velopack's logs (known leftover after an uninstall, accepted 2026-10-24).</summary>
    public static string VelopackFolder { get; } = Path.Combine(LocalAppData, "velopack");

    public static string VelopackTempFolder { get; } = Path.Combine(Path.GetTempPath(), "velopack_LayerPeek");

    private static readonly string[] LogNames = ["velopack.log", "velopack_LayerPeek.log"];

    /// <summary>Whether the folder holds an installation (Update.exe or <c>current</c>), not just logs.</summary>
    public static bool IsInstalled() => File.Exists(UpdateExe) || Directory.Exists(Path.GetDirectoryName(InstalledExe));

    public override Task<InstallerState> ReadStateAsync(string? key, CancellationToken cancellationToken) => Task.FromResult(Read());

    public static InstallerState Read() => new(
        Directory.Exists(InstallFolder),
        TopLevel(InstallFolder),
        Files(InstallFolder),
        SetupLeftoverFolders(),
        Directory.Exists(BackupFolder),
        ReadFile(StartMenuShortcut),
        ReadFile(StartupShortcut),
        PerMonitorSettingsRegistry.ReadTree(UninstallSubKey),
        PerMonitorSettingsRegistry.ReadTree(ActivatorSubKey),
        PerMonitorSettingsRegistry.ReadTree(NotificationSettingsSubKey),
        TrayEntriesOfTheInstallation(),
        Directory.Exists(VelopackFolder),
        [.. LogNames.Where(static name => File.Exists(Path.Combine(VelopackFolder, name)))],
        Directory.Exists(VelopackTempFolder),
        DateTime.UtcNow);

    /// <summary>
    /// Copies the installation folder (it holds only the logs of a developer build then) to <see cref="BackupFolder"/>;
    /// call after the change began and before the installer runs.
    /// </summary>
    public static void BackUpFolder()
    {
        if (Directory.Exists(BackupFolder))
        {
            throw new InvalidOperationException($"{BackupFolder} already exists (left by an earlier run); restore it first with tools/run-e2e.ps1 -RestoreOnly.");
        }

        if (!Directory.Exists(InstallFolder))
        {
            return;
        }

        CopyTree(InstallFolder, BackupFolder, overwrite: false);
    }

    /// <summary>
    /// Every original file is back with at least its size (a log may have grown), the folder has no other top-level
    /// entries, no installation, no backup and no leftover of Setup.exe; the rest as it was.
    /// </summary>
    public override bool Matches(InstallerState original, InstallerState current) =>
        original.InstallFolderExists == current.InstallFolderExists
        && original.FolderEntries.Order(StringComparer.OrdinalIgnoreCase).SequenceEqual(current.FolderEntries.Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase)
        && original.FolderFiles.All(StartsWithOriginal)
        && original.SetupLeftovers.Order(StringComparer.OrdinalIgnoreCase).SequenceEqual(current.SetupLeftovers.Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase)
        && !current.BackupExists
        && original.StartMenuShortcut == current.StartMenuShortcut
        && original.StartupShortcut == current.StartupShortcut
        && Same(original.Uninstall, current.Uninstall)
        && Same(original.Activator, current.Activator)
        && Same(original.NotificationSettings, current.NotificationSettings)
        && original.TrayEntries.Order(StringComparer.OrdinalIgnoreCase).SequenceEqual(current.TrayEntries.Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase)
        && original.VelopackLogs.SequenceEqual(current.VelopackLogs, StringComparer.OrdinalIgnoreCase)
        && original.VelopackFolderExists == current.VelopackFolderExists
        && original.VelopackTempFolderExists == current.VelopackTempFolderExists;

    public override string Describe(string? key, InstallerState original) =>
        $"Velopack test installation of LayerPeek (T-017), before: {original}. By hand: \"{UpdateExe}\" --uninstall --silent "
        + $"(or Settings > Apps > Installed apps > LayerPeek > Uninstall); "
        + (original.InstallFolderExists
            ? $"copy the files of {BackupFolder} back into {InstallFolder} (the developer's logs) and delete {BackupFolder}; "
            : $"delete {InstallFolder} if it is still there; ")
        + $"delete leftover folders {InstallFolder}.<16 characters> of Setup.exe, "
        + $"start the developer build of LayerPeek once (it recreates its Start menu shortcut \"{StartMenuShortcut}\" and its toast activator HKCU\\{ActivatorSubKey}), "
        + $"and delete {VelopackFolder} when it was not there before.";

    /// <summary>Nothing to apply: the installer makes the changes; this change is only restored.</summary>
    protected override Task ApplyCoreAsync(string? key, InstallerState state, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The installer makes the changes itself; this change is only restored.");

    protected override async Task RestoreCoreAsync(string? key, InstallerState original, CancellationToken cancellationToken)
    {
        List<string> failures = [];
        // The test runs only without an installation, so an installation of the test's version made after the change began
        // is the test's. Anything else (a real installation of the user) is never uninstalled or removed: reported instead.
        string? conflict = IsInstalled() ? InstallationConflict(key, original.ReadUtc) : null;
        if (conflict is not null)
        {
            failures.Add(conflict);
        }
        else
        {
            if (File.Exists(UpdateExe))
            {
                Step(failures, "uninstall", () => RunUninstall(UninstallTimeout));
            }

            if (Directory.Exists(InstallFolder))
            {
                try
                {
                    await RemoveInstallationAsync(original, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // The other steps still run; the entry stays in the journal (the restore fails).
                    failures.Add($"removing the installation: {ex.GetType().Name}: {ex.Message}");
                }
            }

            if (Directory.Exists(BackupFolder))
            {
                Step(failures, "copying the folder back", () => RestoreFromBackup());
            }
        }

        Step(failures, "leftovers of Setup.exe", () =>
        {
            foreach (string folder in SetupLeftoverFolders().Except(original.SetupLeftovers, StringComparer.OrdinalIgnoreCase))
            {
                Directory.Delete(Path.Combine(LocalAppData, folder), recursive: true);
            }
        });

        foreach ((string subKey, RegistryKeyState? state) in new[]
        {
            (UninstallSubKey, original.Uninstall),
            (ActivatorSubKey, original.Activator),
            (NotificationSettingsSubKey, original.NotificationSettings),
        })
        {
            Step(failures, $@"HKCU\{subKey}", () => PerMonitorSettingsRegistry.ExecuteAt(subKey, PerMonitorSettingsRegistry.PlanRestore(state, PerMonitorSettingsRegistry.ReadTree(subKey))));
        }

        Step(failures, StartMenuShortcut, () => WriteFile(StartMenuShortcut, original.StartMenuShortcut));
        Step(failures, StartupShortcut, () => WriteFile(StartupShortcut, original.StartupShortcut));
        Step(failures, "tray entries", () =>
        {
            foreach (string entry in TrayEntriesOfTheInstallation().Except(original.TrayEntries, StringComparer.OrdinalIgnoreCase))
            {
                Registry.CurrentUser.DeleteSubKeyTree($@"{NotifyIconSettingsSubKey}\{entry}", throwOnMissingSubKey: false);
            }
        });
        Step(failures, "Velopack logs", () =>
        {
            foreach (string name in LogNames.Except(original.VelopackLogs, StringComparer.OrdinalIgnoreCase))
            {
                File.Delete(Path.Combine(VelopackFolder, name));
            }

            if (!original.VelopackFolderExists && Directory.Exists(VelopackFolder) && !Directory.EnumerateFileSystemEntries(VelopackFolder).Any())
            {
                Directory.Delete(VelopackFolder);
            }

            if (!original.VelopackTempFolderExists && Directory.Exists(VelopackTempFolder))
            {
                Directory.Delete(VelopackTempFolder, recursive: true);
            }
        });

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", failures));
        }
    }

    /// <summary><c>Update.exe --uninstall --silent</c> of the installation (ends its processes, runs the uninstall hook).</summary>
    /// <returns>The exit code.</returns>
    public static int RunUninstall(TimeSpan timeout)
    {
        ProcessStartInfo start = new(UpdateExe) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = InstallFolder };
        start.ArgumentList.Add("--uninstall");
        start.ArgumentList.Add("--silent");
        using Process process = Process.Start(start) ?? throw new InvalidOperationException($"{UpdateExe} did not start.");
        if (!process.WaitForExit(timeout))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{UpdateExe} --uninstall did not end within {timeout.TotalSeconds:0} s.");
        }

        return process.ExitCode;
    }

    /// <summary>Explorer's tray entries (<c>HKCU\Control Panel\NotifyIconSettings\&lt;n&gt;</c>) of an exe in the installation folder.</summary>
    public static IReadOnlyList<string> TrayEntriesOfTheInstallation()
    {
        using RegistryKey? parent = Registry.CurrentUser.OpenSubKey(NotifyIconSettingsSubKey);
        if (parent is null)
        {
            return [];
        }

        List<string> entries = [];
        string folder = Path.TrimEndingDirectorySeparator(InstallFolder) + Path.DirectorySeparatorChar;
        foreach (string name in parent.GetSubKeyNames())
        {
            using RegistryKey? entry = parent.OpenSubKey(name);
            if (entry?.GetValue("ExecutablePath") is string path && path.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
            {
                entries.Add(name);
            }
        }

        return entries;
    }

    /// <summary>Every file below <paramref name="folder"/> as <c>relative path|size</c>, sorted.</summary>
    public static IReadOnlyList<string> Files(string folder) =>
        !Directory.Exists(folder)
            ? []
            : [.. Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Select(file => $"{Path.GetRelativePath(folder, file)}|{new FileInfo(file).Length.ToString(CultureInfo.InvariantCulture)}|{HashOfStart(file, long.MaxValue)}")
                .Order(StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// Why the installation now is not the test's (another version, or made before the change began), or
    /// <see langword="null"/> when it is (see the pure overload); reads <c>current\sq.version</c> and the creation times.
    /// </summary>
    internal static string? InstallationConflict(string? testVersion, DateTime beganUtc)
    {
        string current = Path.GetDirectoryName(InstalledExe)!;
        string manifest = Path.Combine(current, "sq.version");
        string? version = File.Exists(manifest)
            ? System.Xml.Linq.XDocument.Load(manifest).Descendants().FirstOrDefault(static element => element.Name.LocalName == "version")?.Value.Trim()
            : null;
        return InstallationConflict(
            testVersion,
            beganUtc,
            version,
            File.Exists(manifest) ? Directory.GetCreationTimeUtc(current) : null,
            File.Exists(UpdateExe) ? File.GetCreationTimeUtc(UpdateExe) : null);
    }

    /// <summary>
    /// The installation is the test's when <c>current\sq.version</c> has <paramref name="testVersion"/> and <c>current</c>
    /// was created after <paramref name="beganUtc"/>, or, half deleted by an interrupted uninstall (no manifest any more,
    /// a locked <c>Update.exe</c> left), when that <c>Update.exe</c> was created after it. A few seconds of slack: the file
    /// system's creation times may round.
    /// </summary>
    /// <param name="installedVersion">The version in <c>current\sq.version</c>; <see langword="null"/> without the manifest.</param>
    /// <param name="currentCreatedUtc">When <c>current</c> was created (with the manifest).</param>
    /// <param name="updateExeCreatedUtc">When <c>Update.exe</c> was created; <see langword="null"/> without it.</param>
    /// <returns>Why it is not the test's (it is left alone then), or <see langword="null"/>.</returns>
    internal static string? InstallationConflict(string? testVersion, DateTime beganUtc, string? installedVersion, DateTime? currentCreatedUtc, DateTime? updateExeCreatedUtc)
    {
        DateTime after = beganUtc.AddSeconds(-5);
        bool ours = installedVersion is not null
            ? string.Equals(installedVersion, testVersion, StringComparison.Ordinal) && currentCreatedUtc >= after
            : updateExeCreatedUtc >= after;
        return ours
            ? null
            : $"The installation in {InstallFolder} (version {installedVersion ?? "unknown"}, current created {currentCreatedUtc?.ToString("o") ?? "-"}, Update.exe created {updateExeCreatedUtc?.ToString("o") ?? "-"}) "
              + $"is not the one of the test (version {testVersion ?? "unknown"}, after {beganUtc:o}); it was left alone. Uninstall it by hand if it is the test's.";
    }

    /// <summary>SHA-256 of the first <paramref name="length"/> bytes (all of a shorter file), read shared (a log may be open).</summary>
    private static string HashOfStart(string file, long length)
    {
        using FileStream stream = new(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using System.Security.Cryptography.IncrementalHash hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        byte[] buffer = new byte[81920];
        long left = length;
        int read;
        while (left > 0 && (read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, left))) > 0)
        {
            hash.AppendData(buffer, 0, read);
            left -= read;
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    /// <summary>The folders Setup.exe moves an existing installation folder to (<c>LayerPeek.&lt;16 random&gt;</c>).</summary>
    public static IReadOnlyList<string> SetupLeftoverFolders() =>
        !Directory.Exists(LocalAppData)
            ? []
            : [.. Directory.EnumerateDirectories(LocalAppData, "LayerPeek.*").Select(Path.GetFileName).OfType<string>().Where(static name => SetupLeftover().IsMatch(name))];

    [GeneratedRegex("^LayerPeek\\.[A-Za-z0-9]{16}$")]
    private static partial Regex SetupLeftover();

    private static IReadOnlyList<string> TopLevel(string folder) =>
        !Directory.Exists(folder) ? [] : [.. Directory.EnumerateFileSystemEntries(folder).Select(Path.GetFileName).OfType<string>().Order(StringComparer.OrdinalIgnoreCase)];

    /// <summary>The original content is the start of the file now (a log a running build wrote to meanwhile may be longer).</summary>
    internal static bool StartsWithOriginal(string original)
    {
        string[] parts = original.Split('|');
        if (parts.Length != 3 || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out long size))
        {
            // A journal entry of an older format (no hash): it cannot be checked, so it never counts as restored and stays
            // in the journal with its description (restore by hand).
            return false;
        }

        string file = Path.Combine(InstallFolder, parts[0]);
        return File.Exists(file) && new FileInfo(file).Length >= size && HashOfStart(file, size) == parts[2];
    }

    /// <summary>Removes what the installation left: everything when the folder did not exist, otherwise the new top-level entries.</summary>
    private static async Task RemoveInstallationAsync(InstallerState original, CancellationToken cancellationToken)
    {
        IEnumerable<string> entries = original.InstallFolderExists
            ? TopLevel(InstallFolder).Except(original.FolderEntries, StringComparer.OrdinalIgnoreCase).Select(static name => Path.Combine(InstallFolder, name))
            : [InstallFolder];
        foreach (string entry in entries.ToList())
        {
            await DeleteAsync(entry, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The files of the backup go back; a file a running developer build created again meanwhile (its log) gets the old
    /// content in front of the new one. Then the backup goes.
    /// </summary>
    private static void RestoreFromBackup()
    {
        foreach (string backup in Directory.EnumerateFiles(BackupFolder, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(InstallFolder, Path.GetRelativePath(BackupFolder, backup));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            long size = new FileInfo(backup).Length;
            if (!File.Exists(target))
            {
                File.Copy(backup, target);
            }
            else if (new FileInfo(target).Length < size || HashOfStart(target, size) != HashOfStart(backup, size))
            {
                // Not the original with new lines after it: the original first, then what was written meanwhile.
                byte[] newer = File.ReadAllBytes(target);
                File.Copy(backup, target, overwrite: true);
                using FileStream stream = new(target, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                stream.Write(newer);
            }
        }

        Directory.Delete(BackupFolder, recursive: true);
    }

    private static void CopyTree(string source, string destination, bool overwrite)
    {
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            // The log of a running build may be open: read it shared.
            using FileStream input = new(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using FileStream output = new(target, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write);
            input.CopyTo(output);
        }
    }

    private static void Step(List<string> failures, string name, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            failures.Add($"{name}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static async Task DeleteAsync(string path, CancellationToken cancellationToken)
    {
        // Velopack may still hold a file for a moment after its uninstall ended.
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
                else if (File.Exists(path))
                {
                    File.Delete(path);
                }

                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < 10)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static string? ReadFile(string path) => File.Exists(path) ? Convert.ToBase64String(File.ReadAllBytes(path)) : null;

    private static void WriteFile(string path, string? content)
    {
        if (content is null)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        byte[] bytes = Convert.FromBase64String(content);
        if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
        {
            File.WriteAllBytes(path, bytes);
        }
    }

    private static bool Same(RegistryKeyState? a, RegistryKeyState? b) =>
        PerMonitorSettingsRegistry.Export(a).SequenceEqual(PerMonitorSettingsRegistry.Export(b), StringComparer.OrdinalIgnoreCase);
}
