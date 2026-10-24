using System.Text;

using FlaUI.Core.AutomationElements;

using PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;
using PrusaTrayDiagnostic.Simulator.Printers;

namespace PrusaTrayDiagnostic.App.E2E.Tests;

/// <summary>
/// The password of a printer in the real Windows Credential Manager (T-073, for T-002 and T-052): the test app keeps it
/// under a test prefix of its own (T-067, <c>PrusaTrayDiagnostic.E2E/&lt;worktree&gt;/&lt;random&gt;</c>, session only).
/// Saved from the settings, read again after a restart, deleted with the printer; it never reaches the log or
/// <c>settings.json</c>. Only entries under the test prefix are read or deleted (<see cref="TestCredentials"/>); the
/// change is journaled, so a crash is cleaned up by the next start, the watchdog or <c>-RestoreOnly</c>, and the run
/// sweeps <c>PrusaTrayDiagnostic.E2E/*</c> at its start and end.
/// </summary>
public sealed class CredentialManagerTests(E2EFixture fixture)
{
    private const string PrinterId = "e2e-credentials";

    private static readonly TimeSpan StateTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    [Trait(SystemChanges.Trait, SystemChangeTraits.Credentials)]
    public async Task PasswordIsSavedLoadedAfterARestartAndDeletedWithThePrinter()
    {
        SystemChanges.Require();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string prefix = TestCredentials.NewPrefix();
        string target = $"{prefix}/printer/{PrinterId}";
        // Unique, so that finding it in a file can only mean a leak.
        string password = "E2E-pw-" + Guid.NewGuid().ToString("N")[..16];
        Dictionary<string, string> switches = new() { [AppSwitches.TestCredentialPrefixVariable] = prefix };

        await using SystemChange<CredentialsState> credentials = await SystemChanges.CredentialsAsync(prefix, cancellationToken);
        await using SimulatedPrinter printer = await SimulatedPrinter.StartAsync(
            new SimulatedPrinterOptions { Scenario = PrinterScenario.Idle, Password = password },
            cancellationToken);
        string settingsJson = TestSettings.Json("en", [new TestPrinter(PrinterId, "E2E credentials printer", printer.Address())]);

        // 1. No password yet: the printer rejects the login. The password typed into the settings and saved goes to
        //    Credential Manager (session only) and the monitoring logs in with it.
        await using (AppInstance app = await AppInstance.StartAsync(fixture, "credentials-1-save", settingsJson, switches: switches))
        {
            AutomationElement diagnostics = await OpenDiagnosticsAsync(app);
            await WaitForStateAsync(diagnostics, "StateUnauthorized", "the rejected login without a password");
            Assert.Null(TestCredentials.Persist(target));

            nint settings = await app.OpenAsync("settings", AppWindowKind.Settings);
            AutomationElement window = app.Element(settings);
            (await Ui.ItemsAsync(window, "SettingsPrinterList", 1))[0].Patterns.SelectionItem.Pattern.Select();
            await Ui.SelectAsync(window, "PrinterTabConnection");
            // A password box: its value is not read back through UI Automation; Credential Manager shows what was saved.
            (await Ui.FindAsync(window, "PrinterPassword")).Patterns.Value.Pattern.SetValue(password);
            Screenshots.Save(settings, "credentials-1-password-typed");
            await Ui.InvokeAsync(window, "SettingsSave");
            await Wait.UntilAsync(() => !Native.IsWindowVisible(settings), TimeSpan.FromSeconds(20), "the settings to save and close");

            await Wait.UntilAsync(() => TestCredentials.Persist(target) is not null, TimeSpan.FromSeconds(10), $"the entry '{target}' in Credential Manager");
            Assert.Equal(TestCredentials.PersistSession, TestCredentials.Persist(target));
            Assert.True(TestCredentials.Holds(target, password), $"'{target}' does not hold the password typed into the settings.");
            Assert.Equal([target], TestCredentials.Targets(prefix + "/"));
            await WaitForStateAsync(diagnostics, "StateIdle", "the login with the saved password");
            settingsJson = app.ReadSettings();

            await app.QuitAsync();
            AssertNoFileContains(app, password);
        }

        // 2. A restart with the same prefix and the saved settings: the password comes from Credential Manager.
        await using (AppInstance app = await AppInstance.StartAsync(fixture, "credentials-2-restart", settingsJson, switches: switches))
        {
            AutomationElement diagnostics = await OpenDiagnosticsAsync(app);
            await WaitForStateAsync(diagnostics, "StateIdle", "the login with the password from Credential Manager");

            // 3. The printer removed and saved: its entry goes.
            nint settings = await app.OpenAsync("settings", AppWindowKind.Settings);
            AutomationElement window = app.Element(settings);
            (await Ui.ItemsAsync(window, "SettingsPrinterList", 1))[0].Patterns.SelectionItem.Pattern.Select();
            await Ui.InvokeAsync(window, "SettingsRemovePrinter");
            await Ui.ItemsAsync(window, "SettingsPrinterList", 0);
            await Ui.InvokeAsync(window, "SettingsSave");
            await Wait.UntilAsync(() => !Native.IsWindowVisible(settings), TimeSpan.FromSeconds(20), "the settings to save and close");
            await Wait.UntilAsync(() => TestCredentials.Persist(target) is null, TimeSpan.FromSeconds(10), $"the entry '{target}' deleted");
            Assert.Empty(TestCredentials.Targets(prefix + "/"));

            await app.QuitAsync();
            AssertNoFileContains(app, password);
        }
    }

    private static async Task<AutomationElement> OpenDiagnosticsAsync(AppInstance app) =>
        app.Element(await app.OpenAsync($"diagnostics:{PrinterId}", AppWindowKind.Diagnostics));

    /// <summary>Waits until the diagnostics show the printer state <paramref name="stateKey"/> (resource key of its text).</summary>
    private static async Task WaitForStateAsync(AutomationElement diagnostics, string stateKey, string what)
    {
        string expected = AppStrings.Get("en", stateKey);
        string shown = string.Empty;
        try
        {
            await Wait.UntilAsync(
                () => (shown = Ui.Text(diagnostics, "DiagnosticsState")).EndsWith(expected, StringComparison.Ordinal),
                StateTimeout,
                what);
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException($"{ex.Message} The diagnostics show '{shown}', expected '{expected}'.", ex);
        }
    }

    /// <summary>No file of the test app (log, <c>settings.json</c>, anything else) holds the password, in UTF-8 or UTF-16.</summary>
    private static void AssertNoFileContains(AppInstance app, string secret)
    {
        string[] files = Directory.GetFiles(app.DataDirectory, "*", SearchOption.AllDirectories);
        Assert.Contains(files, static file => Path.GetFileName(file) == "settings.json");
        Assert.Contains(files, static file => Path.GetFileName(file) == Path.GetFileName(AppSwitches.LogFile));
        byte[][] patterns = [Encoding.UTF8.GetBytes(secret), Encoding.Unicode.GetBytes(secret)];
        foreach (string file in files)
        {
            byte[] content = File.ReadAllBytes(file);
            Assert.False(
                patterns.Any(pattern => content.AsSpan().IndexOf(pattern) >= 0),
                $"'{Path.GetRelativePath(app.DataDirectory, file)}' contains the password.");
        }
    }
}
