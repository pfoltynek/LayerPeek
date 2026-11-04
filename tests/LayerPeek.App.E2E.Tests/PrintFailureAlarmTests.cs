using System.Text;
using System.Text.Json.Nodes;

using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

using Wait = LayerPeek.App.E2E.Tests.Infrastructure.Wait;

using LayerPeek.App.E2E.Tests.Infrastructure;
using LayerPeek.Simulator.Camera;

namespace LayerPeek.App.E2E.Tests;

/// <summary>
/// The print failure alarm of the real app (T-154) with the detector of the end-to-end tests (Debug test mode,
/// <see cref="AppSwitches.TestFakeDetectorVariable"/>: every sample of a lit camera is a failure) against the simulated
/// printer (printing) and RTSP camera (T-045), in the test mode with real toasts (T-067): the toast comes with the printer
/// name and "Possible print failure", the snapshot is saved next to the recordings, "False alarm" in the toast records the
/// alarm locally, and a click on the toast opens the camera window of the printer. Windows only (UI Automation); system
/// changes like the other toast tests (T-065).
/// </summary>
public sealed class PrintFailureAlarmTests(E2EFixture fixture)
{
    /// <summary>Lease, three samples within the confirmation window (~6 s at 2 s), the snapshot and Windows' delivery.</summary>
    private static readonly TimeSpan AlarmTimeout = TimeSpan.FromSeconds(60);

    [Fact]
    [Trait(SystemChanges.Trait, SystemChangeTraits.Toasts)]
    [Trait(SystemChanges.Trait, SystemChangeTraits.DoNotDisturb)]
    public async Task AnAlarmShowsItsToastSavesItsSnapshotAndTheButtonsWork()
    {
        SystemChanges.Require();
        StringBuilder report = new();
        await using RtspCamera camera = Simulators.StartCamera();
        await using ToastNotificationTests.ToastRun run = await ToastNotificationTests.ToastRun.StartAsync(
            fixture,
            "print-failure",
            "en",
            report,
            options: new ToastRunOptions
            {
                Switches = new Dictionary<string, string> { [AppSwitches.TestFakeDetectorVariable] = "1" },
                EditSettings = settings =>
                {
                    settings["detection"] = new JsonObject { ["enabled"] = true };
                    JsonObject printer = settings["printers"]!.AsArray()[0]!.AsObject();
                    printer["camera"] = new JsonObject { ["enabled"] = true, ["streamUrl"] = camera.StreamUri.ToString() };
                    printer["detection"] = new JsonObject { ["enabled"] = true };
                },
            });
        ToastTexts texts = new(run.PrinterName, AppStrings.Get("en", "NotificationPrintFailure").Replace("{0}", "Detector e2e-alarm", StringComparison.Ordinal), [".bgcode"]);

        ToastMatch banner = await Toasts.WaitForBannerAsync(run.App.Automation, texts, AlarmTimeout);
        report.AppendLine("Banner elements:").AppendLine(banner.Describe());
        Assert.True(banner.HasExactly(texts.PrinterName), $"The toast title is not the printer name:{Environment.NewLine}{banner.Describe()}");
        Assert.True(banner.HasLine(texts.Body), $"The toast text is not '{texts.Body}':{Environment.NewLine}{banner.Describe()}");

        // The snapshot next to the recordings (saving is on by default).
        string recordings = Path.Combine(run.App.DataDirectory, "Recordings");
        await Wait.UntilAsync(() => Snapshots(recordings).Length == 1, TimeSpan.FromSeconds(10), "the snapshot of the alarm");

        // "False alarm" in the toast: a local record, nothing opens.
        AutomationElement? falseAlarm = null;
        string falseAlarmName = AppStrings.Get("en", "NotificationFalseAlarm");
        await Wait.UntilAsync(
            () => (falseAlarm = run.App.Automation.FromHandle(banner.Window).FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName(falseAlarmName)))) is not null,
            TimeSpan.FromSeconds(10),
            "the False alarm button of the toast");
        falseAlarm!.Patterns.Invoke.Pattern.Invoke();
        string records = Path.Combine(run.App.DataDirectory, "detection", "false-alarms");
        await Wait.UntilAsync(() => Directory.Exists(records) && Directory.GetFiles(records, "*.json").Length == 1, TimeSpan.FromSeconds(15), "the record of the false alarm");
        Assert.DoesNotContain(run.App.Windows(), hwnd => run.App.KindOf(hwnd) == AppWindowKind.Camera);

        // Once per print and kind (the default): the alarm does not come again; a click on it in the notification center opens
        // the camera window.
        string activation = await Toasts.InNotificationCenterAsync(run.App.Automation, async (_, window) =>
        {
            ToastMatch toast = await Toasts.FindInAsync(run.App.Automation, window, texts, TimeSpan.FromSeconds(10))
                ?? throw new InvalidOperationException($"The alarm toast of '{texts.PrinterName}' is not in the notification center.");
            return await Toasts.ActivateAsync(toast, () => run.App.Windows().Any(hwnd => run.App.KindOf(hwnd) == AppWindowKind.Camera), TimeSpan.FromSeconds(15), mouseOnly: true);
        });
        report.AppendLine("Camera window opened by " + activation);
        Screenshots.SaveText(report.ToString(), "print-failure-report");
    }

    private static string[] Snapshots(string recordings) =>
        Directory.Exists(recordings) ? Directory.GetFiles(recordings, "*_alarm-e2e-alarm.jpg", SearchOption.AllDirectories) : [];
}
