using System.Globalization;
using System.Management;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

using Microsoft.Win32;

namespace PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;

/// <summary>
/// What the toasts of the test app may leave in Windows (T-068), taken at the start and the end of an end-to-end run by
/// <see cref="SystemStateSnapshot"/>. Must be as before: the notifications switch, the test app's notification settings
/// key, the test shortcut, the registration of the test app's COM toast activator, and the production shortcut, key and
/// activator registration. Only reported (the user accepted traces Windows keeps,
/// 2026-10-22): every other registry key or value under the notification keys that names the test app id, and whether the
/// notification database <c>wpndatabase.db</c> mentions it. Only names and values of the test app are ever recorded.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed record ToastSystemState(
    string ToastsEnabled,
    string TestAppSettings,
    bool TestShortcutExists,
    bool TestActivatorRegistered,
    string ProductionShortcut,
    string ProductionSettings,
    string ProductionActivator,
    IReadOnlyList<string> Traces)
{
    /// <summary>The production app id (T-010); its key and shortcut are only compared.</summary>
    public const string ProductionAppUserModelId = "PrusaTrayDiagnostic";

    /// <summary>Registry keys searched (recursively) for the test app id.</summary>
    private static readonly string[] TraceRoots =
    [
        @"Software\Microsoft\Windows\CurrentVersion\Notifications",
        @"Software\Microsoft\Windows\CurrentVersion\PushNotifications",
        @"Software\Classes\AppUserModelId",
    ];

    private static string NotificationDatabase => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        "Microsoft", "Windows", "Notifications", "wpndatabase.db");

    public static ToastSystemState Capture()
    {
        string startMenu = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        string production = Path.Combine(startMenu, AppSwitches.ProductionShortcutFileName);
        return new ToastSystemState(
            Read(static () => RegistryKeys.ReadDword(ToastsEnabledKind.SubKey, ToastsEnabledKind.Value)?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "(missing)"),
            Read(static () => RegistryKeys.Read(ToastAppSettingsKind.SubKey).ToString()),
            File.Exists(TestToastsKind.ShortcutPath),
            TestToastsKind.ActivatorRegistered(),
            File.Exists(production) ? "SHA-256 " + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(production))) : "(missing)",
            Read(static () => RegistryKeys.Read($@"{ToastAppSettingsKind.SettingsSubKey}\{ProductionAppUserModelId}").ToString()),
            Read(static () => RegistryKeys.Read(TestToastsKind.ActivatorSubKey(AppSwitches.ProductionToastActivatorClsid) + @"\LocalServer32").ToString()),
            FindTraces());
    }

    /// <summary>
    /// Differences from <paramref name="end"/>; traces that appeared go to <paramref name="notes"/> (accepted, documented).
    /// The production settings key may change only while a production instance runs (it may show a toast meanwhile).
    /// </summary>
    public void Compare(ToastSystemState end, List<string> differences, List<string> notes)
    {
        if (ToastsEnabled != end.ToastsEnabled)
        {
            differences.Add($"Notifications switch ToastEnabled: {ToastsEnabled} at the start, {end.ToastsEnabled} at the end.");
        }

        if (TestAppSettings != end.TestAppSettings)
        {
            differences.Add($"Notification settings of {AppSwitches.AppUserModelId}: {TestAppSettings} at the start, {end.TestAppSettings} at the end.");
        }

        if (TestShortcutExists != end.TestShortcutExists)
        {
            differences.Add($"Test Start menu shortcut '{TestToastsKind.ShortcutPath}': {(TestShortcutExists ? "present" : "missing")} at the start, {(end.TestShortcutExists ? "present" : "missing")} at the end.");
        }

        if (TestActivatorRegistered != end.TestActivatorRegistered)
        {
            differences.Add($"Toast activator of the test app {AppSwitches.TestToastActivatorClsid}: {(TestActivatorRegistered ? "registered" : "missing")} at the start, {(end.TestActivatorRegistered ? "registered" : "missing")} at the end.");
        }

        if (ProductionActivator != end.ProductionActivator)
        {
            // The tests never register it; a running production instance does (at its start, only when it differs).
            string text = $"Toast activator of the production app {AppSwitches.ProductionToastActivatorClsid}: {ProductionActivator} at the start, {end.ProductionActivator} at the end.";
            if (ProductionInstanceRuns())
            {
                notes.Add(text + " A production instance runs (it registers its activator itself).");
            }
            else
            {
                differences.Add(text);
            }
        }

        if (ProductionShortcut != end.ProductionShortcut)
        {
            differences.Add($"Production Start menu shortcut '{AppSwitches.ProductionShortcutFileName}' changed: {ProductionShortcut} at the start, {end.ProductionShortcut} at the end.");
        }

        if (ProductionSettings != end.ProductionSettings)
        {
            string text = $"Notification settings of the production app {ProductionAppUserModelId}: {ProductionSettings} at the start, {end.ProductionSettings} at the end.";
            if (ProductionInstanceRuns())
            {
                notes.Add(text + " A production instance runs (its own toasts may change the key).");
            }
            else
            {
                differences.Add(text);
            }
        }

        foreach (string trace in end.Traces.Except(Traces, StringComparer.Ordinal))
        {
            notes.Add($"Trace of the test app left by Windows (accepted, T-068): {trace}");
        }
    }

    public override string ToString()
    {
        StringBuilder text = new();
        text.AppendLine(CultureInfo.InvariantCulture, $"Toasts: ToastEnabled={ToastsEnabled}; {AppSwitches.AppUserModelId}: {TestAppSettings}; test shortcut {(TestShortcutExists ? "present" : "missing")}; test activator {(TestActivatorRegistered ? "registered" : "missing")}");
        text.AppendLine(CultureInfo.InvariantCulture, $"Production app: shortcut {ProductionShortcut}; settings {ProductionSettings}; activator {ProductionActivator}");
        text.AppendLine(CultureInfo.InvariantCulture, $"Traces of {AppSwitches.AppUserModelId}:{(Traces.Count == 0 ? " none" : string.Empty)}");
        foreach (string trace in Traces)
        {
            text.AppendLine("  " + trace);
        }

        return text.ToString();
    }

    /// <summary>Registry keys and values under <see cref="TraceRoots"/> naming the test app, and the notification database.</summary>
    internal static List<string> FindTraces()
    {
        List<string> traces = [];
        foreach (string root in TraceRoots)
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(root);
            if (key is not null)
            {
                Search(key, $@"HKCU\{root}", traces, depth: 0);
            }
        }

        traces.Add(DatabaseTrace());
        return traces;
    }

    private static void Search(RegistryKey key, string path, List<string> traces, int depth)
    {
        foreach (string value in SafeNames(key.GetValueNames))
        {
            if (Mentions(value) || Mentions(SafeValue(key, value)))
            {
                traces.Add($@"{path}\{(value.Length == 0 ? "(Default)" : value)} (value)");
            }
        }

        if (depth > 6)
        {
            return;
        }

        foreach (string name in SafeNames(key.GetSubKeyNames))
        {
            string child = $@"{path}\{name}";
            if (Mentions(name))
            {
                traces.Add(child + " (key)");
            }

            try
            {
                using RegistryKey? sub = key.OpenSubKey(name);
                if (sub is not null)
                {
                    Search(sub, child, traces, depth + 1);
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                // Not readable: nothing to report.
            }
        }
    }

    private static string[] SafeNames(Func<string[]> names)
    {
        try
        {
            return names();
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }

    private static object? SafeValue(RegistryKey key, string name)
    {
        try
        {
            return key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    private static bool Mentions(object? value) => value switch
    {
        string text => text.Contains(AppSwitches.AppUserModelId, StringComparison.OrdinalIgnoreCase),
        string[] lines => lines.Any(static line => line.Contains(AppSwitches.AppUserModelId, StringComparison.OrdinalIgnoreCase)),
        byte[] data => Contains(data),
        _ => false,
    };

    private static bool Contains(byte[] data) =>
        data.AsSpan().IndexOf(Encoding.Unicode.GetBytes(AppSwitches.AppUserModelId)) >= 0
        || data.AsSpan().IndexOf(Encoding.UTF8.GetBytes(AppSwitches.AppUserModelId)) >= 0;

    /// <summary>Whether the notification database (and its write-ahead log) mentions the test app id; only that is recorded.</summary>
    private static string DatabaseTrace()
    {
        List<string> found = [];
        foreach (string file in new[] { NotificationDatabase, NotificationDatabase + "-wal" })
        {
            if (!File.Exists(file))
            {
                continue;
            }

            try
            {
                using FileStream stream = new(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using MemoryStream copy = new();
                stream.CopyTo(copy);
                found.Add($"{Path.GetFileName(file)}: {(Contains(copy.ToArray()) ? "mentions it" : "does not mention it")}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                found.Add($"{Path.GetFileName(file)}: not readable ({ex.GetType().Name})");
            }
        }

        return $@"%LOCALAPPDATA%\Microsoft\Windows\Notifications: {(found.Count == 0 ? "no database" : string.Join(", ", found))}";
    }

    private static string Read(Func<string> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return $"(not readable: {ex.Message})";
        }
    }

    /// <summary>Whether a PrusaTrayDiagnostic.exe without the marker of these tests runs (the user's own instance).</summary>
    /// <remarks>When WMI cannot answer, no instance is assumed: the change is then reported as a difference, never hidden.</remarks>
    private static bool ProductionInstanceRuns()
    {
        try
        {
            using ManagementObjectSearcher searcher = new("SELECT CommandLine FROM Win32_Process WHERE Name = 'PrusaTrayDiagnostic.exe'");
            using ManagementObjectCollection processes = searcher.Get();
            foreach (ManagementBaseObject process in processes)
            {
                using (process)
                {
                    if (process["CommandLine"] is not string commandLine || !commandLine.Contains(AppSwitches.MarkerArgument, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
        }

        return false;
    }
}
