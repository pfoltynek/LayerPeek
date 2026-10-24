using System.Drawing;
using System.Globalization;
using System.Text;
using System.Text.Json;

using FlaUI.Core.AutomationElements;

using PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;

namespace PrusaTrayDiagnostic.App.E2E.Tests;

/// <summary>
/// Self-tests of the system change infrastructure of T-065 in the end-to-end run. Without system changes (also with
/// <c>tools/run-e2e.ps1 -NoSystemChanges</c>): the crash and stale-entry tests with the probe (a registry value of the
/// tests' own) and the guard. With the confirmed warning (trait <see cref="SystemChanges.Trait"/>): theme, tray icon
/// promotion and Do Not Disturb, each applied, checked, restored and checked, and the theme change of a killed process.
/// </summary>
public sealed class SystemChangeTests(E2EFixture fixture)
{
    private static readonly TimeSpan TaskbarTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan WatchdogTimeout = TimeSpan.FromSeconds(90);

    [Fact]
    public async Task ProbeChangeOfAKilledProcessIsRestoredByTheStartCleanup()
    {
        E2EFixture.Require();
        await KilledChildIsRestoredAsync(SystemChanges.Probe, watchdog: false);
    }

    [Fact]
    public async Task ProbeChangeOfAKilledProcessIsRestoredByTheWatchdog()
    {
        E2EFixture.Require();
        await KilledChildIsRestoredAsync(SystemChanges.Probe, watchdog: true);
    }

    [Fact]
    [Trait(SystemChanges.Trait, SystemChangeTraits.Theme)]
    public async Task ThemeChangeOfAKilledProcessIsRestoredByTheStartCleanup()
    {
        SystemChanges.Require();
        ThemeState original = await SystemChanges.Theme.ReadStateAsync(null, TestContext.Current.CancellationToken);
        await KilledChildIsRestoredAsync(SystemChanges.Theme, watchdog: false);
        await ThemeKind.WaitForTaskbarAsync(original.TaskbarIsLight, TaskbarTimeout);
    }

    [Fact]
    [Trait(SystemChanges.Trait, SystemChangeTraits.Theme)]
    public async Task ThemeChangeOfAKilledProcessIsRestoredByTheWatchdog()
    {
        SystemChanges.Require();
        ThemeState original = await SystemChanges.Theme.ReadStateAsync(null, TestContext.Current.CancellationToken);
        await KilledChildIsRestoredAsync(SystemChanges.Theme, watchdog: true);
        // The watchdog's broadcast reached Explorer too, not only the registry.
        await ThemeKind.WaitForTaskbarAsync(original.TaskbarIsLight, TaskbarTimeout);
    }

    [Fact]
    public async Task EntryOfACrashedRunIsRestoredBeforeTheNewOriginalIsRead()
    {
        E2EFixture.Require();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        ProbeKind probe = SystemChanges.Probe;
        Assert.Null((await probe.ReadStateAsync(null, cancellationToken)).Value);
        try
        {
            // A crashed run: its entry says "missing", the state it set is still there.
            ProcessOwner crashed = new(int.MaxValue - 1, 0, "crashed-run");
            Directory.CreateDirectory(SystemChanges.JournalDirectory);
            SystemChangeEntry stale = new(probe.Name, null, crashed, DateTimeOffset.Now, probe.Serialize(new ProbeState(null)), probe.Describe(null, new ProbeState(null)));
            await File.WriteAllTextAsync(SystemChanges.Journal.FileOf(probe.Name, null), JsonSerializer.Serialize(stale, SystemChangeJson.Default.SystemChangeEntry), cancellationToken);
            await probe.ApplyStateAsync(null, new ProbeState("left-by-the-crash"), cancellationToken);

            await using (SystemChange<ProbeState> change = await SystemChanges.ProbeAsync(cancellationToken))
            {
                Assert.Null(change.Original.Value);
                Assert.Equal(ProcessOwner.Current, change.Entry.Owner);
                await change.ApplyAsync(new ProbeState("test"), cancellationToken);
            }

            Assert.Null((await probe.ReadStateAsync(null, cancellationToken)).Value);
            Assert.DoesNotContain(SystemChanges.Journal.FileOf(probe.Name, null), SystemChanges.Journal.PendingFiles());
        }
        finally
        {
            await SystemChanges.Journal.RestorePendingAsync(includeOwn: false, TimeSpan.Zero, CancellationToken.None);
        }
    }

    [Fact]
    public async Task SystemChangeTestWithoutTheWarningIsSkippedAndChangesNothing()
    {
        E2EFixture.Require();
        ProbeKind guard = SystemChanges.GuardProbe;
        Assert.Null((await guard.ReadStateAsync(null, TestContext.Current.CancellationToken)).Value);

        using ChildTestProcess child = ChildTestProcess.Start(
            $"{typeof(SystemChangeChild).FullName}.{nameof(SystemChangeChild.GuardProbeChangesOnlyWithTheConfirmedWarning)}",
            new Dictionary<string, string?> { [SystemChanges.Variable] = null, [SystemChangeWatchdog.Variable] = "0" });
        await child.WaitForExitAsync(TimeSpan.FromMinutes(1));

        Screenshots.SaveText(child.Output, "system-changes-guard-child");
        Assert.True(child.Process.ExitCode == 0, child.Output);
        Assert.Contains("Total: 1, Errors: 0, Failed: 0, Skipped: 1", child.Output, StringComparison.Ordinal);
        Assert.Contains("tools/run-e2e.ps1, which shows the warning first", child.Output, StringComparison.Ordinal);
        Assert.Null((await guard.ReadStateAsync(null, TestContext.Current.CancellationToken)).Value);
        Assert.DoesNotContain(SystemChanges.Journal.FileOf(guard.Name, null), SystemChanges.Journal.PendingFiles());
    }

    [Fact]
    [Trait(SystemChanges.Trait, SystemChangeTraits.Theme)]
    public async Task ThemeSwitchesTheTaskbarAndIsRestored()
    {
        SystemChanges.Require();
        StringBuilder report = new();
        SystemChange<ThemeState> theme = await SystemChanges.ThemeAsync(TestContext.Current.CancellationToken);
        try
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"Windows {Environment.OSVersion.Version}; original: {theme.Original}; ColorPrevalence={ThemeKind.ColorPrevalence()}");

            await theme.ApplyAsync(ThemeState.Light, TestContext.Current.CancellationToken);
            double light = await ThemeKind.WaitForTaskbarAsync(light: true, TaskbarTimeout);
            SaveTaskbar("theme-1-light");
            report.AppendLine(CultureInfo.InvariantCulture, $"Light: taskbar brightness {light:0}");

            await theme.ApplyAsync(ThemeState.Dark, TestContext.Current.CancellationToken);
            double dark = await ThemeKind.WaitForTaskbarAsync(light: false, TaskbarTimeout, light);
            SaveTaskbar("theme-2-dark");
            report.AppendLine(CultureInfo.InvariantCulture, $"Dark: taskbar brightness {dark:0}");

            await theme.DisposeAsync();
            Assert.Equal(theme.Original, await SystemChanges.Theme.ReadStateAsync(null, TestContext.Current.CancellationToken));
            double restored = await ThemeKind.WaitForTaskbarAsync(theme.Original.TaskbarIsLight, TaskbarTimeout, light);
            SaveTaskbar("theme-3-restored");
            report.AppendLine(CultureInfo.InvariantCulture, $"Restored: taskbar brightness {restored:0}");
        }
        finally
        {
            await theme.DisposeAsync();
            Screenshots.SaveText(report.ToString(), "theme-report");
        }
    }

    [Fact]
    [Trait(SystemChanges.Trait, SystemChangeTraits.TrayPromotion)]
    public async Task TestTrayIconIsPromotedOnTheTaskbarAndRestored()
    {
        SystemChanges.Require();
        SystemChange<TrayIconPromotionState> tray = fixture.TrayIconPromotion
            ?? throw new InvalidOperationException("The run-wide tray icon change is missing although the warning listed TrayPromotion.");
        StringBuilder report = new();
        report.AppendLine(CultureInfo.InvariantCulture, $"Entries of the tested exe at the start of the run: {tray.Original}");

        TrayIconPromotionState promoted;
        await using (AppInstance app = await AppInstance.StartAsync(fixture, "tray-promotion", TestSettings.Json("en", [])))
        {
            promoted = await tray.PromoteAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            report.AppendLine(CultureInfo.InvariantCulture, $"Promoted: {promoted}");
            await Wait.UntilAsync(() => NotificationArea.TestIconsOnTaskbar(app.Automation).Count > 0, TimeSpan.FromSeconds(10), "the test tray icon on the taskbar");
            AutomationElement icon = NotificationArea.TestIconsOnTaskbar(app.Automation)[0];
            report.AppendLine(CultureInfo.InvariantCulture, $"On the taskbar: {NotificationArea.Describe(icon)}");
            SaveTaskbar("tray-1-promoted");
            await app.QuitAsync();
        }

        try
        {
            await tray.RestoreAsync(TestContext.Current.CancellationToken);
            TrayIconPromotionState restored = await tray.ReadAsync(TestContext.Current.CancellationToken);
            report.AppendLine(CultureInfo.InvariantCulture, $"Restored: {restored}");

            // Every entry is kept (T-065 Q3, decision 2026-10-22): the original ones with their IsPromoted, the ones
            // Explorer created during the run without it.
            Assert.True(SystemChanges.TrayIconPromotion.Matches(tray.Original, restored), report.ToString());
            Assert.All(promoted.Entries, entry => Assert.Contains(restored.Entries, now => string.Equals(now.Key, entry.Key, StringComparison.OrdinalIgnoreCase)));
            Assert.All(
                restored.Entries.Where(now => !tray.Original.Entries.Any(before => string.Equals(before.Key, now.Key, StringComparison.OrdinalIgnoreCase))),
                now => Assert.True(TrayIconPromotionKind.IsNotPromoted(now.IsPromoted), $"{now.Key}: IsPromoted={now.IsPromoted}"));
        }
        finally
        {
            Screenshots.SaveText(report.ToString(), "tray-report");
        }
    }

    [Fact]
    [Trait(SystemChanges.Trait, SystemChangeTraits.DoNotDisturb)]
    public async Task DoNotDisturbIsSwitchedAndRestored()
    {
        SystemChanges.Require();
        StringBuilder report = new();
        SystemChange<DoNotDisturbState> dnd = await SystemChanges.DoNotDisturbAsync(TestContext.Current.CancellationToken);
        try
        {
            bool original = dnd.Original.Enabled;
            report.AppendLine(CultureInfo.InvariantCulture, $"Original: {(original ? "on" : "off")}; SHQueryUserNotificationState {DoNotDisturbKind.QueryUserNotificationState()}");

            await dnd.ApplyAsync(new DoNotDisturbState(!original), TestContext.Current.CancellationToken);
            Assert.Equal(!original, (await dnd.ReadAsync(TestContext.Current.CancellationToken)).Enabled);
            report.AppendLine(CultureInfo.InvariantCulture, $"Switched to {(!original ? "on" : "off")}; SHQueryUserNotificationState {DoNotDisturbKind.QueryUserNotificationState()}");

            await dnd.ApplyAsync(DoNotDisturbState.Off, TestContext.Current.CancellationToken);
            Assert.False((await dnd.ReadAsync(TestContext.Current.CancellationToken)).Enabled);
            report.AppendLine(CultureInfo.InvariantCulture, $"Off; SHQueryUserNotificationState {DoNotDisturbKind.QueryUserNotificationState()}");

            await dnd.DisposeAsync();
            Assert.Equal(original, (await SystemChanges.DoNotDisturb.ReadStateAsync(null, TestContext.Current.CancellationToken)).Enabled);
            report.AppendLine(CultureInfo.InvariantCulture, $"Restored; SHQueryUserNotificationState {DoNotDisturbKind.QueryUserNotificationState()}");
        }
        finally
        {
            await dnd.DisposeAsync();
            Screenshots.SaveText(report.ToString(), "do-not-disturb-report");
        }
    }

    /// <summary>
    /// A child test process applies a change of <paramref name="kind"/> and is killed. Without the watchdog the start
    /// cleanup (layer 2, <see cref="SystemChangeJournal.RestorePendingAsync"/>) restores it; with it the watchdog alone
    /// does (layer 4: this test only waits for the entry to go).
    /// </summary>
    internal static async Task KilledChildIsRestoredAsync(ISystemChangeKind kind, bool watchdog)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        JsonElement original = await kind.ReadAsync(null, cancellationToken);
        string entryFile = SystemChanges.Journal.FileOf(kind.Name, null);
        Assert.DoesNotContain(entryFile, SystemChanges.Journal.PendingFiles());
        string ready = Path.Combine(E2EFixture.Root, $"child-ready-{Guid.NewGuid():N}.txt");
        string label = $"killed-{kind.Name}-{(watchdog ? "watchdog" : "start-cleanup")}";
        ChildTestProcess? child = null;
        try
        {
            child = ChildTestProcess.Start(
                $"{typeof(SystemChangeChild).FullName}.{nameof(SystemChangeChild.ApplyAChangeAndWaitToBeKilled)}",
                new Dictionary<string, string?>
                {
                    [SystemChangeChild.KindVariable] = kind.Name,
                    [SystemChangeChild.ReadyVariable] = ready,
                    [SystemChangeChild.ParentVariable] = Environment.ProcessId.ToString(CultureInfo.InvariantCulture),
                    [SystemChangeWatchdog.Variable] = watchdog ? "1" : "0",
                });
            await Wait.UntilAsync(() => File.Exists(ready) || child.Process.HasExited, TimeSpan.FromMinutes(1), "the child to apply its change");
            Assert.False(child.Process.HasExited, $"The child ended before it was killed:{Environment.NewLine}{child.Output}");
            Assert.Contains(entryFile, SystemChanges.Journal.PendingFiles());
            Assert.False(kind.Matches(original, await kind.ReadAsync(null, cancellationToken)), "The child did not change anything.");

            child.Process.Kill();
            await child.WaitForExitAsync(TimeSpan.FromSeconds(30));
            Screenshots.SaveText(child.Output, label);

            if (watchdog)
            {
                await Wait.UntilAsync(() => !File.Exists(entryFile), WatchdogTimeout, "the watchdog of the killed child to restore its change");
                // Its restore is done only when it ended: until then it would also take the entry of the next test's child.
                await SystemChangeWatchdog.WaitUntilGoneAsync(child.Process.Id, WatchdogTimeout);
            }
            else
            {
                Assert.Contains(entryFile, SystemChanges.Journal.PendingFiles());
                SystemChangeRestoreReport report = await SystemChanges.Journal.RestorePendingAsync(includeOwn: false, TimeSpan.FromSeconds(10), cancellationToken);
                Screenshots.SaveText(report.ToString(), label + "-restore");
                Assert.True(report.Success, report.ToString());
                Assert.Single(report.Restored);
            }

            Assert.True(kind.Matches(original, await kind.ReadAsync(null, cancellationToken)), $"The {kind.Name} state is not back.");
            Assert.DoesNotContain(entryFile, SystemChanges.Journal.PendingFiles());
        }
        finally
        {
            child?.Dispose();
            // Nothing stays behind even when a check above failed.
            await SystemChanges.Journal.RestorePendingAsync(includeOwn: false, TimeSpan.FromSeconds(10), CancellationToken.None);
            File.Delete(ready);
        }
    }

    private static void SaveTaskbar(string name)
    {
        using Bitmap taskbar = ThemeKind.CaptureTaskbar();
        Screenshots.Save(taskbar, name);
    }
}
