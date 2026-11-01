using System.Globalization;
using System.Text;

using LayerPeek.App.E2E.Tests.Infrastructure;

namespace LayerPeek.App.E2E.Tests;

/// <summary>
/// One-time recovery of the test app's notifications (T-133), only on request (<see cref="Variable"/> = 1, see
/// <see cref="Command"/> and docs/troubleshooting.md): Windows' notification service may keep the notifications of
/// <see cref="AppSwitches.AppUserModelId"/> switched off (<c>ToastNotifier.Setting</c> = <c>DisabledForApplication</c>)
/// although its key says on, and every toast test then fails at its pre-flight. The recovery starts the test app (its Start
/// menu shortcut, its activator, every change journaled like any toast test), switches the notifications on while the app
/// runs (<see cref="ToastNotificationTests.ToastRun.SwitchTestAppOnAsync"/>), and when Windows still keeps them off opens
/// Settings &gt; System &gt; Notifications (only when Settings does not run) and waits until the user switched
/// "LayerPeek E2E" on. Everything else is restored as after every toast test. Skipped in every normal run.
/// </summary>
public sealed class TestAppNotificationRecovery(E2EFixture fixture)
{
    /// <summary>Set to 1 to run the recovery.</summary>
    public const string Variable = "LAYERPEEK_E2E_RECOVER_NOTIFICATIONS";

    /// <summary>The command of the recovery (Windows PowerShell or pwsh, from the repository root).</summary>
    public const string Command = $"$env:{Variable} = '1'; pwsh ./tools/run-e2e.ps1 -Class '*{nameof(TestAppNotificationRecovery)}'; Remove-Item Env:{Variable}";

    private const string SettingsAddress = "ms-settings:notifications";

    /// <summary>How long the user has to switch the test app on in Settings.</summary>
    private static readonly TimeSpan UserTimeout = TimeSpan.FromMinutes(5);

    [Fact]
    [Trait(SystemChanges.Trait, SystemChangeTraits.Toasts)]
    [Trait(SystemChanges.Trait, SystemChangeTraits.DoNotDisturb)]
    public async Task SwitchTheNotificationsOfTheTestAppBackOn()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable(Variable) == "1", $"Only on request ({Variable}=1): {Command}");
        SystemChanges.Require();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        StringBuilder report = new();
        try
        {
            string before = await ToastSetting.ReadAsync(cancellationToken);
            report.AppendLine(CultureInfo.InvariantCulture, $"Effective notification setting of {AppSwitches.AppUserModelId} before: {before}");
            if (before == ToastSetting.Enabled)
            {
                TestContext.Current.SendDiagnosticMessage($"The notifications of {AppSwitches.AppUserModelId} are on already; nothing to recover.");
                return;
            }

            await using ToastNotificationTests.ToastRun run = await ToastNotificationTests.ToastRun.StartAsync(
                fixture, "notification-recovery", "en", report, options: new ToastRunOptions { WithoutPrinter = true, AllowNotificationsOff = true });
            string setting = await ToastSetting.ReadAsync(cancellationToken);
            if (ToastSetting.IsDisabled(setting))
            {
                setting = await run.SwitchTestAppOnAsync(cancellationToken);
            }

            if (ToastSetting.IsDisabled(setting))
            {
                setting = await LetTheUserSwitchItOnAsync(report, cancellationToken);
            }

            report.AppendLine(CultureInfo.InvariantCulture, $"Effective notification setting at the end: {setting}");
            Assert.True(
                setting == ToastSetting.Enabled,
                $"The notifications of {AppSwitches.AppUserModelId} are still off (ToastNotifier.Setting = {setting}). Switch \"LayerPeek E2E\" on in Settings > System > Notifications "
                + "while this recovery runs (docs/troubleshooting.md).");
            TestContext.Current.SendDiagnosticMessage($"The notifications of {AppSwitches.AppUserModelId} are on again.");
        }
        finally
        {
            Screenshots.SaveText(report.ToString(), "notification-recovery-report");
        }
    }

    /// <summary>Opens Settings &gt; Notifications (when it does not run) and waits until the effective setting is on.</summary>
    private static async Task<string> LetTheUserSwitchItOnAsync(StringBuilder report, CancellationToken cancellationToken)
    {
        string message = $"Windows keeps the notifications of the test app off. In Settings > System > Notifications switch \"LayerPeek E2E\" on; waiting up to {UserTimeout.TotalMinutes:0} minutes.";
        TestContext.Current.SendDiagnosticMessage(message);
        report.AppendLine(message);
        WindowsSettingsApp? opened = null;
        try
        {
            if (WindowsSettingsApp.IsRunning())
            {
                report.AppendLine("Settings runs already: not opened by the recovery (open System > Notifications in it).");
            }
            else
            {
                opened = await WindowsSettingsApp.OpenAsync(SettingsAddress, cancellationToken);
                report.AppendLine(CultureInfo.InvariantCulture, $"Opened {SettingsAddress} (process {opened.ForegroundProcess}).");
            }

            return await ToastSetting.WaitForAsync(ToastSetting.ReadAsync, ToastSetting.Enabled, UserTimeout, TimeSpan.FromSeconds(2), cancellationToken);
        }
        finally
        {
            // Settings opened by the recovery is closed again; one the user had open stays.
            opened?.Dispose();
        }
    }
}
