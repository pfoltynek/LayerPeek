using System.Diagnostics;
using System.Runtime.Versioning;

using Microsoft.Win32;

namespace LayerPeek.App.E2E.Tests.Infrastructure;

/// <summary>What the portable copy of the update test leaves (T-128), as it was before the copy existed.</summary>
/// <param name="FolderExists">Whether the folder of the copy (the key) exists.</param>
/// <param name="TrayEntries">Explorer's tray entries of an exe inside that folder.</param>
/// <param name="VelopackLogs">The files in Velopack's log folder <c>%LOCALAPPDATA%\velopack</c> (names).</param>
/// <param name="VelopackFolderExists">Whether that log folder exists.</param>
internal sealed record UpdateCopyState(bool FolderExists, IReadOnlyList<string> TrayEntries, IReadOnlyList<string> VelopackLogs, bool VelopackFolderExists)
{
    public override string ToString() =>
        $"folder {(FolderExists ? "exists" : "missing")}; tray entries of the copy: {(TrayEntries.Count == 0 ? "none" : string.Join(", ", TrayEntries))}; "
        + $"Velopack logs: {(VelopackLogs.Count == 0 ? "none" : string.Join(", ", VelopackLogs))}";
}

/// <summary>
/// The portable Velopack copy of the update test (T-128; key: its folder, <c>update-&lt;id&gt;</c> under the reserved folder
/// of the run): its processes (the copy, its <c>Update.exe</c>), the folder itself, the tray entries Explorer creates for the
/// copy's exe under <c>HKCU\Control Panel\NotifyIconSettings</c> (a one-off path: they would only accumulate) and the log
/// files Velopack creates in <c>%LOCALAPPDATA%\velopack</c>. The restore ends the processes, deletes the folder, those tray
/// entries and the log files that did not exist before (lines Velopack adds to an existing log stay, like T-017). Journaled,
/// so that a killed run is cleaned up by the watchdog or the next start; part of the change kind of the toast tests (the
/// update test is one).
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class UpdateCopyKind() : SystemChangeKind<UpdateCopyState>("UpdateCopy", SystemChangeTraits.Toasts, SystemChangeJson.Default.UpdateCopyState)
{
    /// <summary>The start of the folder name of every copy (the restore refuses any other folder).</summary>
    public const string FolderPrefix = "update-";

    public static string VelopackLogFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify), "velopack");

    public override Task<UpdateCopyState> ReadStateAsync(string? key, CancellationToken cancellationToken)
    {
        string folder = Checked(key);
        return Task.FromResult(new UpdateCopyState(Directory.Exists(folder), TrayEntriesUnder(folder), VelopackLogs(), Directory.Exists(VelopackLogFolder)));
    }

    /// <summary>The folder and its tray entries are gone, and Velopack's log folder holds no file that was not there before.</summary>
    public override bool Matches(UpdateCopyState original, UpdateCopyState current) =>
        !current.FolderExists
        && current.TrayEntries.Count == 0
        && current.VelopackLogs.All(name => original.VelopackLogs.Contains(name, StringComparer.OrdinalIgnoreCase))
        && (original.VelopackFolderExists || !current.VelopackFolderExists);

    public override string Describe(string? key, UpdateCopyState original) =>
        $"Portable test copy of LayerPeek for the update test (T-128) in {key}, before: {original}. By hand: end its processes "
        + $"(LayerPeek.exe, Update.exe in that folder), delete the folder, delete the keys under HKCU\\{InstallerKind.NotifyIconSettingsSubKey} "
        + $"whose ExecutablePath is inside it, and the files in {VelopackLogFolder} that were not there before.";

    /// <summary>Nothing to apply: the test creates the copy; this change is only restored.</summary>
    protected override Task ApplyCoreAsync(string? key, UpdateCopyState state, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The test creates the copy itself; this change is only restored.");

    protected override async Task RestoreCoreAsync(string? key, UpdateCopyState original, CancellationToken cancellationToken)
    {
        string folder = Checked(key);
        List<string> failures = [];
        foreach (int id in ProcessesUnder(folder))
        {
            try
            {
                using Process process = Process.GetProcessById(id);
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException)
            {
                // Gone meanwhile; a process that stays holds the folder, which then fails below.
            }
        }

        // Update.exe may hold a file for a moment after it ended.
        for (int attempt = 1; Directory.Exists(folder); attempt++)
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt >= 10)
                {
                    failures.Add($"{folder}: {ex.Message}");
                    break;
                }

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
        }

        foreach (string entry in TrayEntriesUnder(folder))
        {
            Registry.CurrentUser.DeleteSubKeyTree($@"{InstallerKind.NotifyIconSettingsSubKey}\{entry}", throwOnMissingSubKey: false);
        }

        foreach (string name in VelopackLogs().Except(original.VelopackLogs, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                File.Delete(Path.Combine(VelopackLogFolder, name));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures.Add($"{Path.Combine(VelopackLogFolder, name)}: {ex.Message}");
            }
        }

        if (!original.VelopackFolderExists && Directory.Exists(VelopackLogFolder) && !Directory.EnumerateFileSystemEntries(VelopackLogFolder).Any())
        {
            Directory.Delete(VelopackLogFolder);
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", failures));
        }
    }

    /// <summary>Processes started from a file below <paramref name="folder"/> (the copy, its Update.exe).</summary>
    public static List<int> ProcessesUnder(string folder)
    {
        string prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
        List<int> ids = [];
        foreach (Process process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.MainModule?.FileName is { } file && file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        ids.Add(process.Id);
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                {
                    // Ended meanwhile, or not ours to read (another user, a protected process).
                }
            }
        }

        return ids;
    }

    /// <summary>The names of Velopack's log files (<c>velopack*</c>) in its log folder; nothing else of the folder is ever touched.</summary>
    public static IReadOnlyList<string> VelopackLogs() =>
        Directory.Exists(VelopackLogFolder)
            ? [.. Directory.EnumerateFiles(VelopackLogFolder, "velopack*").Select(Path.GetFileName).OfType<string>().Order(StringComparer.OrdinalIgnoreCase)]
            : [];

    /// <summary>Explorer's tray entries (<c>HKCU\Control Panel\NotifyIconSettings\&lt;n&gt;</c>) of an exe below <paramref name="folder"/>.</summary>
    public static IReadOnlyList<string> TrayEntriesUnder(string folder)
    {
        using RegistryKey? parent = Registry.CurrentUser.OpenSubKey(InstallerKind.NotifyIconSettingsSubKey);
        if (parent is null)
        {
            return [];
        }

        string prefix = Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar;
        List<string> entries = [];
        foreach (string name in parent.GetSubKeyNames())
        {
            using RegistryKey? entry = parent.OpenSubKey(name);
            if (entry?.GetValue("ExecutablePath") is string path && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                entries.Add(name);
            }
        }

        return entries;
    }

    /// <summary>
    /// The key as a folder this kind may delete: an absolute <c>update-&lt;id&gt;</c> folder directly in a reserved folder of
    /// these tests (<c>%TEMP%\LayerPeek.App.E2E.Tests…</c>); anything else is refused.
    /// </summary>
    internal static string Checked(string? key)
    {
        if (key is null || !Path.IsPathFullyQualified(key))
        {
            throw new InvalidOperationException($"'{key}' is no absolute folder of an update test copy; refusing to touch it.");
        }

        string folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(key));
        string name = Path.GetFileName(folder);
        string? parent = Path.GetFileName(Path.GetDirectoryName(folder));
        return name.StartsWith(FolderPrefix, StringComparison.Ordinal) && name.Length > FolderPrefix.Length
            && parent is not null && parent.StartsWith("LayerPeek.App.E2E.Tests", StringComparison.OrdinalIgnoreCase)
            ? folder
            : throw new InvalidOperationException($"'{key}' is not an {FolderPrefix}<id> folder in a reserved folder of the end-to-end tests; refusing to touch it.");
    }
}
