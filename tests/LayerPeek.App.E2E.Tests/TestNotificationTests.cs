using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;

using FlaUI.Core.AutomationElements;

using Microsoft.Win32;

using LayerPeek.App.E2E.Tests.Infrastructure;

using Wait = LayerPeek.App.E2E.Tests.Infrastructure.Wait;

namespace LayerPeek.App.E2E.Tests;

/// <summary>
/// The test notification of the settings window (T-101) on the real exe with real toasts of the test app (T-067, T-068),
/// replacing the manual check of T-125 (T-128):
/// <list type="number">
/// <item>"Send test notification" on the General tab shows the toast; its sound follows the Sound option shown in the window,
/// also when it is not saved: the saved settings say "All notifications", the window is switched to "Off" without saving,
/// and the toast's own XML in Windows' notification history is silent (the sound of a toast is nowhere on screen; this way
/// no sound plays during the run, user decision 2026-10-22).</item>
/// <item>A second click replaces the first toast: the history holds one test notification, the app handed two to Windows.</item>
/// <item>A click on the test notification in the notification center brings the settings window to the front on its
/// General tab (it was on the Printers tab).</item>
/// <item>With the notifications of the test app switched off in Windows (its key under Settings &gt; System &gt;
/// Notifications, journaled; when Windows does not take that, the notifications switch of Windows, also journaled) the
/// window shows "Notifications are turned off in Windows." with the button to Settings instead of the test button. The test
/// mode does not open Settings (it logs the address); the test opens that address itself, only when Settings does not run,
/// checks that Settings came to the foreground and closes it again.</item>
/// </list>
/// System changes (trait <see cref="SystemChanges.Trait"/>), all through the journal of <c>tools/run-e2e.ps1</c> and
/// restored also after a failure or a killed run: those of every toast test (<see cref="ToastNotificationTests"/>) and the
/// notifications of the test app (and possibly of Windows) switched off for a moment.
/// </summary>
public sealed class TestNotificationTests(E2EFixture fixture)
{
    /// <summary>The tag of the test notification (<c>TestNotification.Identifier</c> of the app).</summary>
    private const string TestNotificationTag = "layerpeek-test-notification";

    private const string SettingsAddress = "ms-settings:notifications";

    private static readonly TimeSpan ClickTimeout = TimeSpan.FromSeconds(15);

    /// <summary>How long the window may take to learn that Windows switched the notifications off.</summary>
    private static readonly TimeSpan TurnedOffTimeout = TimeSpan.FromSeconds(15);

    [Fact]
    [Trait(SystemChanges.Trait, SystemChangeTraits.Toasts)]
    [Trait(SystemChanges.Trait, SystemChangeTraits.DoNotDisturb)]
    public async Task TestNotificationFollowsTheWindowReplacesItselfOpensGeneralAndWarnsWhenWindowsTurnedItOff()
    {
        SystemChanges.Require();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        StringBuilder report = new();
        // Saved: every notification with a sound. The window switches to Off without saving.
        ToastRunOptions options = new()
        {
            WithoutPrinter = true,
            EditSettings = static settings => settings["notifications"]!["sound"] = "All",
        };
        try
        {
            await using ToastNotificationTests.ToastRun run = await ToastNotificationTests.ToastRun.StartAsync(fixture, "test-notification", "en", report, options: options);
            ToastTexts texts = new(AppStrings.Get("en", "NotificationTestTitle"), AppStrings.Get("en", "NotificationTestBody"));
            nint settings = await run.App.OpenAsync("settings", AppWindowKind.Settings);
            AutomationElement window = run.App.Element(settings);
            await Ui.SelectAsync(window, "SettingsTabGeneral");
            Assert.True(Ui.IsShown(window, "SettingsTestNotification"), "The General tab shows no test button.");
            Assert.False(Ui.IsShown(window, "SettingsNotificationsTurnedOff"), "The General tab warns before anything was switched off.");
            string off = await Ui.SelectComboItemAsync(window, "SettingsSound", static (text, _) => text == AppStrings.Get("en", "SettingsSoundOff"), "Off");
            report.AppendLine(CultureInfo.InvariantCulture, $"Sound in the window: '{off}' (not saved; saved: All)");

            // 1. The first test notification: silent, as the window says.
            ToastNotificationTests.MovePointerAway();
            await Ui.InvokeAsync(window, "SettingsTestNotification");
            ToastMatch first = await run.WaitForBannerAsync(texts);
            report.AppendLine("First banner:").AppendLine(first.Describe());
            using (Bitmap capture = Toasts.Capture(await Toasts.SettledBoundsAsync(first)))
            {
                Screenshots.Save(capture, "test-notification-banner");
            }

            Assert.True(first.HasExactly(texts.PrinterName), $"The title is not '{texts.PrinterName}':{Environment.NewLine}{first.Describe()}");
            Assert.True(first.HasLine(texts.Body), $"The text is not '{texts.Body}':{Environment.NewLine}{first.Describe()}");
            string sent = AppStrings.Get("en", "SettingsTestNotificationSentWindows");
            await Wait.UntilAsync(() => Ui.Text(window, "SettingsTestNotificationResult") == sent, TimeSpan.FromSeconds(10), "the result 'sent' next to the test button");
            await Wait.UntilAsync(() => Shown(run.App) == 1, TimeSpan.FromSeconds(10), "the app's log of the first test notification");
            Assert.Contains("The test notification was handed to Windows (sound: False).", run.App.ReadLog(), StringComparison.Ordinal);
            HistoryToast toast = await SingleTestNotificationAsync(report, "after the first click", cancellationToken);
            Assert.True(toast.IsSilent, $"The test notification plays a sound although the window says Off (saved: All):{Environment.NewLine}{toast.Xml}");
            Assert.Equal(new[] { texts.PrinterName, texts.Body }, toast.Texts);

            // 2. The second click replaces it.
            await Toasts.WaitForBannerGoneAsync(run.App.Automation, texts, Toasts.BannerLifetime());
            await Ui.InvokeAsync(window, "SettingsTestNotification");
            ToastMatch second = await run.WaitForBannerAsync(texts);
            report.AppendLine("Second banner:").AppendLine(second.Describe());
            await Wait.UntilAsync(() => Shown(run.App) == 2, TimeSpan.FromSeconds(10), "the app's log of the second test notification");
            HistoryToast replaced = await SingleTestNotificationAsync(report, "after the second click", cancellationToken);
            Assert.True(replaced.IsSilent, $"The second test notification is not silent:{Environment.NewLine}{replaced.Xml}");
            Assert.Equal("All", SavedSound(run.App));

            // 3. A click on it in the notification center: the settings window comes to the front on its General tab.
            await Toasts.WaitForBannerGoneAsync(run.App.Automation, texts, Toasts.BannerLifetime());
            await Ui.SelectAsync(window, "SettingsTabPrinters");
            string activation = await Toasts.InNotificationCenterAsync(run.App.Automation, async (_, center) =>
            {
                ToastMatch inCenter = await Toasts.FindInAsync(run.App.Automation, center, texts, TimeSpan.FromSeconds(10))
                    ?? throw new InvalidOperationException($"The test notification ('{texts.Body}') is not in the notification center.");
                report.AppendLine("Notification center elements:").AppendLine(inCenter.Describe());
                using (Bitmap capture = Toasts.Capture(await Toasts.SettledBoundsAsync(inCenter)))
                {
                    Screenshots.Save(capture, "test-notification-center");
                }

                return await Toasts.ActivateAsync(inCenter, () => IsSelected(window, "SettingsTabGeneral"), ClickTimeout, mouseOnly: true);
            });
            report.AppendLine("Activated by " + activation);
            await Wait.UntilAsync(() => Native.GetForegroundWindow() == settings, TimeSpan.FromSeconds(10), () => $"the settings window in the foreground (foreground: {WindowsSettingsApp.ProcessOf(Native.GetForegroundWindow())})");
            Assert.Single(run.App.Windows(), hwnd => run.App.KindOf(hwnd) == AppWindowKind.Settings);
            Screenshots.Save(settings, "test-notification-clicked-general");

            // 4. Switched off in Windows: the warning with the button to Settings.
            // T-133: switched on again while the app runs and its shortcut exists (also after a failure): Windows takes
            // an "on" written after the app quit or before it started no more (its notification service keeps the "off").
            string switchedOn;
            try
            {
                string how = await TurnOffAsync(run, window, report, cancellationToken);
                report.AppendLine("Switched off by " + how);
                Screenshots.Save(settings, "test-notification-turned-off");
                Assert.Equal(AppStrings.Get("en", "SettingsNotificationsTurnedOffWindows"), Ui.Text(window, "SettingsNotificationsTurnedOff"));
                Assert.False(Ui.IsShown(window, "SettingsTestNotification"), "The test button is still shown although Windows turned the notifications off.");
                Assert.True(Ui.IsShown(window, "SettingsOpenNotificationSettings"), "The button to Settings is not shown.");
                AutomationElement open = await Ui.FindAsync(window, "SettingsOpenNotificationSettings");
                Assert.Equal(AppStrings.Get("en", "SettingsOpenNotificationSettingsWindows"), ButtonText(open));
                open.Patterns.Invoke.Pattern.Invoke();
                string logged = $"Test mode: System Settings is not opened ({SettingsAddress}).";
                await Wait.UntilAsync(() => run.App.ReadLog().Contains(logged, StringComparison.Ordinal), TimeSpan.FromSeconds(10), $"the app to log '{logged}'");

                // What the button opens outside the test mode, opened by the test (only when Settings does not run).
                if (WindowsSettingsApp.IsRunning())
                {
                    report.AppendLine(CultureInfo.InvariantCulture, $"Settings was already open: {SettingsAddress} not opened by the test (it would replace the user's page).");
                    TestContext.Current.SendDiagnosticMessage($"Settings was already open: {SettingsAddress} was not opened by the test.");
                }
                else
                {
                    using (WindowsSettingsApp app = await WindowsSettingsApp.OpenAsync(SettingsAddress, cancellationToken))
                    {
                        report.AppendLine(CultureInfo.InvariantCulture, $"{SettingsAddress} opened Settings in the foreground (process {app.ForegroundProcess}); closed again.");
                    }

                    await Wait.UntilAsync(() => !WindowsSettingsApp.IsRunning(), TimeSpan.FromSeconds(10), "Settings to close");
                }
            }
            finally
            {
                switchedOn = await run.SwitchTestAppOnAsync(cancellationToken);
                report.AppendLine("Switched on again while the app runs: " + switchedOn);
            }

            Assert.True(
                switchedOn == ToastSetting.Enabled,
                $"Windows keeps the notifications of the test app {AppSwitches.AppUserModelId} switched off after the test switched them on again (ToastNotifier.Setting = {switchedOn}). "
                + $"Recovery (docs/troubleshooting.md): {TestAppNotificationRecovery.Command}");

            // The window closes without saving (the sound stays All), so the app quits without a question.
            await CameraWindowSettings.CloseAsync(settings, window, save: false);
            Assert.Equal("All", SavedSound(run.App));
            await run.AssertDoNotDisturbStayedOffAsync(report);
            run.AssertProductionUnchanged();
        }
        finally
        {
            Screenshots.SaveText(report.ToString(), "test-notification-report");
        }
    }

    /// <summary>
    /// Switches the notifications of the test app off in Windows (its notification settings key, <c>Enabled</c> = 0) and
    /// clicks the test button until the window warns. When Windows does not take the key within <see cref="TurnedOffTimeout"/>,
    /// the notifications switch of Windows goes off too (both journaled, both restored by the run).
    /// </summary>
    /// <returns>What switched them off (for the results).</returns>
    private static async Task<string> TurnOffAsync(ToastNotificationTests.ToastRun run, AutomationElement window, StringBuilder report, CancellationToken cancellationToken)
    {
        RegistryKeySnapshot before = RegistryKeys.Read(ToastAppSettingsKind.SubKey);
        report.AppendLine(CultureInfo.InvariantCulture, $"Notification settings of the test app before switching off: {before}");
        await run.AppSettings.ApplyAsync(WithNotificationsOff(before), cancellationToken);
        if (await ClickUntilWarnedAsync(window))
        {
            return $"the test app's key (Enabled=0): {RegistryKeys.Read(ToastAppSettingsKind.SubKey)}";
        }

        report.AppendLine(CultureInfo.InvariantCulture, $"The test app's key (Enabled=0) gave no warning within {TurnedOffTimeout.TotalSeconds:0} s; the notifications switch of Windows goes off too.");
        await run.ToastsEnabled.ApplyAsync(new ToastsEnabledState(0), cancellationToken);
        if (await ClickUntilWarnedAsync(window))
        {
            return "the notifications switch of Windows (ToastEnabled=0), after the test app's key gave no warning";
        }

        IEnumerable<string> lines = run.App.ReadLog().Split('\n').Where(static line => line.Contains("notification", StringComparison.OrdinalIgnoreCase)).Select(static line => "  " + line.TrimEnd());
        throw new TimeoutException($"No warning '{AppStrings.Get("en", "SettingsNotificationsTurnedOffWindows")}' after switching off the test app's notifications and those of Windows. "
            + $"What the app logged about notifications:{Environment.NewLine}{string.Join(Environment.NewLine, lines)}");
    }

    /// <summary>Clicks the test button (while shown) and waits for the warning; <see langword="false"/> when none came.</summary>
    private static async Task<bool> ClickUntilWarnedAsync(AutomationElement window)
    {
        DateTime deadline = DateTime.UtcNow + TurnedOffTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (Ui.IsShown(window, "SettingsNotificationsTurnedOff"))
            {
                return true;
            }

            if (Ui.TryFind(window, "SettingsTestNotification") is { } test && test.Properties.IsEnabled.ValueOrDefault && !test.Properties.IsOffscreen.ValueOrDefault)
            {
                test.Patterns.Invoke.Pattern.Invoke();
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        return Ui.IsShown(window, "SettingsNotificationsTurnedOff");
    }

    /// <summary>The key of the test app with <c>Enabled</c> = 0 (Windows' own switch of Settings &gt; System &gt; Notifications for the app).</summary>
    internal static RegistryKeySnapshot WithNotificationsOff(RegistryKeySnapshot current) =>
        new(
            true,
            [.. current.Values.Where(static value => !string.Equals(value.Name, "Enabled", StringComparison.OrdinalIgnoreCase)), new RegistryValueState("Enabled", nameof(RegistryValueKind.DWord), "0")],
            current.SubKeys);

    /// <summary>The one test notification of the test app in Windows' history (a new one replaces the previous one).</summary>
    private static async Task<HistoryToast> SingleTestNotificationAsync(StringBuilder report, string when, CancellationToken cancellationToken)
    {
        IReadOnlyList<HistoryToast> history = [];
        // Windows may add a shown toast to the history a moment later.
        await Wait.UntilAsync(
            async () => (history = await ToastHistory.ReadAsync(cancellationToken)).Any(static toast => toast.Tag == TestNotificationTag),
            TimeSpan.FromSeconds(15),
            "the test notification in the toast history");
        List<HistoryToast> tests = [.. history.Where(static toast => toast.Tag == TestNotificationTag)];
        report.AppendLine(CultureInfo.InvariantCulture, $"Toast history of {AppSwitches.AppUserModelId} {when}: {history.Count} toast(s), {tests.Count} with the tag {TestNotificationTag}");
        foreach (HistoryToast toast in tests)
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"  launch='{toast.Launch}' silent={toast.IsSilent} xml={toast.Xml}");
        }

        Assert.True(tests.Count == 1, $"{tests.Count} test notifications in the history {when} (a new one must replace the previous one).");
        return tests[0];
    }

    /// <summary>How often the app logged a test notification handed to Windows.</summary>
    private static int Shown(AppInstance app) => app.ReadLog().Split('\n').Count(static line => line.Contains("The test notification was handed to Windows", StringComparison.Ordinal));

    private static string? SavedSound(AppInstance app) => JsonNode.Parse(app.ReadSettings())?["notifications"]?["sound"]?.GetValue<string>();

    private static bool IsSelected(AutomationElement window, string automationId)
    {
        try
        {
            return Ui.TryFind(window, automationId) is { } tab && tab.Patterns.SelectionItem.Pattern.IsSelected.ValueOrDefault;
        }
        catch (Exception ex) when (ex is COMException or FlaUI.Core.Exceptions.ElementNotAvailableException)
        {
            return false;
        }
    }

    /// <summary>The text of a button: its name, or the text of its content.</summary>
    private static string ButtonText(AutomationElement button) =>
        button.Properties.Name.ValueOrDefault is { Length: > 0 } name
            ? name
            : button.FindFirstDescendant(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Text))?.Properties.Name.ValueOrDefault ?? string.Empty;
}
