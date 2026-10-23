using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;

using Microsoft.Win32;

namespace PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;

/// <summary>
/// The trait value of every test that shows toasts of the test app or creates its Start menu shortcut (T-067, T-068):
/// <c>[Trait(SystemChanges.Trait, ToastSystemChanges.TraitValue)]</c>. The one place to switch to the per-kind value of
/// T-066 (<c>SystemChangeTraits.Toasts</c>); the three kinds below are the only other places.
/// </summary>
internal static class ToastSystemChanges
{
    public const string TraitValue = SystemChanges.TraitValue;
}

/// <summary>One registry value as text, so that it can be written back exactly (<see cref="RegistryKeyState"/>).</summary>
/// <param name="Kind">The <see cref="RegistryValueKind"/> name (DWord, QWord, String, ExpandString, MultiString, Binary).</param>
/// <param name="Data">DWORD/QWORD in decimal, strings as they are (multi-strings joined by NUL), binary as hex.</param>
internal sealed record RegistryValueState(string Name, string Kind, string Data);

/// <summary>A registry key: whether it exists, its values and the names of its subkeys (compared, never written).</summary>
internal sealed record RegistryKeyState(bool Exists, IReadOnlyList<RegistryValueState> Values, IReadOnlyList<string> SubKeys)
{
    public static RegistryKeyState Missing { get; } = new(false, [], []);

    public RegistryValueState? Value(string name) =>
        Values.FirstOrDefault(value => string.Equals(value.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Same existence, same values (any order, names ignoring case) and same subkeys.</summary>
    public bool SameAs(RegistryKeyState other) =>
        Exists == other.Exists
        && Values.Count == other.Values.Count
        && Values.All(value => other.Value(value.Name) is { } same && same.Kind == value.Kind && same.Data == value.Data)
        && SubKeys.Order(StringComparer.OrdinalIgnoreCase).SequenceEqual(other.SubKeys.Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);

    public override string ToString() =>
        !Exists ? "(key missing)"
        : $"{(Values.Count == 0 ? "no values" : string.Join(", ", Values.Select(static value => $"{value.Name}={value.Data} ({value.Kind})")))}"
          + (SubKeys.Count == 0 ? string.Empty : $"; subkeys: {string.Join(", ", SubKeys)}");
}

/// <summary>One step that brings a registry key to a <see cref="RegistryKeyState"/>.</summary>
internal enum RegistryAction
{
    /// <summary>Delete the whole key (it did not exist).</summary>
    DeleteKey,

    /// <summary>Create the key (it existed without values maybe).</summary>
    CreateKey,

    /// <summary>Write the value as it was.</summary>
    SetValue,

    /// <summary>Delete a value that did not exist.</summary>
    DeleteValue,
}

/// <summary>Reads and writes <see cref="RegistryKeyState"/> under <c>HKCU</c>.</summary>
[SupportedOSPlatform("windows")]
internal static class RegistryKeys
{
    /// <summary>The key as it is now; a value of a kind that could not be written back exactly is refused.</summary>
    public static RegistryKeyState Read(string subKey)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(subKey);
        if (key is null)
        {
            return RegistryKeyState.Missing;
        }

        List<RegistryValueState> values = [];
        foreach (string name in key.GetValueNames().Order(StringComparer.OrdinalIgnoreCase))
        {
            RegistryValueKind kind = key.GetValueKind(name);
            object? data = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            values.Add(new RegistryValueState(name, kind.ToString(), kind switch
            {
                RegistryValueKind.DWord => ((int)data!).ToString(CultureInfo.InvariantCulture),
                RegistryValueKind.QWord => ((long)data!).ToString(CultureInfo.InvariantCulture),
                RegistryValueKind.String or RegistryValueKind.ExpandString => (string)data!,
                RegistryValueKind.MultiString => string.Join('\0', (string[])data!),
                RegistryValueKind.Binary => Convert.ToHexString((byte[])data!),
                _ => throw new InvalidOperationException($"HKCU\\{subKey}\\{name} has the value kind {kind}, which cannot be restored exactly; refusing to change the key."),
            }));
        }

        return new RegistryKeyState(true, values, [.. key.GetSubKeyNames().Order(StringComparer.OrdinalIgnoreCase)]);
    }

    /// <summary>
    /// The steps that turn <paramref name="current"/> into <paramref name="wanted"/>: a key that should not exist goes as
    /// a whole; otherwise values are written or deleted (subkeys are never created or deleted, only compared).
    /// </summary>
    internal static List<(RegistryAction Action, RegistryValueState? Value)> Plan(RegistryKeyState wanted, RegistryKeyState current)
    {
        List<(RegistryAction, RegistryValueState?)> plan = [];
        if (!wanted.Exists)
        {
            if (current.Exists)
            {
                plan.Add((RegistryAction.DeleteKey, null));
            }

            return plan;
        }

        if (!current.Exists)
        {
            plan.Add((RegistryAction.CreateKey, null));
        }

        foreach (RegistryValueState value in current.Values)
        {
            if (wanted.Value(value.Name) is null)
            {
                plan.Add((RegistryAction.DeleteValue, value));
            }
        }

        foreach (RegistryValueState value in wanted.Values)
        {
            if (current.Value(value.Name) is not { } now || now.Kind != value.Kind || now.Data != value.Data)
            {
                plan.Add((RegistryAction.SetValue, value));
            }
        }

        return plan;
    }

    /// <summary>Brings <c>HKCU\<paramref name="subKey"/></c> to <paramref name="wanted"/> (see <see cref="Plan"/>).</summary>
    public static void Write(string subKey, RegistryKeyState wanted)
    {
        List<(RegistryAction Action, RegistryValueState? Value)> plan = Plan(wanted, Read(subKey));
        if (plan.Count == 0)
        {
            return;
        }

        if (plan[0].Action == RegistryAction.DeleteKey)
        {
            Registry.CurrentUser.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
            return;
        }

        using RegistryKey key = Registry.CurrentUser.CreateSubKey(subKey, writable: true);
        foreach ((RegistryAction action, RegistryValueState? value) in plan)
        {
            switch (action)
            {
                case RegistryAction.DeleteValue:
                    key.DeleteValue(value!.Name, throwOnMissingValue: false);
                    break;
                case RegistryAction.SetValue:
                    RegistryValueKind kind = Enum.Parse<RegistryValueKind>(value!.Kind);
                    key.SetValue(value.Name, Parse(kind, value.Data), kind);
                    break;
            }
        }
    }

    private static object Parse(RegistryValueKind kind, string data) => kind switch
    {
        RegistryValueKind.DWord => int.Parse(data, CultureInfo.InvariantCulture),
        RegistryValueKind.QWord => long.Parse(data, CultureInfo.InvariantCulture),
        RegistryValueKind.String or RegistryValueKind.ExpandString => data,
        RegistryValueKind.MultiString => data.Length == 0 ? Array.Empty<string>() : data.Split('\0'),
        RegistryValueKind.Binary => Convert.FromHexString(data),
        _ => throw new InvalidOperationException($"Unsupported value kind {kind}."),
    };

    /// <summary>A DWORD value, <see langword="null"/> when missing; another kind is refused (it could not be written back).</summary>
    public static int? ReadDword(string subKey, string name)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(subKey);
        if (key?.GetValue(name) is not { } value)
        {
            return null;
        }

        return key.GetValueKind(name) == RegistryValueKind.DWord
            ? (int)value
            : throw new InvalidOperationException($"HKCU\\{subKey}\\{name} is a {key.GetValueKind(name)}, not a DWORD; refusing to change it.");
    }
}

/// <summary><c>ToastEnabled</c> of Windows (<see langword="null"/> = missing, which means on).</summary>
internal sealed record ToastsEnabledState(int? ToastEnabled)
{
    public bool IsOn => ToastEnabled != 0;
}

/// <summary>
/// The switch "Notifications" of Windows (Settings &gt; System &gt; Notifications, T-068):
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\PushNotifications</c>, DWORD <c>ToastEnabled</c> (0 = every
/// notification off; missing = on). A toast test switches it on only when it is off. What the Settings app writes; not a
/// documented API. A group policy that switches toasts off is not changed (the tests are skipped then).
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class ToastsEnabledKind() : SystemChangeKind<ToastsEnabledState>("ToastsEnabled", changesSystem: true, SystemChangeJson.Default.ToastsEnabledState)
{
    public const string SubKey = @"Software\Microsoft\Windows\CurrentVersion\PushNotifications";
    public const string Value = "ToastEnabled";

    /// <summary>The group policy "Turn off toast notifications" (user and machine).</summary>
    private const string PolicySubKey = @"Software\Policies\Microsoft\Windows\CurrentVersion\PushNotifications";
    private const string PolicyValue = "NoToastApplicationNotification";

    public override Task<ToastsEnabledState> ReadStateAsync(string? key, CancellationToken cancellationToken) =>
        Task.FromResult(new ToastsEnabledState(RegistryKeys.ReadDword(SubKey, Value)));

    public override string Describe(string? key, ToastsEnabledState original) =>
        $"Notifications of Windows: HKCU\\{SubKey}\\{Value} = {original.ToastEnabled?.ToString(CultureInfo.InvariantCulture) ?? "(missing)"}. "
        + $"By hand: Settings > System > Notifications > Notifications {(original.IsOn ? "on" : "off")}"
        + (original.ToastEnabled is null ? $", or delete the value {Value}." : ".");

    /// <summary>Why a group policy switches toasts off, or <see langword="null"/>.</summary>
    public static string? PolicyBlocksToasts()
    {
        foreach ((RegistryKey hive, string name) in new[] { (Registry.CurrentUser, "HKCU"), (Registry.LocalMachine, "HKLM") })
        {
            using RegistryKey? policy = hive.OpenSubKey(PolicySubKey);
            if (policy?.GetValue(PolicyValue) is int blocked && blocked != 0)
            {
                return $"Toasts are switched off by a group policy ({name}\\{PolicySubKey}\\{PolicyValue} = {blocked}); the tests do not change policies.";
            }
        }

        return null;
    }

    protected override Task ApplyCoreAsync(string? key, ToastsEnabledState state, CancellationToken cancellationToken)
    {
        using RegistryKey push = Registry.CurrentUser.CreateSubKey(SubKey, writable: true);
        if (state.ToastEnabled is { } value)
        {
            push.SetValue(Value, value, RegistryValueKind.DWord);
        }
        else
        {
            push.DeleteValue(Value, throwOnMissingValue: false);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Windows' notification settings of the test app (T-068): <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\PrusaTrayDiagnostic.E2E</c>,
/// which Windows creates after the first toast of the app. The original key (usually missing) is restored exactly: a key
/// that did not exist is deleted, otherwise its values are written back. A toast test switches the app's notifications
/// and banners on when they are off (<c>Enabled</c> / <c>ShowBanner</c> = 0). The production app's key is never touched.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class ToastAppSettingsKind() : SystemChangeKind<RegistryKeyState>("ToastSettings", changesSystem: true, SystemChangeJson.Default.RegistryKeyState)
{
    public const string SettingsSubKey = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings";

    /// <summary>The key of the test app; the only one this kind writes (and may delete as a whole).</summary>
    public static string SubKey { get; } = TestAppKey(AppSwitches.AppUserModelId);

    /// <summary>The values of the key that switch the app's notifications / banners off when they are 0.</summary>
    private static readonly string[] Switches = ["Enabled", "ShowBanner"];

    /// <summary>How long a restored key must stay as restored: Windows writes the key of an app shortly after its toasts.</summary>
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(2);

    private const int MaxRestoreAttempts = 3;

    /// <summary>
    /// The settings key of <paramref name="appUserModelId"/>, refused for anything but the test app id: this kind deletes
    /// the key as a whole, and an empty or production id would delete the user's notification settings.
    /// </summary>
    internal static string TestAppKey(string appUserModelId) =>
        appUserModelId is { Length: > 0 } && appUserModelId.EndsWith(".E2E", StringComparison.Ordinal) && !appUserModelId.Contains('\\', StringComparison.Ordinal)
            ? $@"{SettingsSubKey}\{appUserModelId}"
            : throw new InvalidOperationException($"'{appUserModelId}' is not the app id of the test app; refusing to touch its notification settings.");

    public override Task<RegistryKeyState> ReadStateAsync(string? key, CancellationToken cancellationToken) =>
        Task.FromResult(RegistryKeys.Read(SubKey));

    /// <summary>
    /// Writes the original back and waits until it stays so; when Windows writes the key again meanwhile (it records the
    /// app's last toasts), the original is written once more (the journal then checks the result).
    /// </summary>
    protected override async Task RestoreCoreAsync(string? key, RegistryKeyState original, CancellationToken cancellationToken)
    {
        for (int attempt = 1; attempt <= MaxRestoreAttempts; attempt++)
        {
            RegistryKeys.Write(SubKey, original);
            await Task.Delay(Settle, cancellationToken).ConfigureAwait(false);
            if (original.SameAs(RegistryKeys.Read(SubKey)))
            {
                return;
            }
        }
    }

    public override bool Matches(RegistryKeyState original, RegistryKeyState current) => original.SameAs(current);

    public override string Describe(string? key, RegistryKeyState original) =>
        $"Notification settings of the test app, HKCU\\{SubKey}: {original}. By hand: "
        + (original.Exists ? "regedit, write the values above back." : "regedit, delete that key.");

    /// <summary>
    /// <paramref name="original"/> with the app's notifications and banners on: an existing <c>Enabled</c> /
    /// <c>ShowBanner</c> of 0 becomes 1; a missing key stays missing (Windows' defaults are on).
    /// </summary>
    internal static RegistryKeyState WithNotificationsOn(RegistryKeyState original) =>
        !original.Exists
            ? original
            : original with
            {
                Values = [.. original.Values.Select(static value =>
                    Switches.Contains(value.Name, StringComparer.OrdinalIgnoreCase) && value.Kind == nameof(RegistryValueKind.DWord) && value.Data == "0"
                        ? value with { Data = "1" }
                        : value)],
            };

    protected override Task ApplyCoreAsync(string? key, RegistryKeyState state, CancellationToken cancellationToken)
    {
        RegistryKeys.Write(SubKey, state);
        return Task.CompletedTask;
    }
}

/// <summary>Whether the Start menu shortcut of the test app exists (its toast history cannot be read from here).</summary>
internal sealed record TestToastsState(bool ShortcutExists);

/// <summary>
/// What a test app with toasts (T-067) leaves in Windows: its toasts in the notification center and its Start menu
/// shortcut "Prusa Tray Diagnostic E2E.lnk" (T-068). The key of a change is the exe under test. Restoring runs that exe
/// in the Debug-only mode <c>--ptd-e2e-clear-toasts</c>, which clears the history of the test app id and deletes the
/// test shortcut, and checks its exit code 0 and that the shortcut is gone. Nothing of the production app is touched.
/// Like every system change it needs the warning of <c>tools/run-e2e.ps1</c> (the warning lists the test toasts and
/// the shortcut): every test that starts a test app with toasts carries the trait and journals this change (user
/// decision 2026-10-22).
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class TestToastsKind() : SystemChangeKind<TestToastsState>("ToastHistory", changesSystem: true, SystemChangeJson.Default.TestToastsState)
{
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(30);

    public static string ShortcutPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppSwitches.TestShortcutFileName);

    public override Task<TestToastsState> ReadStateAsync(string? key, CancellationToken cancellationToken) =>
        Task.FromResult(new TestToastsState(File.Exists(ShortcutPath)));

    /// <summary>The shortcut is gone: a shortcut that existed before was left by a crashed run and goes too.</summary>
    public override bool Matches(TestToastsState original, TestToastsState current) => !current.ShortcutExists;

    public override string Describe(string? key, TestToastsState original) =>
        $"Toasts of the test app {AppSwitches.AppUserModelId} and its Start menu shortcut '{ShortcutPath}' (shortcut before: {(original.ShortcutExists ? "a leftover" : "none")}). "
        + $"By hand: \"{key ?? "PrusaTrayDiagnostic.exe (Debug)"}\" {AppSwitches.ClearToastsArgument}, or clear the notifications of "
        + $"\"{Path.GetFileNameWithoutExtension(AppSwitches.TestShortcutFileName)}\" in the notification center (Win+N) and delete the shortcut.";

    /// <summary>Nothing to apply: the test app creates the shortcut and shows the toasts itself.</summary>
    protected override Task ApplyCoreAsync(string? key, TestToastsState state, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The test app creates its shortcut and toasts itself; this change is only restored.");

    protected override async Task RestoreCoreAsync(string? key, TestToastsState original, CancellationToken cancellationToken)
    {
        if (key is null || !File.Exists(key))
        {
            throw new InvalidOperationException($"The exe that cleans up the toasts ('{key}') does not exist any more.");
        }

        // A refused exe (ClearToasts.NotADebugBuild) fails the restore; the entry stays with its manual steps.
        ClearToastsResult result = await ClearToasts.RunAsync(key, CleanupTimeout).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"{AppSwitches.ClearToastsArgument} exited with {result.ExitCode}: {result.Error}");
        }
    }
}

/// <summary>What a run of <c>--ptd-e2e-clear-toasts</c> returned.</summary>
/// <param name="StartedApp">
/// Whether something wrote into the test data folder it got (the mode starts no app, so it must stay missing; the folder
/// is deleted again either way).
/// </param>
internal sealed record ClearToastsResult(int ExitCode, string Error, bool StartedApp);

/// <summary>The Debug-only cleanup mode of the app (T-067): toasts and Start menu shortcut of the test app.</summary>
[SupportedOSPlatform("windows")]
internal static class ClearToasts
{
    /// <summary>
    /// Runs <paramref name="exe"/> with <c>--ptd-e2e-clear-toasts</c> and complete test mode switches (a data folder that
    /// must stay missing, an instance suffix, the forward-only switch), so that a regression that starts the app would
    /// show and never reach the user's instance.
    /// </summary>
    public static async Task<ClearToastsResult> RunAsync(string exe, TimeSpan timeout)
    {
        string? notDebug = NotADebugBuild(exe);
        if (notDebug is not null)
        {
            throw new InvalidOperationException(notDebug);
        }

        // Under the reserved folder of the run when there is one (swept after a crash); the restore process has none.
        string parent = !E2EFixture.IsRestoreProcess && Directory.Exists(E2EFixture.Root) ? E2EFixture.Root : Path.GetTempPath();
        string run = Path.Combine(parent, "PrusaTrayDiagnostic.E2E.ClearToasts-" + Guid.NewGuid().ToString("N")[..12]);
        ProcessStartInfo start = new(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(exe) ?? Path.GetTempPath(),
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        start.ArgumentList.Add(AppSwitches.ClearToastsArgument);
        start.ArgumentList.Add(AppSwitches.MarkerArgument + run);
        start.Environment[AppSwitches.DataDirectoryVariable] = Path.Combine(run, "data");
        start.Environment[AppSwitches.SuffixVariable] = "e2e-" + Guid.NewGuid().ToString("N")[..12];
        start.Environment[AppSwitches.ForwardOnlyVariable] = "1";
        start.Environment.Remove(AppSwitches.TestToastsVariable);
        start.Environment.Remove(AppSwitches.TestCredentialPrefixVariable);
        start.Environment.Remove(AppSwitches.TestStartDelayVariable);
        using Process process = Process.Start(start) ?? throw new InvalidOperationException($"'{exe}' {AppSwitches.ClearToastsArgument} did not start.");
        try
        {
            Task<string> error = process.StandardError.ReadToEndAsync();
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(timeout).ConfigureAwait(false);
            await output.ConfigureAwait(false);
            return new ClearToastsResult(process.ExitCode, (await error.ConfigureAwait(false)).Trim(), Directory.Exists(run));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            // Only when a regression started the app: never leave its folder behind.
            if (Directory.Exists(run))
            {
                try
                {
                    Directory.Delete(run, recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }

    /// <summary>
    /// Why <paramref name="exe"/> must not be run in the cleanup mode, or <see langword="null"/>: only a Debug build of the
    /// app knows the mode (its managed assembly, or the Native AOT exe itself, holds the argument); a Release build would
    /// ignore it and start as the user's production app. A journal entry may name an exe that was rebuilt meanwhile.
    /// </summary>
    internal static string? NotADebugBuild(string exe)
    {
        if (!string.Equals(Path.GetFileName(exe), "PrusaTrayDiagnostic.exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(exe))
        {
            return $"'{exe}' is not an existing PrusaTrayDiagnostic.exe; refusing to run it with {AppSwitches.ClearToastsArgument}.";
        }

        string assembly = Path.ChangeExtension(exe, ".dll");
        string code = File.Exists(assembly) ? assembly : exe;
        return E2EFixture.BinaryContains(code, AppSwitches.ClearToastsArgument)
            ? null
            : $"'{code}' does not know {AppSwitches.ClearToastsArgument} (not a Debug build); refusing to run it, it would start the production app.";
    }
}
