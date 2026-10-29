using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.UIA3;

using Wait = LayerPeek.App.E2E.Tests.Infrastructure.Wait;

using LayerPeek.App.E2E.Tests.Infrastructure;
using LayerPeek.Simulator.Printers;

namespace LayerPeek.App.E2E.Tests;

/// <summary>
/// Real toasts of the test app (T-068; T-010 click, T-025 icon) on the real exe in the test mode with toasts
/// (T-067, app id <c>LayerPeek.E2E</c>, shortcut "LayerPeek E2E"). A simulated printer (T-045) prints;
/// switching its scenario at run time makes the app show a toast titled with the printer's name, unique per test, so that
/// only this toast is ever found, captured (its own screen region) or clicked. A click must open the printer's web through
/// the local proxy (T-028): the test mode records the address instead of starting a browser, and the test opens it.
/// <para>
/// System changes (T-065, trait <see cref="SystemChanges.Trait"/>), each through the journal and restored also after a
/// failure or a killed run: Do Not Disturb off, the notifications of Windows and of the test app on (only when they are
/// off), the test app's notification settings key Windows creates (deleted again when it did not exist), and its toasts
/// and Start menu shortcut (cleared by the app when it quits, and by <c>--layerpeek-e2e-clear-toasts</c> in the restore).
/// </para>
/// </summary>
public sealed class ToastNotificationTests(E2EFixture fixture)
{
    private const string PrinterId = "toast";

    /// <summary>Banner after the event: one poll (5 s) plus Windows' delivery.</summary>
    private static readonly TimeSpan BannerTimeout = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan OpenTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    [Trait(SystemChanges.Trait, SystemChangeTraits.Toasts)]
    [Trait(SystemChanges.Trait, SystemChangeTraits.DoNotDisturb)]
    public async Task ToastBannerClickOpensThePrinterWebThroughTheProxy()
    {
        SystemChanges.Require();
        StringBuilder report = new();
        await using ToastRun run = await ToastRun.StartAsync(fixture, "toast-banner", "en", report);
        ToastTexts texts = run.Texts(AppStrings.Get("en", "NotificationPrintPaused"));

        await run.SetScenarioAsync("paused");
        ToastMatch banner = await run.WaitForBannerAsync(texts);
        // The pointer on the banner keeps it open (Windows does not dismiss a hovered toast) while it is checked.
        Hover(banner.Anchor);
        report.AppendLine("Banner elements:").AppendLine(banner.Describe());

        Assert.True(banner.HasExactly(texts.PrinterName), $"The toast title is not the printer name:{Environment.NewLine}{banner.Describe()}");
        Assert.True(banner.HasLine(texts.Body), $"The toast text is not '{texts.Body}':{Environment.NewLine}{banner.Describe()}");
        Rectangle bounds = await Toasts.SettledBoundsAsync(banner);
        using (Bitmap capture = Toasts.Capture(bounds))
        {
            Screenshots.Save(capture, "toast-banner");
        }

        // T-025: the app icon in the header of the toast.
        double scale = Toasts.ScaleOf(banner.Window);
        (Rectangle area, string how) = Toasts.IconSearchArea(banner, bounds, scale);
        ToastIconMatch? icon;
        using (Bitmap header = Toasts.Capture(area))
        {
            Screenshots.Save(header, "toast-banner-icon-area");
            icon = ToastIcon.Find(header, ToastIcon.CandidateSizes(scale));
            if (icon is not null)
            {
                using Bitmap found = header.Clone(icon.Location, header.PixelFormat);
                Screenshots.Save(found, "toast-banner-icon");
                using Bitmap reference = ToastIcon.Reference(icon.Size, icon.EntrySize);
                Screenshots.Save(reference, "toast-banner-icon-reference");
            }
        }

        report.AppendLine(CultureInfo.InvariantCulture, $"Scale {scale:0.##}; icon searched in {area} ({how}); best match: {icon?.ToString() ?? "(area too small)"}");
        Assert.True(icon is { IsMatch: true }, $"The toast does not show the app icon: {icon?.ToString() ?? "area too small"} (searched {how}).");

        string activation = await Toasts.ActivateAsync(banner, () => run.OpenedUrls().Count > 0, OpenTimeout);
        report.AppendLine("Activated by " + activation);
        await run.AssertOpensThePrinterWebAsync(await run.SingleOpenedUrlAsync(), report);
        await run.AssertDoNotDisturbStayedOffAsync(report);
        run.AssertProductionUnchanged();
        Screenshots.SaveText(report.ToString(), "toast-banner-report");
    }

    [Fact]
    [Trait(SystemChanges.Trait, SystemChangeTraits.Toasts)]
    [Trait(SystemChanges.Trait, SystemChangeTraits.DoNotDisturb)]
    public async Task ToastInTheNotificationCenterOpensThePrinterWeb()
    {
        SystemChanges.Require();
        StringBuilder report = new();
        await using ToastRun run = await ToastRun.StartAsync(fixture, "toast-center", "en", report);
        ToastTexts texts = run.Texts(AppStrings.Get("en", "NotificationError"));

        // The pointer away from where the banner appears: a hovered banner never expires.
        MovePointerAway();
        await run.SetScenarioAsync("error");
        await run.WaitForBannerAsync(texts);
        await Toasts.WaitForBannerGoneAsync(run.App.Automation, texts, Toasts.BannerLifetime());

        string activation = await Toasts.InNotificationCenterAsync(run.App.Automation, async (_, window) =>
        {
            ToastMatch toast = await Toasts.FindInAsync(run.App.Automation, window, texts, TimeSpan.FromSeconds(10))
                ?? throw new InvalidOperationException($"The toast of '{texts.PrinterName}' ('{texts.Body}') is not in the notification center.");
            report.AppendLine("Notification center elements:").AppendLine(toast.Describe());
            Rectangle bounds = await Toasts.SettledBoundsAsync(toast);
            using (Bitmap capture = Toasts.Capture(bounds))
            {
                Screenshots.Save(capture, "toast-center");
            }

            return await Toasts.ActivateAsync(toast, () => run.OpenedUrls().Count > 0, OpenTimeout, mouseOnly: true);
        });

        // Windows reports a click in the notification center only to the COM toast activator (T-068).
        report.AppendLine("Activated by " + activation);
        report.AppendLine("App log about the click:").AppendLine(run.LoggedActivations());
        Assert.Contains("reached the app through its toast activator", run.App.ReadLog(), StringComparison.Ordinal);
        await run.AssertOpensThePrinterWebAsync(await run.SingleOpenedUrlAsync(), report);
        await run.AssertDoNotDisturbStayedOffAsync(report);
        run.AssertProductionUnchanged();
        Screenshots.SaveText(report.ToString(), "toast-center-report");
    }

    /// <summary>
    /// The "printer cooled down" toast (T-080, T-093) with the simulator's cooling curve and the printer's threshold of
    /// 100 °C: off by default, so the first finished print cools down without it; switched on in the settings window (General,
    /// notifications), the next finished print brings it, titled with the printer's name and saying the print finished and
    /// the printer cooled down. Its sound (information: none with "important only") is covered by the unit tests of the
    /// toast XML; the test toasts are silent anyway (T-068).
    /// </summary>
    [Fact]
    [Trait(SystemChanges.Trait, SystemChangeTraits.Toasts)]
    [Trait(SystemChanges.Trait, SystemChangeTraits.DoNotDisturb)]
    public async Task CooledDownToastComesOnlyWithItsSwitchOn()
    {
        SystemChanges.Require();
        StringBuilder report = new();
        SimulationTiming cycle = new()
        {
            HeatUp = TimeSpan.FromSeconds(5),
            Print = TimeSpan.FromSeconds(20),
            FinishedHold = TimeSpan.FromSeconds(110),
            IdleHold = TimeSpan.FromSeconds(15),
        };
        await using ToastRun run = await ToastRun.StartAsync(fixture, "toast-cooled", "en", report, cycle, cooldownThresholdCelsius: 100);
        // The simulator reports the bed: the detail with both temperatures (T-109).
        string detail = AppStrings.Get("en", "NotificationCooledDownDetailBed");
        ToastTexts texts = new(run.PrinterName, AppStrings.Get("en", "NotificationCooledDownFinished"), [detail[..detail.IndexOf('{', StringComparison.Ordinal)], ".bgcode"]);
        try
        {
            // Off by default: the first print cools down below 100 °C without a toast.
            await WaitForCooledDownAsync(run.Printer, report, "first print");
            await Task.Delay(TimeSpan.FromSeconds(12), TestContext.Current.CancellationToken);
            Assert.Null(Toasts.FindBanner(run.App.Automation, texts));
            bool inCenter = await Toasts.InNotificationCenterAsync(run.App.Automation, async (_, window) => await Toasts.FindInAsync(run.App.Automation, window, texts, TimeSpan.FromSeconds(3)) is not null);
            report.AppendLine(CultureInfo.InvariantCulture, $"Switch off: cooled-down toast in the notification center: {inCenter}");
            Assert.False(inCenter, "A cooled-down toast came with the switch off (the default).");
            Assert.DoesNotContain("\"cooledDown\"", run.App.ReadSettings(), StringComparison.Ordinal);

            // Switched on in the settings window.
            nint settings = await run.App.OpenAsync("settings", AppWindowKind.Settings);
            AutomationElement settingsElement = run.App.Element(settings);
            await Ui.SelectAsync(settingsElement, "SettingsTabGeneral");
            await Ui.SetCheckedAsync(settingsElement, "SettingsNotifyCooledDown", true);
            await CameraWindowSettings.CloseAsync(settings, settingsElement, save: true);
            await Wait.UntilAsync(() => run.App.ReadSettings().Contains("\"cooledDown\": true", StringComparison.Ordinal), TimeSpan.FromSeconds(10), "the switch in settings.json");

            // The next print: the toast.
            await WaitForPrintAsync(run.Printer, report);
            MovePointerAway();
            await WaitForCooledDownAsync(run.Printer, report, "second print");
            ToastMatch banner = await run.WaitForBannerAsync(texts);
            Hover(banner.Anchor);
            report.AppendLine("Banner elements:").AppendLine(banner.Describe());
            Assert.True(banner.HasExactly(texts.PrinterName), $"The toast title is not the printer name:{Environment.NewLine}{banner.Describe()}");
            Assert.True(banner.HasLine(texts.Body), $"The toast text is not '{texts.Body}':{Environment.NewLine}{banner.Describe()}");
            Rectangle bounds = await Toasts.SettledBoundsAsync(banner);
            using (Bitmap capture = Toasts.Capture(bounds))
            {
                Screenshots.Save(capture, "toast-cooled-down");
            }

            await run.AssertDoNotDisturbStayedOffAsync(report);
            run.AssertProductionUnchanged();
        }
        finally
        {
            Screenshots.SaveText(report.ToString(), "toast-cooled-report");
        }
    }

    /// <summary>Waits (simulator side) until a print is running.</summary>
    private static async Task WaitForPrintAsync(SimulatedPrinter printer, StringBuilder report)
    {
        await Wait.UntilAsync(() => printer.Simulation.GetStatus().State == "PRINTING", TimeSpan.FromMinutes(3), "the next print of the simulator");
        report.AppendLine(CultureInfo.InvariantCulture, $"{DateTime.Now:HH:mm:ss} printing again");
    }

    /// <summary>Waits (simulator side) until the finished print's nozzle is below the threshold of 100 °C.</summary>
    private static async Task WaitForCooledDownAsync(SimulatedPrinter printer, StringBuilder report, string what)
    {
        await Wait.UntilAsync(() => printer.Simulation.GetStatus() is { State: "FINISHED", TempNozzle: < 98 }, TimeSpan.FromMinutes(4), $"the {what} to finish and cool down below 100 °C");
        report.AppendLine(CultureInfo.InvariantCulture, $"{DateTime.Now:HH:mm:ss} {what}: finished and below 100 °C ({printer.Simulation.GetStatus().TempNozzle:0.0} °C)");
    }

    /// <summary>
    /// T-068: the app ended (killed, so its toast stays in the notification center and nothing is cleaned up); a click on
    /// the toast makes Windows start the exe through the COM toast activator of the test app, in the same test mode (its
    /// command line carries the test switches), and the started app opens the printer's web through its proxy. The
    /// started app is then quit like the first one (it clears the toasts, the shortcut and the activator).
    /// </summary>
    [Fact]
    [Trait(SystemChanges.Trait, SystemChangeTraits.Toasts)]
    [Trait(SystemChanges.Trait, SystemChangeTraits.DoNotDisturb)]
    public async Task ToastInTheNotificationCenterStartsTheAppWhenItIsNotRunning()
    {
        SystemChanges.Require();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        StringBuilder report = new();
        await using ToastRun run = await ToastRun.StartAsync(fixture, "toast-center-started", "en", report);
        ToastTexts texts = run.Texts(AppStrings.Get("en", "NotificationError"));

        MovePointerAway();
        await run.SetScenarioAsync("error");
        await run.WaitForBannerAsync(texts);
        await Toasts.WaitForBannerGoneAsync(run.App.Automation, texts, Toasts.BannerLifetime());

        int first = run.App.Process.Id;
        run.App.Process.Kill(entireProcessTree: true);
        await run.App.Process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        Assert.True(TestToastsKind.ActivatorRegistered(), "The killed app left no toast activator to start it.");
        Assert.Empty(run.OpenedUrls());
        report.AppendLine(CultureInfo.InvariantCulture, $"The app ({first}) was killed; its toast stays in the notification center.");

        string activation = await Toasts.InNotificationCenterAsync(run.App.Automation, async (_, window) =>
        {
            ToastMatch toast = await Toasts.FindInAsync(run.App.Automation, window, texts, TimeSpan.FromSeconds(10))
                ?? throw new InvalidOperationException($"The toast of '{texts.PrinterName}' ('{texts.Body}') is not in the notification center after the app ended.");
            report.AppendLine("Notification center elements:").AppendLine(toast.Describe());
            Rectangle bounds = await Toasts.SettledBoundsAsync(toast);
            using (Bitmap capture = Toasts.Capture(bounds))
            {
                Screenshots.Save(capture, "toast-center-not-running");
            }

            // The start takes longer than a click on a running app: the app starts, loads its settings and its proxy.
            return await Toasts.ActivateAsync(toast, () => run.OpenedUrls().Count > 0, StartedAppOpenTimeout, mouseOnly: true);
        });

        report.AppendLine("Activated by " + activation);
        string commandLine = await run.AdoptRestartAsync(first);
        report.AppendLine(CultureInfo.InvariantCulture, $"Started by Windows with the activator's arguments: {string.Join(' ', ActivatorArguments(commandLine))}");
        Assert.Contains("-ToastActivated", commandLine, StringComparison.Ordinal);
        Assert.Contains(AppSwitches.TestEnvironmentArgument + AppSwitches.DataDirectoryVariable + "=", commandLine, StringComparison.Ordinal);
        report.AppendLine("App log about the click:").AppendLine(run.LoggedActivations());
        Assert.Contains("Started by Windows for a click on a notification (click received: True)", run.App.ReadLog(), StringComparison.Ordinal);
        await run.AssertOpensThePrinterWebAsync(await run.SingleOpenedUrlAsync(), report);
        await run.AssertDoNotDisturbStayedOffAsync(report);
        run.AssertProductionUnchanged();
        Screenshots.SaveText(report.ToString(), "toast-center-not-running-report");
    }

    /// <summary>A click opens the web of a started app within this time.</summary>
    private static readonly TimeSpan StartedAppOpenTimeout = TimeSpan.FromSeconds(60);

    /// <summary>The switches of a command line (no paths of the exe; the test folder only as the switches show it).</summary>
    private static IEnumerable<string> ActivatorArguments(string commandLine) =>
        commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(static part => part.StartsWith('-') || part.StartsWith("\"-", StringComparison.Ordinal))
            .Select(static part => part.Contains('=', StringComparison.Ordinal) ? part[..(part.IndexOf('=', StringComparison.Ordinal) + 1)] + "…" : part);

    [Fact]
    [Trait(SystemChanges.Trait, SystemChangeTraits.Toasts)]
    [Trait(SystemChanges.Trait, SystemChangeTraits.DoNotDisturb)]
    public async Task ToastFollowsTheLanguageSavedAtRuntime()
    {
        SystemChanges.Require();
        StringBuilder report = new();
        await using ToastRun run = await ToastRun.StartAsync(fixture, "toast-language", "en", report);

        // English first.
        ToastTexts english = run.Texts(AppStrings.Get("en", "NotificationPrintPaused"));
        await run.SetScenarioAsync("paused");
        ToastMatch first = await run.WaitForBannerAsync(english);
        report.AppendLine("English banner:").AppendLine(first.Describe());
        using (Bitmap capture = Toasts.Capture(await Toasts.SettledBoundsAsync(first)))
        {
            Screenshots.Save(capture, "toast-language-en");
        }

        // Deutsch in the settings, Save: no restart.
        nint settings = await run.App.OpenAsync("settings", AppWindowKind.Settings);
        AutomationElement window = run.App.Element(settings);
        await Ui.SelectAsync(window, "SettingsTabGeneral");
        await Ui.SelectComboItemAsync(window, "SettingsLanguage", static (text, _) => text == "Deutsch", "Deutsch");
        await Ui.InvokeAsync(window, "SettingsSave");
        await Wait.UntilAsync(() => SavedLanguage(run.App) == "de", TimeSpan.FromSeconds(10), "language de in settings.json");

        ToastTexts german = run.Texts(AppStrings.Get("de", "NotificationError"));
        await run.SetScenarioAsync("error");
        ToastMatch second = await run.WaitForBannerAsync(german);
        report.AppendLine("German banner:").AppendLine(second.Describe());
        using (Bitmap capture = Toasts.Capture(await Toasts.SettledBoundsAsync(second)))
        {
            Screenshots.Save(capture, "toast-language-de");
        }

        Assert.True(second.HasLine(german.Body), $"The toast after the language change is not German ('{german.Body}'):{Environment.NewLine}{second.Describe()}");
        Assert.False(second.Mentions(AppStrings.Get("en", "NotificationError")), "The toast after the language change is still English.");
        await run.AssertDoNotDisturbStayedOffAsync(report);
        run.AssertProductionUnchanged();
        Screenshots.SaveText(report.ToString(), "toast-language-report");
    }

    /// <summary>
    /// A test run with a real toast that is killed (T-065 layer 2): a child test process journals the toast changes, starts
    /// the test app, shows a toast and is killed together with the app. The start cleanup restores its entries: the exe
    /// clears the toasts and the shortcut (<c>--layerpeek-e2e-clear-toasts</c>, exit code 0), and the test app's notification
    /// settings key is as before; the toast is no longer in the notification center.
    /// </summary>
    [Fact]
    [Trait(SystemChanges.Trait, SystemChangeTraits.Toasts)]
    [Trait(SystemChanges.Trait, SystemChangeTraits.DoNotDisturb)]
    public async Task ToastsOfAKilledRunAreClearedByTheStartCleanup()
    {
        SystemChanges.Require();
        Assert.SkipWhen(ToastsEnabledKind.PolicyBlocksToasts() is not null, ToastsEnabledKind.PolicyBlocksToasts() ?? string.Empty);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        StringBuilder report = new();
        RegistryKeySnapshot settingsBefore = RegistryKeys.Read(ToastAppSettingsKind.SubKey);
        report.AppendLine(CultureInfo.InvariantCulture, $"Notification settings of the test app before: {settingsBefore}");

        // The parent keeps Windows' notifications on for the child; the child journals what its toast leaves.
        await using SystemChange<ToastsEnabledState> enabled = await SystemChanges.ToastsEnabledAsync(cancellationToken);
        await TurnOnAsync(enabled);
        await using SystemChange<DoNotDisturbState> doNotDisturb = await SystemChanges.DoNotDisturbAsync(cancellationToken);
        await TurnOffAsync(doNotDisturb);

        string printerName = "LP E2E crash " + Guid.NewGuid().ToString("N")[..8];
        string ready = Path.Combine(E2EFixture.Root, $"toast-child-ready-{Guid.NewGuid():N}.txt");
        string toastFile = SystemChanges.Journal.FileOf(SystemChanges.TestToasts.Name, Path.GetFullPath(E2EFixture.AppPath));
        string settingsFile = SystemChanges.Journal.FileOf(SystemChanges.ToastAppSettings.Name, null);
        ChildTestProcess? child = null;
        try
        {
            MovePointerAway();
            child = ChildTestProcess.Start(
                $"{typeof(ToastCrashChild).FullName}.{nameof(ToastCrashChild.ShowAToastAndWaitToBeKilled)}",
                new Dictionary<string, string?>
                {
                    [ToastCrashChild.PrinterNameVariable] = printerName,
                    [SystemChangeChild.ReadyVariable] = ready,
                    [SystemChangeChild.ParentVariable] = Environment.ProcessId.ToString(CultureInfo.InvariantCulture),
                    [SystemChangeWatchdog.Variable] = "0",
                });
            await Wait.UntilAsync(() => File.Exists(ready) || child.Process.HasExited, TimeSpan.FromMinutes(2), "the child to show its toast");
            Assert.False(child.Process.HasExited, $"The child ended before it was killed:{Environment.NewLine}{child.Output}");
            Assert.Contains(toastFile, SystemChanges.Journal.PendingFiles());
            Assert.Contains(settingsFile, SystemChanges.Journal.PendingFiles());
            Assert.True(File.Exists(TestToastsKind.ShortcutPath), "The child's test app did not create its shortcut.");
            Assert.True(TestToastsKind.ActivatorRegistered(), "The child's test app did not register its toast activator.");

            // The whole tree: the app the child started holds its output pipe, so the child alone would never report its end.
            child.Process.Kill(entireProcessTree: true);
            await child.WaitForExitAsync(TimeSpan.FromSeconds(30));
            List<string> killed = await AppProcesses.KillMarkedAsync(E2EFixture.Root);
            report.AppendLine(CultureInfo.InvariantCulture, $"Killed: the child test process with its app (process tree); left over and killed now: {(killed.Count == 0 ? "none" : string.Join(", ", killed))}");
            Screenshots.SaveText(child.Output, "toast-killed-child");
            Assert.True(File.Exists(TestToastsKind.ShortcutPath), "The killed app removed its shortcut; nothing left to restore.");

            SystemChangeRestoreReport restored = await SystemChanges.Journal.RestorePendingAsync(includeOwn: false, TimeSpan.FromSeconds(10), cancellationToken);
            report.AppendLine("Start cleanup:").AppendLine(restored.ToString());
            Assert.True(restored.Success, restored.ToString());
            Assert.Equal(2, restored.Restored.Count);
            Assert.DoesNotContain(toastFile, SystemChanges.Journal.PendingFiles());
            Assert.DoesNotContain(settingsFile, SystemChanges.Journal.PendingFiles());
            Assert.False(File.Exists(TestToastsKind.ShortcutPath), "The cleanup left the shortcut of the test app.");
            Assert.False(TestToastsKind.ActivatorRegistered(), "The cleanup left the toast activator of the test app.");
            RegistryKeySnapshot settingsAfter = RegistryKeys.Read(ToastAppSettingsKind.SubKey);
            report.AppendLine(CultureInfo.InvariantCulture, $"Notification settings of the test app after: {settingsAfter}");
            Assert.True(settingsBefore.SameAs(settingsAfter), $"The notification settings of the test app: {settingsBefore} before, {settingsAfter} after.");

            // The history is empty: the toast is gone from the notification center too.
            bool stillThere = await Toasts.InNotificationCenterAsync(fixture.Automation, async (_, window) =>
            {
                await Task.Delay(1500, cancellationToken);
                return Toasts.MentionsPrinter(fixture.Automation, window, printerName);
            });
            Assert.False(stillThere, "The toast of the killed run is still in the notification center after the cleanup.");
        }
        finally
        {
            child?.Dispose();
            await AppProcesses.KillMarkedAsync(E2EFixture.Root);
            // Nothing stays behind even when a check above failed.
            await SystemChanges.Journal.RestorePendingAsync(includeOwn: false, TimeSpan.FromSeconds(10), CancellationToken.None);
            File.Delete(ready);
            Screenshots.SaveText(report.ToString(), "toast-killed-report");
        }
    }

    /// <summary>The language of the saved settings.</summary>
    private static string? SavedLanguage(AppInstance app)
    {
        try
        {
            string json = File.ReadAllText(Path.Combine(app.DataDirectory, "settings.json"));
            return JsonNode.Parse(json)?["language"]?.GetValue<string>();
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static void Hover(AutomationElement element)
    {
        Rectangle bounds = element.BoundingRectangle;
        Mouse.Position = new Point(bounds.Left + (bounds.Width / 2), bounds.Top + (bounds.Height / 2));
    }

    /// <summary>The pointer to the middle of the primary monitor, away from the banners in the corner (it only moves, never clicks).</summary>
    internal static void MovePointerAway()
    {
        Rectangle screen = System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 800, 600);
        Mouse.Position = new Point(screen.Left + (screen.Width / 2), screen.Top + (screen.Height / 2));
    }

    internal static async Task TurnOnAsync(SystemChange<ToastsEnabledState> enabled)
    {
        if (!enabled.Original.IsOn)
        {
            await enabled.ApplyAsync(new ToastsEnabledState(1));
        }
    }

    internal static async Task TurnOffAsync(SystemChange<DoNotDisturbState> doNotDisturb)
    {
        if (doNotDisturb.Original.Enabled)
        {
            await doNotDisturb.ApplyAsync(DoNotDisturbState.Off);
        }
    }

    /// <summary>
    /// One test app with real toasts and a printing simulator, with every change a toast needs, all journaled; disposing
    /// quits the app (it clears its toasts and shortcut), then restores every change in reverse order.
    /// </summary>
    private sealed class ToastRun : IAsyncDisposable
    {
        private static readonly HttpClient Http = new(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };

        /// <summary>What to undo, last first.</summary>
        private readonly List<IAsyncDisposable> _undo = [];
        private readonly StringBuilder _report;
        private byte[]? _productionShortcut;
        private SystemChange<DoNotDisturbState>? _doNotDisturb;

        private ToastRun(StringBuilder report) => _report = report;

        public AppInstance App { get; private set; } = null!;

        public SimulatedPrinter Printer { get; private set; } = null!;

        /// <summary>Unique per test: only this test's toast is ever found.</summary>
        public string PrinterName { get; } = "LP E2E " + Guid.NewGuid().ToString("N")[..8];

        public static async Task<ToastRun> StartAsync(E2EFixture fixture, string name, string language, StringBuilder report, SimulationTiming? timing = null, int? cooldownThresholdCelsius = null)
        {
            Assert.SkipWhen(ToastsEnabledKind.PolicyBlocksToasts() is not null, ToastsEnabledKind.PolicyBlocksToasts() ?? string.Empty);
            ToastRun run = new(report);
            try
            {
                await run.StartCoreAsync(fixture, name, language, timing, cooldownThresholdCelsius);
                return run;
            }
            catch
            {
                await run.DisposeAsync();
                throw;
            }
        }

        public ToastTexts Texts(string body) => new(PrinterName, body, [".bgcode"]);

        /// <summary>
        /// The banner of the toast; when it does not come, the failure also says whether the toast reached the
        /// notification center (a banner suppressed or not found) or not (Windows dropped it), and what the app logged
        /// about its notifications.
        /// </summary>
        public async Task<ToastMatch> WaitForBannerAsync(ToastTexts texts)
        {
            try
            {
                return await Toasts.WaitForBannerAsync(App.Automation, texts, BannerTimeout);
            }
            catch (TimeoutException ex)
            {
                string banners = Toasts.Diagnose(App.Automation, texts) ?? "  none";
                string center = string.Empty;
                bool inCenter = await Toasts.InNotificationCenterAsync(App.Automation, async (_, window) =>
                {
                    bool found = await Toasts.FindInAsync(App.Automation, window, texts, TimeSpan.FromSeconds(5)) is not null;
                    center = Toasts.Diagnose(App.Automation, texts) ?? "  none";
                    return found;
                });
                IEnumerable<string> logged = App.ReadLog()
                    .Split('\n')
                    .Where(static line => line.Contains("notification", StringComparison.OrdinalIgnoreCase) || line.Contains("shortcut", StringComparison.OrdinalIgnoreCase))
                    .Select(static line => "  " + line.TrimEnd());
                throw new TimeoutException(
                    $"{ex.Message}{Environment.NewLine}Elements naming the printer after the timeout:{Environment.NewLine}{banners}{Environment.NewLine}"
                    + $"The toast is {(inCenter ? "in" : "NOT in")} the notification center; elements naming the printer there:{Environment.NewLine}{center}{Environment.NewLine}"
                    + $"What the app logged about notifications:{Environment.NewLine}{string.Join(Environment.NewLine, logged)}",
                    ex);
            }
        }

        public async Task SetScenarioAsync(string scenario)
        {
            using HttpResponseMessage response = await Http.PostAsync(new Uri(Printer.BaseUri, "_simulator/scenario/" + scenario), null);
            Assert.True(response.IsSuccessStatusCode, $"The simulator did not switch to '{scenario}': {(int)response.StatusCode}.");
        }

        /// <summary>
        /// The one address a click opened: waits past the app's duplicate window (one click reported both by the toast's
        /// event and the COM toast activator opens once, T-068) and fails when more were opened.
        /// </summary>
        public async Task<string> SingleOpenedUrlAsync()
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            List<string> opened = OpenedUrls();
            Assert.True(opened.Count == 1, $"One click opened {opened.Count} addresses.");
            return opened[0];
        }

        /// <summary>
        /// The app process Windows started for a click after <paramref name="ended"/> ended (marked with this run's folder,
        /// T-068) becomes the app of this run; returns its command line.
        /// </summary>
        public async Task<string> AdoptRestartAsync(int ended)
        {
            int started = 0;
            await Wait.UntilAsync(
                () => (started = AppProcesses.FindMarked(App.RunDirectory).FirstOrDefault(id => id != ended)) != 0,
                TimeSpan.FromSeconds(10),
                "the app process Windows started for the click");
            string commandLine = AppProcesses.MarkedCommandLine(App.RunDirectory, started)
                ?? throw new InvalidOperationException($"The started process {started} is gone.");
            App.AdoptRestart(Process.GetProcessById(started));
            return commandLine;
        }

        /// <summary>What the app logged about toast activations (lines of the app's own log).</summary>
        public string LoggedActivations() => string.Join(
            Environment.NewLine,
            App.ReadLog().Split('\n')
                .Where(static line => line.Contains("toast activator", StringComparison.Ordinal)
                    || line.Contains("Started by Windows", StringComparison.Ordinal)
                    || line.Contains("Notification click", StringComparison.Ordinal))
                .Select(static line => "  " + line.TrimEnd()));

        /// <summary>The addresses the app would have opened in the browser (complete lines only).</summary>
        public List<string> OpenedUrls()
        {
            string file = Path.Combine(App.DataDirectory, AppSwitches.OpenedUrlsFileName);
            try
            {
                using FileStream stream = new(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using StreamReader reader = new(stream, Encoding.UTF8);
                string text = reader.ReadToEnd();
                int end = text.LastIndexOf('\n');
                return end < 0 ? [] : [.. text[..end].Split('\n', StringSplitOptions.RemoveEmptyEntries)];
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                return [];
            }
        }

        /// <summary>
        /// The address is the app's local proxy of the printer (127.0.0.1, not the printer's own port, T-028), and a GET
        /// through it returns the simulator's page without asking for the password (the proxy logs in, T-052).
        /// </summary>
        public async Task AssertOpensThePrinterWebAsync(string address, StringBuilder report)
        {
            Uri uri = new(address);
            // The token in the path is not written to the results.
            report.AppendLine(CultureInfo.InvariantCulture, $"Opened: {uri.Scheme}://{uri.Authority}/… (printer at port {Printer.Port})");
            Assert.Equal("http", uri.Scheme);
            Assert.Equal("127.0.0.1", uri.Host);
            Assert.NotEqual(Printer.Port, uri.Port);

            using HttpResponseMessage response = await Http.GetAsync(uri);
            string body = await response.Content.ReadAsStringAsync();
            report.AppendLine(CultureInfo.InvariantCulture, $"GET through the proxy: {(int)response.StatusCode}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Empty(response.Headers.WwwAuthenticate);
            Assert.Contains("PrusaLink simulator – ", body, StringComparison.Ordinal);
        }

        /// <summary>Do Not Disturb is still off after the toasts (T-065: only checked, the change switched it off when needed).</summary>
        public async Task AssertDoNotDisturbStayedOffAsync(StringBuilder report)
        {
            DoNotDisturbState now = await _doNotDisturb!.ReadAsync();
            report.AppendLine(CultureInfo.InvariantCulture, $"Do Not Disturb at the end: {(now.Enabled ? "on" : "off")} (at the start: {(_doNotDisturb.Original.Enabled ? "on" : "off")})");
            Assert.False(now.Enabled, "Do Not Disturb came on during the toast test.");
        }

        /// <summary>The production Start menu shortcut, byte for byte (or still missing).</summary>
        public void AssertProductionUnchanged() => Assert.Equal(_productionShortcut, ReadProductionShortcut());

        public async ValueTask DisposeAsync()
        {
            List<Exception> failures = [];
            for (int i = _undo.Count - 1; i >= 0; i--)
            {
                try
                {
                    await _undo[i].DisposeAsync();
                }
                catch (Exception ex)
                {
                    failures.Add(ex);
                }
            }

            _undo.Clear();
            if (failures.Count == 1)
            {
                throw new InvalidOperationException("Cleaning up after the toast test failed.", failures[0]);
            }

            if (failures.Count > 1)
            {
                throw new AggregateException("Cleaning up after the toast test failed.", failures);
            }
        }

        private static byte[]? ReadProductionShortcut()
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppSwitches.ProductionShortcutFileName);
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }

        /// <summary>The test settings with the printer of this run and its cool-down thresholds (T-079, T-109), when given.</summary>
        private string Settings(string language, int? cooldownThresholdCelsius)
        {
            JsonObject settings = JsonNode.Parse(TestSettings.Json(language, [new TestPrinter(PrinterId, PrinterName, Printer.Address())]))!.AsObject();
            if (cooldownThresholdCelsius is { } threshold)
            {
                // T-109: the same threshold for the nozzle and the bed.
                settings["printers"]![0]!["cooldownNozzleThresholdCelsius"] = threshold;
                settings["printers"]![0]!["cooldownBedThresholdCelsius"] = threshold;
            }

            return settings.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }

        private async Task StartCoreAsync(E2EFixture fixture, string name, string language, SimulationTiming? timing, int? cooldownThresholdCelsius)
        {
            CancellationToken cancellationToken = TestContext.Current.CancellationToken;
            _productionShortcut = ReadProductionShortcut();

            // Restored last: the key Windows creates with the first toast goes after the toasts are cleared.
            SystemChange<RegistryKeySnapshot> appSettings = await SystemChanges.ToastAppSettingsAsync(cancellationToken);
            _undo.Add(appSettings);
            RegistryKeySnapshot on = ToastAppSettingsKind.WithNotificationsOn(appSettings.Original);
            if (!on.SameAs(appSettings.Original))
            {
                await appSettings.ApplyAsync(on, cancellationToken);
            }

            SystemChange<ToastsEnabledState> enabled = await SystemChanges.ToastsEnabledAsync(cancellationToken);
            _undo.Add(enabled);
            await TurnOnAsync(enabled);

            _doNotDisturb = await SystemChanges.DoNotDisturbAsync(cancellationToken);
            _undo.Add(_doNotDisturb);
            await TurnOffAsync(_doNotDisturb);
            _report.AppendLine(CultureInfo.InvariantCulture, $"Windows {Environment.OSVersion.Version}; ToastEnabled {enabled.Original.ToastEnabled?.ToString(CultureInfo.InvariantCulture) ?? "(missing)"}; "
                + $"test app settings {appSettings.Original}; Do Not Disturb {(_doNotDisturb.Original.Enabled ? "on (switched off)" : "off")}");

            SystemChange<TestToastsState> toasts = await SystemChanges.TestToastsAsync(E2EFixture.AppPath, cancellationToken);
            _undo.Add(toasts);

            Printer = await SimulatedPrinter.StartAsync(new SimulatedPrinterOptions { Scenario = PrinterScenario.Printing, Timing = timing ?? SimulationTiming.Default }, cancellationToken);
            _undo.Add(Printer);
            App = await AppInstance.StartAsync(
                fixture,
                name,
                Settings(language, cooldownThresholdCelsius),
                Simulators.Secrets(PrinterId, Printer),
                new Dictionary<string, string> { [AppSwitches.TestToastsVariable] = "1" });
            _undo.Add(App);

            await Wait.UntilAsync(() => File.Exists(TestToastsKind.ShortcutPath), TimeSpan.FromSeconds(20), "the Start menu shortcut of the test app");
            // Registered before the shortcut that names it (T-068).
            Assert.True(TestToastsKind.ActivatorRegistered(), "The test app did not register its toast activator.");
            // The first status is the baseline (no toast); the scenario switch must come after it. One poll is 5 s.
            await Wait.UntilAsync(() => Printer.RequestCount > 0, TimeSpan.FromSeconds(30), "the app to poll the printer");
            await Task.Delay(TimeSpan.FromSeconds(8), cancellationToken);
        }
    }
}

/// <summary>
/// The child side of <see cref="ToastNotificationTests.ToastsOfAKilledRunAreClearedByTheStartCleanup"/>: runs only in a
/// child test process (<see cref="E2EFixture.ChildVariable"/>) and is skipped everywhere else.
/// </summary>
public sealed class ToastCrashChild
{
    /// <summary>The printer name of the child's toast (unique, chosen by the parent).</summary>
    public const string PrinterNameVariable = "LAYERPEEK_E2E_TOAST_PRINTER";

    private static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(2);

    [Fact]
    public async Task ShowAToastAndWaitToBeKilled()
    {
        string? printerName = Environment.GetEnvironmentVariable(PrinterNameVariable);
        Assert.SkipUnless(E2EFixture.IsChildProcess && printerName is { Length: > 0 }, "Only in the child test process of ToastNotificationTests.");
        SystemChanges.Require();
        string ready = Environment.GetEnvironmentVariable(SystemChangeChild.ReadyVariable) ?? throw new InvalidOperationException($"{SystemChangeChild.ReadyVariable} is not set.");
        int parent = int.Parse(Environment.GetEnvironmentVariable(SystemChangeChild.ParentVariable) ?? throw new InvalidOperationException($"{SystemChangeChild.ParentVariable} is not set."), CultureInfo.InvariantCulture);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        await using SystemChange<RegistryKeySnapshot> appSettings = await SystemChanges.ToastAppSettingsAsync(cancellationToken);
        RegistryKeySnapshot on = ToastAppSettingsKind.WithNotificationsOn(appSettings.Original);
        if (!on.SameAs(appSettings.Original))
        {
            await appSettings.ApplyAsync(on, cancellationToken);
        }

        await using SystemChange<TestToastsState> toasts = await SystemChanges.TestToastsAsync(E2EFixture.AppPath, cancellationToken);
        await using SimulatedPrinter printer = await Simulators.StartPrinterAsync(PrinterScenario.Printing);

        // The fixture of a child has no automation and no app helper: the app is started directly, marked for the parent.
        string run = Path.Combine(E2EFixture.Root, "run-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(Path.Combine(run, "data"));
        await File.WriteAllTextAsync(Path.Combine(run, "data", "settings.json"), TestSettings.Json("en", [new TestPrinter("crash", printerName, printer.Address())]), cancellationToken);
        await File.WriteAllLinesAsync(Path.Combine(run, "data", AppSwitches.SecretsFileName), Simulators.Secrets("crash", printer).Select(static pair => $"{pair.Key}\t{pair.Value}"), cancellationToken);
        ProcessStartInfo start = AppInstance.StartInfo(run, "e2e-" + Guid.NewGuid().ToString("N")[..12]);
        start.Environment[AppSwitches.TestToastsVariable] = "1";
        using Process app = TestLaunch.Start(start);

        await Wait.UntilAsync(() => File.Exists(TestToastsKind.ShortcutPath), TimeSpan.FromSeconds(20), "the Start menu shortcut of the test app");
        await Wait.UntilAsync(() => printer.RequestCount > 0, TimeSpan.FromSeconds(30), "the app to poll the printer");
        await Task.Delay(TimeSpan.FromSeconds(8), cancellationToken);
        printer.Simulation.SetScenario(PrinterScenario.Paused);
        using (UIA3Automation automation = new())
        {
            await Toasts.WaitForBannerAsync(automation, new ToastTexts(printerName, AppStrings.Get("en", "NotificationPrintPaused"), [".bgcode"]), TimeSpan.FromSeconds(30));
        }

        await File.WriteAllTextAsync(ready, Environment.ProcessId.ToString(CultureInfo.InvariantCulture), cancellationToken);
        using Process parentProcess = Process.GetProcessById(parent);
        try
        {
            await parentProcess.WaitForExitAsync(cancellationToken).WaitAsync(MaxWait, cancellationToken);
        }
        catch (TimeoutException)
        {
            // Not killed in time: the changes are restored by the disposals above, the app is killed by the parent.
        }
    }
}
