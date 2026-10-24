using System.Diagnostics;

using Microsoft.Win32;

using LayerPeek.App.E2E.Tests.Infrastructure;

namespace LayerPeek.App.E2E.Tests;

/// <summary>
/// The opt-in toasts of the test mode (T-067) on the real exe: the test app gets its own Start menu shortcut
/// "LayerPeek E2E" (app id <c>LayerPeek.E2E</c>); quitting removes it with the toasts of the test
/// app, and after a crash the command line mode <c>--layerpeek-e2e-clear-toasts</c> does, without starting an app. The
/// production shortcut is never touched. No toast is shown here (no printer); T-068 tests real toasts.
/// <para>
/// Only these end-to-end tests touch the shared Start menu and the test app's notifications: runs of all worktrees take
/// turns on the desktop lock, so a unit test run elsewhere never loses its shortcut to them. Every test removes the test
/// shortcut in <c>finally</c>, and starts by removing a leftover of a crashed run.
/// </para>
/// <para>
/// The tests that start a test app with toasts change the system (its shortcut and notifications, T-065/T-068, user
/// decision 2026-10-22): they carry the trait <see cref="SystemChanges.Trait"/>, run only after the warning of
/// <c>tools/run-e2e.ps1</c> (skipped with <c>-NoSystemChanges</c>) and journal the change, so that a crash is cleaned up
/// by the next start, the watchdog or <c>-RestoreOnly</c>.
/// </para>
/// </summary>
public sealed class TestModeOsIntegrationTests(E2EFixture fixture)
{
    private static readonly TimeSpan ShortcutTimeout = TimeSpan.FromSeconds(20);

    private static readonly TimeSpan ClearToastsTimeout = TimeSpan.FromSeconds(30);

    private const string NotificationSettings = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\";

    private static string StartMenu => Environment.GetFolderPath(Environment.SpecialFolder.Programs);

    private static string TestShortcut => Path.Combine(StartMenu, AppSwitches.TestShortcutFileName);

    private static string ProductionShortcut => Path.Combine(StartMenu, AppSwitches.ProductionShortcutFileName);

    [Fact]
    [Trait(SystemChanges.Trait, SystemChangeTraits.Toasts)]
    public async Task TestToastsUseTheirOwnShortcutAndQuitRemovesIt()
    {
        SystemChanges.Require();
        ProductionState production = ProductionState.Read();
        bool settingsKeyBefore = TestAppSettingsKeyExists();
        await RemoveLeftoverAsync();
        try
        {
            // Journaled (T-068): a crash is cleaned up by the next start, the watchdog or -RestoreOnly.
            await using SystemChange<TestToastsState> toasts = await SystemChanges.TestToastsAsync(E2EFixture.AppPath, TestContext.Current.CancellationToken);
            await using (AppInstance app = await AppInstance.StartAsync(
                fixture,
                "test-toasts",
                TestSettings.Json("en", []),
                switches: new Dictionary<string, string> { [AppSwitches.TestToastsVariable] = "1" }))
            {
                // The toast service registers the test app at startup (T-010), with the test shortcut.
                await Wait.UntilAsync(() => File.Exists(TestShortcut), ShortcutTimeout, "the Start menu shortcut of the test app");
                AssertIsTheTestShortcut(TestShortcut);
                AssertIsTheTestActivator(app);

                await app.QuitAsync();
                Assert.Equal(0, app.Process.ExitCode);
                Assert.False(File.Exists(TestShortcut), "Quitting left the shortcut of the test app in the Start menu.");
                Assert.Contains("the Start menu shortcut of the test app was deleted", app.ReadLog(), StringComparison.Ordinal);
                Assert.False(TestToastsKind.ActivatorRegistered(), "Quitting left the toast activator of the test app registered.");
                Assert.Contains("the toast activator of the test app was unregistered", app.ReadLog(), StringComparison.Ordinal);
            }

            AssertProductionActivatorUnchanged(production);

            production.AssertUnchanged();
            Assert.Equal(settingsKeyBefore, TestAppSettingsKeyExists());
        }
        finally
        {
            await RemoveLeftoverAsync();
        }
    }

    [Fact]
    public async Task WithoutTheSwitchTheTestModeCreatesNoShortcut()
    {
        E2EFixture.Require();
        ProductionState production = ProductionState.Read();
        await RemoveLeftoverAsync();
        await using AppInstance app = await AppInstance.StartAsync(fixture, "no-test-toasts", TestSettings.Json("en", []));
        // Long enough for the toast service to have registered itself, had it been on.
        await Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);

        Assert.False(File.Exists(TestShortcut));
        // Nor a toast activator (T-068): without toasts nothing is registered with Windows.
        Assert.False(TestToastsKind.ActivatorRegistered());
        await app.QuitAsync();
        production.AssertUnchanged();
    }

    [Fact]
    [Trait(SystemChanges.Trait, SystemChangeTraits.Toasts)]
    public async Task ClearToastsModeRemovesWhatACrashedTestAppLeftAndStartsNoApp()
    {
        SystemChanges.Require();
        ProductionState production = ProductionState.Read();
        bool settingsKeyBefore = TestAppSettingsKeyExists();
        await RemoveLeftoverAsync();
        try
        {
            // Journaled (T-068): a crash is cleaned up by the next start, the watchdog or -RestoreOnly.
            await using SystemChange<TestToastsState> toasts = await SystemChanges.TestToastsAsync(E2EFixture.AppPath, TestContext.Current.CancellationToken);

            // A test app with toasts that crashes (killed): its shortcut stays behind.
            await using (AppInstance app = await AppInstance.StartAsync(
                fixture,
                "test-toasts-crash",
                TestSettings.Json("en", []),
                switches: new Dictionary<string, string> { [AppSwitches.TestToastsVariable] = "1" }))
            {
                await Wait.UntilAsync(() => File.Exists(TestShortcut), ShortcutTimeout, "the Start menu shortcut of the test app");
                app.Process.Kill(entireProcessTree: true);
                await app.Process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            }

            Assert.True(File.Exists(TestShortcut), "The killed app removed its shortcut; nothing left to test.");
            Assert.True(TestToastsKind.ActivatorRegistered(), "The killed app left no toast activator; nothing left to test.");

            ClearToastsResult cleanup = await ClearToasts.RunAsync(E2EFixture.AppPath, ClearToastsTimeout);

            Assert.True(cleanup.ExitCode == 0, $"--layerpeek-e2e-clear-toasts exited with {cleanup.ExitCode}: {cleanup.Error}");
            Assert.Equal(string.Empty, cleanup.Error);
            Assert.False(File.Exists(TestShortcut), "The cleanup left the shortcut of the test app.");
            Assert.False(TestToastsKind.ActivatorRegistered(), "The cleanup left the toast activator of the test app (T-068).");
            // No app ran: the test mode switches it got would have created a log in its test data folder.
            Assert.False(cleanup.StartedApp, "The cleanup started the app (it wrote into the test data folder).");

            // Nothing left to remove: still 0.
            cleanup = await ClearToasts.RunAsync(E2EFixture.AppPath, ClearToastsTimeout);
            Assert.True(cleanup.ExitCode == 0, $"A second cleanup exited with {cleanup.ExitCode}: {cleanup.Error}");

            production.AssertUnchanged();
            AssertProductionActivatorUnchanged(production);
            Assert.Equal(settingsKeyBefore, TestAppSettingsKeyExists());
        }
        finally
        {
            await RemoveLeftoverAsync();
        }
    }

    /// <summary>
    /// A shortcut or activator of an earlier crashed run (or of a failed assertion above) goes before and after every test.
    /// </summary>
    private static async Task RemoveLeftoverAsync()
    {
        if (!File.Exists(TestShortcut) && !TestToastsKind.ActivatorRegistered())
        {
            return;
        }

        ClearToastsResult cleanup = await ClearToasts.RunAsync(E2EFixture.AppPath, ClearToastsTimeout);
        bool shortcutLeft = File.Exists(TestShortcut);
        bool activatorLeft = TestToastsKind.ActivatorRegistered();
        if (shortcutLeft || activatorLeft)
        {
            // The app's own cleanup failed (reported by the test that needs it); never leave them.
            File.Delete(TestShortcut);
            Registry.CurrentUser.DeleteSubKeyTree(TestToastsKind.ActivatorSubKey(AppSwitches.TestToastActivatorClsid), throwOnMissingSubKey: false);
            Assert.Fail($"--layerpeek-e2e-clear-toasts ({cleanup.ExitCode}: {cleanup.Error}) did not remove{(shortcutLeft ? $" '{TestShortcut}'" : string.Empty)}{(activatorLeft ? " the test toast activator" : string.Empty)}; deleted directly.");
        }
    }

    /// <summary>
    /// The test app's toast activator (T-068): Windows starts the exe under test with the marker, the test mode switches of
    /// this app and nothing of the production app.
    /// </summary>
    private static void AssertIsTheTestActivator(AppInstance app)
    {
        Assert.True(TestToastsKind.ActivatorRegistered(), "The test app did not register its toast activator.");
        using RegistryKey? server = Registry.CurrentUser.OpenSubKey(TestToastsKind.ActivatorSubKey(AppSwitches.TestToastActivatorClsid) + @"\LocalServer32");
        string commandLine = server?.GetValue(null) as string ?? string.Empty;
        Assert.StartsWith("\"" + Path.GetFullPath(E2EFixture.AppPath) + "\" -ToastActivated ", commandLine, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(AppSwitches.TestEnvironmentArgument + AppSwitches.DataDirectoryVariable + "=" + app.DataDirectory, commandLine, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(AppSwitches.TestEnvironmentArgument + AppSwitches.SuffixVariable + "=" + app.Suffix, commandLine, StringComparison.Ordinal);
        Assert.Contains(AppSwitches.TestEnvironmentArgument + AppSwitches.TestToastsVariable + "=1", commandLine, StringComparison.Ordinal);
        Assert.Contains(AppSwitches.MarkerArgument + app.RunDirectory, commandLine, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(AppSwitches.ForwardOnlyVariable, commandLine, StringComparison.Ordinal);
    }

    private static void AssertProductionActivatorUnchanged(ProductionState production) =>
        Assert.Equal(production.Activator, ProductionState.Read().Activator);

    /// <summary>The shortcut points to the exe under test and carries the test app id (UTF-16 in the property store).</summary>
    private static void AssertIsTheTestShortcut(string path)
    {
        Assert.True(E2EFixture.BinaryContains(path, Path.GetFileName(E2EFixture.AppPath)), "The test shortcut does not point to the exe under test.");
        Assert.True(E2EFixture.BinaryContains(path, AppSwitches.AppUserModelId), "The test shortcut lacks the app id of the test app.");
    }

    /// <summary>Windows' notification settings of the test app (created by Windows after its first toast, T-068).</summary>
    private static bool TestAppSettingsKeyExists()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(NotificationSettings + AppSwitches.AppUserModelId);
        return key is not null;
    }

    /// <summary>
    /// The production shortcut, byte for byte (or missing). Its notification settings are not compared: the user's own
    /// instance may show a toast meanwhile, and Windows updates them then.
    /// </summary>
    private sealed class ProductionState
    {
        private readonly byte[]? _shortcut;

        private ProductionState(byte[]? shortcut, string activator)
        {
            _shortcut = shortcut;
            Activator = activator;
        }

        /// <summary>The registration of the production toast activator (T-068); the tests never write it.</summary>
        public string Activator { get; }

        public static ProductionState Read() => new(
            File.Exists(ProductionShortcut) ? File.ReadAllBytes(ProductionShortcut) : null,
            RegistryKeys.Read(TestToastsKind.ActivatorSubKey(AppSwitches.ProductionToastActivatorClsid) + @"\LocalServer32").ToString());

        public void AssertUnchanged() => Assert.Equal(_shortcut, Read()._shortcut);
    }
}
