using System.Globalization;

using LayerPeek.App.E2E.Tests.Infrastructure;

namespace LayerPeek.App.E2E.Tests;

/// <summary>
/// A second start through the shell (T-043, T-072): a shortcut to the exe under test, in the app's own test folder (never
/// in the Start menu), opened with <c>ShellExecuteEx</c> like a double-click, only without File Explorer. The second
/// process hands over to the running instance and exits with 0 without a second process or tray icon. With the right to
/// take the foreground (the test process is in the foreground after a click with the real mouse, as after a
/// double-click) the most recently used window comes to the front; without it (the foreground is locked by the test's
/// own window) the window flashes its taskbar button (<c>HSHELL_FLASH</c>); without an open window the settings open.
/// A real click in the Start menu stays a manual check: a process started by File Explorer has no test mode switches
/// and would run on the user's data.
/// </summary>
public sealed class ShellStartTests(E2EFixture fixture)
{
    private static readonly TimeSpan HandoverTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task ShellStartWithoutWindowsOpensTheSettings()
    {
        E2EFixture.Require();
        await using AppInstance app = await AppInstance.StartAsync(fixture, "shell-start-no-window", TestSettings.Json("en", []));
        string shortcut = await PrepareShortcutAsync(app);
        int icons = await CountTestIconsAsync(app);

        await StartThroughShellAndExpectHandoverAsync(app, shortcut);
        nint settings = await app.WaitForWindowAsync(AppWindowKind.Settings);
        Screenshots.Save(settings, "shell-start-1-no-window-opens-settings");

        await ExpectNoMoreTestIconsAsync(app, icons);
    }

    [Fact]
    public async Task ShellStartWithForegroundRightsActivatesTheRecentWindow()
    {
        E2EFixture.Require();
        await using AppInstance app = await AppInstance.StartAsync(fixture, "shell-start-foreground", TestSettings.Json("en", []));
        string shortcut = await PrepareShortcutAsync(app);
        int icons = await CountTestIconsAsync(app);

        // About, then the settings: the settings are the most recently used window. Both minimized.
        nint about = await app.OpenAsync("about", AppWindowKind.Other);
        nint settings = await app.OpenAsync("settings", AppWindowKind.Settings);
        foreach (nint hwnd in new[] { about, settings })
        {
            Native.ShowWindow(hwnd, Native.SwMinimize);
            await Wait.UntilAsync(() => Native.IsIconic(hwnd), TimeSpan.FromSeconds(5), "the window minimized");
        }

        using (ForegroundHolder holder = ForegroundHolder.Create())
        {
            // As after a double-click: the starting process is in the foreground and had the last input.
            await holder.ActivateWithMouseAsync();
            await StartThroughShellAndExpectHandoverAsync(app, shortcut);
            try
            {
                await Wait.UntilAsync(
                    () => Native.GetForegroundWindow() == settings && !Native.IsIconic(settings),
                    HandoverTimeout,
                    "the settings (most recently used) restored in the foreground");
            }
            catch (TimeoutException ex)
            {
                throw new TimeoutException($"{ex.Message} The foreground is {Describe(app, Native.GetForegroundWindow())}.", ex);
            }
            Assert.True(Native.IsIconic(about), "About was not the most recent window and must stay minimized.");
            Screenshots.Save(settings, "shell-start-2-foreground-rights-activate-recent");
        }

        await ExpectNoMoreTestIconsAsync(app, icons);
    }

    [Fact]
    public async Task ShellStartWithoutForegroundRightsFlashesTheRecentWindow()
    {
        E2EFixture.Require();
        await using AppInstance app = await AppInstance.StartAsync(fixture, "shell-start-no-foreground", TestSettings.Json("en", []));
        string shortcut = await PrepareShortcutAsync(app);
        int icons = await CountTestIconsAsync(app);

        string outcome;
        using (ForegroundHolder holder = ForegroundHolder.Create())
        {
            // The windows open in the foreground (the test passes its foreground right to the app), so that neither
            // flashes already because Windows refused to activate it when it opened.
            await holder.ActivateWithMouseAsync();
            ForegroundHolder.AllowForeground(app.Process.Id);
            nint about = await app.OpenAsync("about", AppWindowKind.Other);
            nint settings = await app.OpenAsync("settings", AppWindowKind.Settings);
            await Wait.UntilAsync(() => Native.GetForegroundWindow() == settings, TimeSpan.FromSeconds(5), "the settings opened in the foreground");

            // The foreground belongs to the test's window and is locked: no other process may take it without new input.
            await holder.ActivateWithMouseAsync();
            holder.LockForeground();
            holder.ClearShellEvents();
            await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
            DateTime launched = DateTime.UtcNow;
            Assert.DoesNotContain(holder.ShellEvents, e => e.Code == ForegroundHolder.ShellFlash && e.Window == settings);

            await StartThroughShellAndExpectHandoverAsync(app, shortcut);
            bool Flashed() => holder.ShellEvents.Any(e => e.Code == ForegroundHolder.ShellFlash && e.Window == settings && e.ReceivedUtc >= launched);
            await Wait.UntilAsync(
                () => Flashed() || Native.GetForegroundWindow() == settings,
                HandoverTimeout,
                "the settings (most recently used) flashing their taskbar button");
            string events = string.Join(
                ", ",
                holder.ShellEvents.Select(e => $"{(e.ReceivedUtc - launched).TotalMilliseconds:+0;-0} ms 0x{e.Code:X} {Describe(app, e.Window)}"));
            if (Flashed())
            {
                outcome = $"Flashed (HSHELL_FLASH), the foreground stayed with the test's window. Shell messages (relative to the start): {events}";
                Assert.Equal(holder.Handle, Native.GetForegroundWindow());
            }
            else
            {
                // The foreground rules differ between Windows builds: when Windows let the app take the foreground anyway,
                // the window must at least be restored and in front.
                outcome = $"Windows activated the window instead of flashing it. Shell messages (relative to the start): {events}";
                Assert.False(Native.IsIconic(settings));
                Assert.Equal(settings, Native.GetForegroundWindow());
            }

            Screenshots.Save(settings, "shell-start-3-no-foreground-rights-flash");
            Screenshots.SaveText(outcome, "shell-start-3-no-foreground-rights-outcome");
            Assert.True(Native.IsWindowVisible(about));
        }

        await ExpectNoMoreTestIconsAsync(app, icons);
    }

    /// <summary>
    /// The shortcut to the exe under test in the app's run folder (deleted with it), with the marker argument; and the
    /// proof that a process started through a shortcut gets the test mode variables.
    /// </summary>
    private static async Task<string> PrepareShortcutAsync(AppInstance app)
    {
        string folder = Path.Combine(app.RunDirectory, "shortcut");
        string shortcut = Path.Combine(folder, $"LP E2E second start {Guid.NewGuid().ToString("N")[..8]}.lnk");
        Directory.CreateDirectory(folder);
        await ShellStart.ExpectInheritedEnvironmentAsync(folder, AppSwitches.SuffixVariable, app.Suffix);
        ShellStart.CreateShortcut(shortcut, E2EFixture.AppPath, ShellStart.ShortcutArguments(ShortcutArguments(app)), app.RunDirectory);
        return shortcut;
    }

    /// <summary>The arguments of the shortcut to the app: the marker of its run.</summary>
    private static string[] ShortcutArguments(AppInstance app) => [AppSwitches.MarkerArgument + app.RunDirectory];

    /// <summary>
    /// Opens the shortcut through the shell with the test mode variables of <paramref name="app"/> (and forward-only, so a
    /// start that would become the primary instance exits with 3 instead of running a second app). It exits with 0 within
    /// the handover budget, leaves no second process, logs the handover and adds no entry to the user's Recent items.
    /// </summary>
    private static async Task StartThroughShellAndExpectHandoverAsync(AppInstance app, string shortcut)
    {
        DateTime started = DateTime.Now;
        using ShellStart.StartedProcess second = await ShellStart.StartAppAsync(
            shortcut,
            ShortcutArguments(app),
            new Dictionary<string, string?>
            {
                [AppSwitches.DataDirectoryVariable] = app.DataDirectory,
                [AppSwitches.SuffixVariable] = app.Suffix,
                [AppSwitches.ForwardOnlyVariable] = "1",
                [AppSwitches.TestToastsVariable] = null,
                [AppSwitches.TestCredentialPrefixVariable] = null,
                [AppSwitches.TestStartDelayVariable] = null,
            });
        int? exitCode = await second.WaitForExitAsync(HandoverTimeout);
        Assert.True(exitCode == 0, $"The second start through the shell (pid {second.Id}) exited with {exitCode?.ToString(CultureInfo.InvariantCulture) ?? "nothing within 5 s"}.");
        Assert.Equal([app.Process.Id], AppProcesses.FindMarked(app.RunDirectory));
        Assert.Contains($"[pid {second.Id}] LayerPeek.App.Program: Another instance is running", app.ReadLog(), StringComparison.Ordinal);

        // ShellExecuteEx must not record the shortcut in the user's Recent items.
        string recent = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
        string name = Path.GetFileNameWithoutExtension(shortcut);
        string[] traces = Directory.Exists(recent)
            ? [.. Directory.EnumerateFiles(recent).Where(file => Path.GetFileName(file).Contains(name, StringComparison.OrdinalIgnoreCase) && File.GetLastWriteTime(file) >= started.AddSeconds(-2))]
            : [];
        Assert.True(traces.Length == 0, $"The shell start left a Recent item: {string.Join(", ", traces)}");
    }

    /// <summary>The tray icons of test apps now (icons of killed apps may linger and vanish meanwhile, never appear).</summary>
    private static async Task<int> CountTestIconsAsync(AppInstance app)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                (IReadOnlyList<FlaUI.Core.AutomationElements.AutomationElement> icons, _) = await NotificationArea.FindTestIconsAsync(app.Automation);
                // Escape goes to the foreground window: only while the flyout is still shown (it may have closed by itself).
                NotificationArea.CloseOverflow(NotificationArea.Overflow(app.Automation) is not null);
                return icons.Count;
            }
            catch (TimeoutException ex) when (attempt < 2)
            {
                // The overflow flyout sometimes does not open on the first try (seen once in a full run): report and look again.
                // Text only: a screenshot of the whole desktop would capture the user's other windows for a mere retry.
                Screenshots.SaveText(
                    $"{ex.Message}{Environment.NewLine}{string.Join(Environment.NewLine, NotificationArea.DescribeAll(app.Automation))}",
                    "shell-start-tray-icons-not-found");
                NotificationArea.CloseOverflow(NotificationArea.Overflow(app.Automation) is not null);
            }
        }
    }

    private static async Task ExpectNoMoreTestIconsAsync(AppInstance app, int before)
    {
        int after = await CountTestIconsAsync(app);
        Assert.True(after <= before, $"The second start added a tray icon: {before} test icons before, {after} after.");
    }

    /// <summary>A window for the report; the title only of the app's and the test's own windows (others are the user's).</summary>
    private static string Describe(AppInstance app, nint hwnd)
    {
        Native.GetWindowThreadProcessId(hwnd, out uint process);
        bool own = process == (uint)app.Process.Id || process == (uint)Environment.ProcessId;
        return $"0x{hwnd:X} pid={process} class={Native.ClassName(hwnd)}" + (own ? $" title='{Native.Title(hwnd)}'" : string.Empty);
    }
}
