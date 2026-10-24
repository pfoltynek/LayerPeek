using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Xml.Linq;

using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

using Microsoft.Win32;

using LayerPeek.App.E2E.Tests.Infrastructure;
using LayerPeek.Tests.Shared;

namespace LayerPeek.App.E2E.Tests;

/// <summary>
/// The Velopack installer of the release build (T-017), really installed and uninstalled on this machine (user decision
/// 2026-10-24, only through <c>tools/run-e2e.ps1</c> with its warning):
/// <list type="number">
/// <item>The logs of a <c>.zip</c> or development build stay in <c>%LOCALAPPDATA%\LayerPeek\logs</c>, inside Velopack's
/// installation folder (decision 2026-10-24). With that folder not empty, an interactive <c>Setup.exe</c> asks first (the
/// "overwrite/repair" dialog); cancelling it installs nothing and keeps the logs.</item>
/// <item>The silent install answers that dialog itself and replaces the folder: the old logs go. It needs no admin rights
/// (UAC), creates the Start menu shortcut with the AppUserModelID <c>LayerPeek</c> and, by the install hook, the
/// <c>ToastActivatorCLSID</c> and the COM registration of the toast activator.</item>
/// <item>The uninstall leaves none of them, no notification settings, no autostart shortcut and no taskbar choice of the
/// tray icon.</item>
/// </list>
/// The developer's logs are copied away before and back afterwards (journal). Velopack's own log in
/// <c>%LOCALAPPDATA%\velopack</c> is a known leftover (accepted 2026-10-24); the restore deletes what this test created of
/// it. The installed app is never started: it would run on the user's own settings and passwords.
/// </summary>
public sealed class VelopackInstallerTests(E2EFixture fixture)
{
    private const string Runtime = "win-x64";

    private const uint WmClose = 0x0010;

    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(3);

    private static readonly TimeSpan DialogTimeout = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan FolderTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    [Trait(SystemChanges.Trait, SystemChangeTraits.Installer)]
    public async Task SilentInstallRegistersToastsWithoutAdminRightsAndUninstallLeavesNothingBehind()
    {
        SystemChanges.Require();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        // The installer is the release build itself, not the exe under test: one install per run is enough.
        Assert.SkipWhen(E2EFixture.IsNativeAot, "The installer of the release build is tested once, in the pass against the Debug exe.");
        Assert.SkipUnless(RuntimeInformation.OSArchitecture == Architecture.X64, $"The {Runtime} installer runs on x64 Windows only.");
        string setup = SetupPath();
        Assert.SkipUnless(File.Exists(setup), $"No installer {setup}: build it first with pwsh scripts/release-build.ps1 (T-055, T-017).");
        Assert.SkipWhen(InstallerKind.IsInstalled(), $"LayerPeek is installed on this machine ({InstallerKind.InstallFolder}); the test never touches a real installation.");
        Assert.SkipWhen(KeyExists(InstallerKind.UninstallSubKey), $@"HKCU\{InstallerKind.UninstallSubKey} exists (a real installation); the test never touches it.");
        Assert.SkipWhen(IsElevated(), "The tests run as administrator: an install without UAC cannot be told from one with it.");
        // Its log would be open and the folder could not be replaced (the user's app is never closed by the tests).
        Assert.SkipWhen(Process.GetProcessesByName("LayerPeek").Length > 0, "A LayerPeek runs; its log in the installation folder would be open.");

        await using SystemChange<InstallerState> change = await SystemChanges.InstallerAsync(Version, cancellationToken);
        InstallerKind.BackUpFolder();
        if (!change.Original.InstallFolderExists)
        {
            // Like a machine that ran the .zip before: a log in the folder (the restore deletes the whole folder again).
            string probe = Path.Combine(InstallerKind.InstallFolder, "logs", "LayerPeek.log");
            Directory.CreateDirectory(Path.GetDirectoryName(probe)!);
            await File.WriteAllTextAsync(probe, "E2E stand-in of a log of the .zip version (T-017)\n", cancellationToken);
        }

        IReadOnlyList<string> logsBefore = InstallerKind.Files(InstallerKind.InstallFolder);
        Assert.NotEmpty(logsBefore);

        // 1. Interactive Setup.exe with the folder not empty: the overwrite/repair dialog. Closing it cancels: nothing is installed,
        //    the logs stay.
        string dialog = await CancelOverwriteDialogAsync(setup, cancellationToken);
        TestContext.Current.SendDiagnosticMessage($"Overwrite dialog of Setup.exe: {dialog}");
        Assert.False(InstallerKind.IsInstalled(), "Cancelling the overwrite dialog installed the app.");
        Assert.Equal(logsBefore, InstallerKind.Files(InstallerKind.InstallFolder));

        // 2. Silent install. Without the shell (UseShellExecute = false) Windows never asks for elevation: an installer
        //    that needed admin rights would fail with ERROR_ELEVATION_REQUIRED instead of showing UAC.
        Assert.Equal(0, await RunAsync(setup, ["--silent"], InstallTimeout, cancellationToken));
        Assert.True(File.Exists(InstallerKind.InstalledExe), $"The installer did not create {InstallerKind.InstalledExe}.");
        Assert.True(File.Exists(InstallerKind.UpdateExe), $"The installer did not create {InstallerKind.UpdateExe}.");
        // --silent answers the dialog with "repair": the old folder is moved aside and deleted, with the logs (decision 2026-10-24: accepted).
        IReadOnlyList<string> logsAfterInstall = [.. InstallerKind.Files(InstallerKind.InstallFolder).Where(static file => file.StartsWith("logs", StringComparison.OrdinalIgnoreCase))];
        TestContext.Current.SendDiagnosticMessage($"Logs after the silent install: {(logsAfterInstall.Count == 0 ? "none" : string.Join(", ", logsAfterInstall))}");
        Assert.Empty(logsAfterInstall);
        Assert.Equal(change.Original.SetupLeftovers, InstallerKind.SetupLeftoverFolders());
        using (RegistryKey uninstall = Registry.CurrentUser.OpenSubKey(InstallerKind.UninstallSubKey) ?? throw new InvalidOperationException($@"No HKCU\{InstallerKind.UninstallSubKey} after the install."))
        {
            Assert.Contains("--uninstall", (string?)uninstall.GetValue("QuietUninstallString") ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        // A silent install does not start the app.
        Assert.Empty(ProcessesOf(InstallerKind.InstalledExe));

        // Start menu shortcut of Velopack (--shortcuts StartMenuRoot --aumid LayerPeek), completed by the install hook.
        Assert.True(File.Exists(InstallerKind.StartMenuShortcut), $"No Start menu shortcut {InstallerKind.StartMenuShortcut}.");
        ShortcutInfo shortcut = ShortcutProperties.Read(InstallerKind.StartMenuShortcut);
        Assert.Equal(InstallerKind.InstalledExe, shortcut.TargetPath, ignoreCase: true);
        Assert.Equal("LayerPeek", shortcut.AppUserModelId);
        Assert.Equal(new Guid(AppSwitches.ProductionToastActivatorClsid), Guid.Parse(shortcut.ToastActivatorClsid ?? Guid.Empty.ToString()));
        Assert.False(File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppSwitches.ProductionShortcutFileName)), "The installer created a desktop shortcut.");
        Assert.Equal(change.Original.StartupShortcut, InstallerKind.Read().StartupShortcut);

        // COM toast activator of the install hook: Windows starts the installed exe for a click on a toast.
        using (RegistryKey server = Registry.CurrentUser.OpenSubKey(InstallerKind.ActivatorSubKey + @"\LocalServer32") ?? throw new InvalidOperationException("The install hook did not register the toast activator."))
        {
            Assert.Equal($"\"{InstallerKind.InstalledExe}\" -ToastActivated", (string?)server.GetValue(null, null, RegistryValueOptions.DoNotExpandEnvironmentNames), ignoreCase: true);
        }

        // 3. What the user and Explorer add while the app is installed: autostart switched on (the app's own shortcut in
        //    the Startup folder, T-017) and the tray icon shown on the taskbar (T-061; the installed app is never started,
        //    so the entry is made here the way Explorer makes it). The autostart shortcut points to a file that does not
        //    exist outside the installation: Velopack deletes only shortcuts into the installation, so only the uninstall
        //    hook (which deletes the app's Startup\LayerPeek.lnk by its name) can remove it, and a reboot in between starts
        //    nothing.
        string dummyTarget = Path.Combine(E2EFixture.Root, "autostart-target-that-does-not-exist", "LayerPeek.exe");
        ShellStart.CreateShortcut(InstallerKind.StartupShortcut, dummyTarget, string.Empty, E2EFixture.Root);
        Assert.True(File.Exists(InstallerKind.StartupShortcut));
        string trayEntry = NewTrayEntryName();
        using (RegistryKey entry = Registry.CurrentUser.CreateSubKey($@"{InstallerKind.NotifyIconSettingsSubKey}\{trayEntry}", writable: true))
        {
            entry.SetValue("ExecutablePath", InstallerKind.InstalledExe, RegistryValueKind.String);
            entry.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
        }

        // A toast of LayerPeek in the notification center (Windows shows it now: the Start menu shortcut carries the id).
        Assert.True(await ToastHistoryCountAsync(show: true, cancellationToken) > 0, "The test toast of LayerPeek is not in the notification center (notifications switched off?).");

        // 4. Silent uninstall, as Settings > Apps runs it (QuietUninstallString): the uninstall hook runs first.
        Assert.Equal(0, InstallerKind.RunUninstall(InstallTimeout));
        await Wait.UntilAsync(() => !Directory.Exists(InstallerKind.InstallFolder), FolderTimeout, "the installation folder to be deleted");
        Assert.False(KeyExists(InstallerKind.UninstallSubKey), "The uninstall left its Uninstall key.");
        Assert.False(File.Exists(InstallerKind.StartMenuShortcut), "The uninstall left the Start menu shortcut.");
        Assert.False(File.Exists(InstallerKind.StartupShortcut), "The uninstall left the autostart shortcut.");
        Assert.False(KeyExists(InstallerKind.ActivatorSubKey), "The uninstall left the registration of the toast activator.");
        Assert.False(KeyExists(InstallerKind.NotificationSettingsSubKey), "The uninstall left Windows' notification settings of LayerPeek.");
        using (RegistryKey? entry = Registry.CurrentUser.OpenSubKey($@"{InstallerKind.NotifyIconSettingsSubKey}\{trayEntry}"))
        {
            // The entry itself stays (Explorer does not create a deleted one again, T-061), only the choice goes.
            Assert.NotNull(entry);
            Assert.Null(entry.GetValue("IsPromoted"));
        }

        // The uninstall hook cleared the toasts of LayerPeek.
        Assert.Equal(0, await ToastHistoryCountAsync(show: false, cancellationToken));

        Assert.Empty(ProcessesOf(InstallerKind.InstalledExe));
        // Known leftover (accepted 2026-10-24): Velopack's log; the restore deletes it when it was not there before.
        TestContext.Current.SendDiagnosticMessage($"Velopack logs after the uninstall: {string.Join(", ", InstallerKind.Read().VelopackLogs)}");
    }

    /// <summary>
    /// Starts Setup.exe without <c>--silent</c>, waits for its overwrite/repair dialog, records its texts and buttons
    /// (UI Automation, screenshot) and closes it, which Velopack treats as "cancel". An installed app is never left
    /// running: should Setup.exe install without asking, the app it starts is ended at once.
    /// </summary>
    /// <returns>The title, texts and buttons of the dialog.</returns>
    private async Task<string> CancelOverwriteDialogAsync(string setup, CancellationToken cancellationToken)
    {
        ProcessStartInfo start = new(setup) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(setup)! };
        using Process process = Process.Start(start) ?? throw new InvalidOperationException($"{setup} did not start.");
        try
        {
            nint window = 0;
            Stopwatch elapsed = Stopwatch.StartNew();
            while (window == 0)
            {
                foreach (int id in ProcessesOf(InstallerKind.InstalledExe))
                {
                    using Process app = Process.GetProcessById(id);
                    app.Kill(entireProcessTree: true);
                    Assert.Fail("Setup.exe installed and started the app without asking about the folder that is not empty.");
                }

                Assert.False(process.HasExited, $"Setup.exe ended (exit code {(process.HasExited ? process.ExitCode : 0)}) without the overwrite dialog.");
                Assert.True(elapsed.Elapsed < DialogTimeout, "Setup.exe showed no overwrite dialog.");
                window = Native.TopLevelWindows(process.Id).FirstOrDefault();
                if (window == 0)
                {
                    await Task.Delay(250, cancellationToken);
                }
            }

            // Let the dialog finish its layout before it is read.
            await Task.Delay(1000, cancellationToken);
            Screenshots.Save(window, "installer-overwrite-dialog");
            StringBuilder text = new($"\"{Native.Title(window)}\"");
            AutomationElement dialog = fixture.Automation.FromHandle(window);
            foreach (AutomationElement element in dialog.FindAllDescendants())
            {
                if (element.ControlType is ControlType.Button or ControlType.Text && !string.IsNullOrWhiteSpace(element.Name))
                {
                    text.Append(CultureInfo.InvariantCulture, $"; {element.ControlType}: \"{element.Name.ReplaceLineEndings(" ")}\"");
                }
            }

            _ = Native.SendMessageTimeout(window, WmClose, 0, 0, 0x0002, 5000, out _);
            await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            text.Append(CultureInfo.InvariantCulture, $"; exit code after closing it: {process.ExitCode}");
            return text.ToString();
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }

    /// <summary>The installer of this worktree's release build: <c>artifacts/release/&lt;version&gt;/dist</c>.</summary>
    private static string SetupPath() =>
        Path.Combine(TestWorktree.Root, "artifacts", "release", Version, "dist", $"LayerPeek-{Version}-{Runtime}-Setup.exe");

    /// <summary>The version of this worktree (<c>Directory.Build.props</c>), the version of its installer.</summary>
    private static string Version { get; } =
        XDocument.Load(Path.Combine(TestWorktree.Root, "Directory.Build.props")).Descendants("Version").Single().Value.Trim();

    /// <summary>
    /// How many toasts of LayerPeek the notification center holds, after showing one silent test toast when
    /// <paramref name="show"/>. Through Windows PowerShell 5.1, which has the WinRT projection of
    /// <c>ToastNotificationManager</c> (this project targets <c>net11.0-windows</c> without it).
    /// </summary>
    private static async Task<int> ToastHistoryCountAsync(bool show, CancellationToken cancellationToken)
    {
        const string Script = """
            $null = [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime]
            $null = [Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime]
            if ($env:LAYERPEEK_E2E_SHOW_TOAST -eq '1') {
                $xml = New-Object Windows.Data.Xml.Dom.XmlDocument
                $xml.LoadXml('<toast><visual><binding template="ToastGeneric"><text>LayerPeek E2E</text><text>Installer test (T-017)</text></binding></visual><audio silent="true"/></toast>')
                $toast = [Windows.UI.Notifications.ToastNotification]::new($xml)
                $toast.Tag = 'e2e-t017'
                # Straight into the notification center, no banner: a click on one would start the installed Release app.
                $toast.SuppressPopup = $true
                [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('LayerPeek').Show($toast)
                Start-Sleep -Seconds 2
            }
            @([Windows.UI.Notifications.ToastNotificationManager]::History.GetHistory('LayerPeek')).Count
            """;
        string powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        ProcessStartInfo start = new(powershell) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(Script)) })
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["LAYERPEEK_E2E_SHOW_TOAST"] = show ? "1" : "0";
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Windows PowerShell did not start.");
        Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
        string output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(60), cancellationToken);
        string last = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? string.Empty;
        return int.TryParse(last, NumberStyles.None, CultureInfo.InvariantCulture, out int count)
            ? count
            : throw new InvalidOperationException($"Reading the toast history failed (exit code {process.ExitCode}): {output} {await error}");
    }

    private static async Task<int> RunAsync(string exe, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ProcessStartInfo start = new(exe) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(exe)! };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException($"{exe} did not start.");
        try
        {
            await process.WaitForExitAsync(cancellationToken).WaitAsync(timeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{Path.GetFileName(exe)} did not end within {timeout.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)} s.");
        }

        return process.ExitCode;
    }

    private static List<int> ProcessesOf(string exe)
    {
        List<int> ids = [];
        foreach (Process process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)))
        {
            using (process)
            {
                try
                {
                    if (string.Equals(process.MainModule?.FileName, exe, StringComparison.OrdinalIgnoreCase))
                    {
                        ids.Add(process.Id);
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // Ended meanwhile, or another user's process.
                }
            }
        }

        return ids;
    }

    /// <summary>A key name like Explorer's (a large number) that does not exist yet.</summary>
    private static string NewTrayEntryName()
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            string name = Random.Shared.NextInt64(1_000_000_000_000_000_000, long.MaxValue).ToString(CultureInfo.InvariantCulture);
            if (!KeyExists($@"{InstallerKind.NotifyIconSettingsSubKey}\{name}"))
            {
                return name;
            }
        }

        throw new InvalidOperationException("No free name for a tray entry.");
    }

    private static bool KeyExists(string subKey)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(subKey);
        return key is not null;
    }

    private static bool IsElevated()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
