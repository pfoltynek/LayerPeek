using System.Globalization;
using System.Text;

using FlaUI.Core.AutomationElements;

using PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;
using PrusaTrayDiagnostic.Simulator.Camera;

namespace PrusaTrayDiagnostic.App.E2E.Tests;

/// <summary>
/// The real jump list of the auxiliary windows on the Windows 11 taskbar (T-043, T-072). The windows carry
/// PreventPinning and the AppUserModelID (checked by T-044), so the jump list must offer only closing the window: no
/// entry that starts the app, no "Pin to taskbar", no tasks or recent items. The taskbar button is right-clicked with the
/// real mouse, the jump list is read through UI Automation by the kind and number of its entries (not by the language of
/// Windows; the texts go to the report), Escape closes it and its close entry closes the window.
/// </summary>
public sealed class TaskbarJumpListTests(E2EFixture fixture)
{
    private const string PrinterId = "e2e-jump-list";

    [Fact]
    public async Task JumpListOffersOnlyCloseWindow()
    {
        E2EFixture.Require();
        await using RtspCamera camera = Simulators.StartCamera();
        await using AppInstance app = await AppInstance.StartAsync(
            fixture,
            "jump-list",
            TestSettings.Json("en", [new TestPrinter(PrinterId, "Core One", "127.0.0.1:1", Camera: camera.StreamUri)]));
        StringBuilder report = new();
        bool userButtonBefore = TaskbarJumpList.FindButton(app.Automation, TaskbarJumpList.ProductionButtonId) is not null;
        report.AppendLine(CultureInfo.InvariantCulture, $"Taskbar button of the user's own instance before: {userButtonBefore}");

        (string Command, AppWindowKind Kind, string Label)[] windows =
        [
            ("settings", AppWindowKind.Settings, "settings"),
            ("diagnostics", AppWindowKind.Diagnostics, "diagnostics"),
            ("about", AppWindowKind.Other, "about"),
            ($"camera:{PrinterId}", AppWindowKind.Camera, "camera"),
        ];
        TaskbarJumpList.JumpList? list = null;
        try
        {
            foreach ((string command, AppWindowKind kind, string label) in windows)
            {
                // One window at a time: the button then stands for exactly this window.
                await TaskbarJumpList.WaitForNoButtonAsync(app.Automation);
                nint window = await app.OpenAsync(command, kind);
                AutomationElement button = await TaskbarJumpList.WaitForButtonAsync(app.Automation);
                string buttonName = button.Properties.Name.ValueOrDefault ?? string.Empty;
                report.AppendLine(CultureInfo.InvariantCulture, $"{label}: button '{buttonName}' (id '{button.Properties.AutomationId.ValueOrDefault}')");
                // "Prusa Tray Diagnostic" (the exe's file description), then the localized window count.
                Assert.Matches("^Prusa Tray Diagnostic($|[^A-Za-z0-9])", buttonName);
                await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
                TaskbarJumpList.ScreenshotButton(button, $"jump-list-{label}-button");

                // The jump list: only the close entry; Escape closes it and leaves the window open.
                list = await TaskbarJumpList.OpenAsync(app.Automation, button);
                report.AppendLine(CultureInfo.InvariantCulture, $"{label}: jump list {list.Describe()}");
                await list.ScreenshotAsync($"jump-list-{label}");
                AssertOnlyCloseEntry(list, label);
                await list.DismissAsync();
                Assert.True(Native.IsWindowVisible(window), $"Escape on the jump list closed the {label} window.");

                // The close entry closes the window, and with it the button.
                list = await TaskbarJumpList.OpenAsync(app.Automation, button);
                AutomationElement close = AssertOnlyCloseEntry(list, label);
                await list.RunAsync(close);
                await Wait.UntilAsync(() => !Native.IsWindowVisible(window), TimeSpan.FromSeconds(10), $"the {label} window closed from its jump list");
                await TaskbarJumpList.WaitForNoButtonAsync(app.Automation);
            }

            bool userButtonAfter = TaskbarJumpList.FindButton(app.Automation, TaskbarJumpList.ProductionButtonId) is not null;
            report.AppendLine(CultureInfo.InvariantCulture, $"Taskbar button of the user's own instance after: {userButtonAfter}");
            Assert.Equal(userButtonBefore, userButtonAfter);
            Assert.Equal([app.Process.Id], AppProcesses.FindMarked(app.RunDirectory));
        }
        finally
        {
            // A failed check must not leave the jump list open on the shared desktop.
            // Best effort: a failure here must not replace the test's own failure.
            if (list is { IsOpen: true })
            {
                try
                {
                    await list.DismissAsync();
                }
                catch (TimeoutException ex)
                {
                    report.AppendLine(CultureInfo.InvariantCulture, $"Closing the jump list after the failure: {ex.Message}");
                }
            }

            Screenshots.SaveText(report.ToString(), "jump-list-report");
        }
    }

    /// <summary>Exactly one entry, the system entry that closes the window: nothing that starts or pins the app.</summary>
    private static AutomationElement AssertOnlyCloseEntry(TaskbarJumpList.JumpList list, string label)
    {
        AutomationElement[] entries = list.Entries();
        string description = list.Describe();
        AutomationElement close = Assert.Single(entries);
        Assert.True(
            close.Properties.AutomationId.ValueOrDefault == TaskbarJumpList.CloseEntryId,
            $"The {label} jump list must offer only closing the window: {description}");
        return close;
    }
}
