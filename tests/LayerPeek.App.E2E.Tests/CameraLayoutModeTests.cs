using System.Drawing;
using System.Globalization;
using System.Text;
using System.Text.Json;

using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

using Wait = LayerPeek.App.E2E.Tests.Infrastructure.Wait;

using LayerPeek.App.E2E.Tests.Infrastructure;
using LayerPeek.Simulator.Camera;
using LayerPeek.Simulator.Printers;

namespace LayerPeek.App.E2E.Tests;

/// <summary>
/// The layout mode of the camera window in the real app (T-091, T-093): entered with the button and the context menu, a
/// drag with the real mouse (not simulated by Avalonia) into another anchor and within an anchor, the keyboard, the popover,
/// "Restore defaults", "Done" (only the printer's overlay section of the test settings file is written), Esc (nothing is
/// written), the saved layout after a restart and in full screen, no layout mode in the mini preview and full screen, and
/// "Done" while the settings window has an unsaved change of another field (T-092).
/// </summary>
public sealed class CameraLayoutModeTests(E2EFixture fixture)
{
    private const string PrinterId = "e2e-layout";

    private static readonly TimeSpan ModeTimeout = TimeSpan.FromSeconds(10);

    /// <summary>A long print after a short heating: the cards of a running print stay for the whole test.</summary>
    private static readonly SimulationTiming LongPrint = new() { HeatUp = TimeSpan.FromSeconds(5), Print = TimeSpan.FromMinutes(30) };

    private readonly StringBuilder _report = new();

    [Fact]
    public async Task RealMouseDragMovesAWidgetAndDoneSavesTheLayout()
    {
        E2EFixture.Require();
        await using RtspCamera camera = Simulators.StartCamera();
        await using AppInstance app = await AppInstance.StartAsync(
            fixture,
            "camera-layout",
            TestSettings.Json("en", [new TestPrinter(PrinterId, "Core One", "127.0.0.1:1", Camera: camera.StreamUri)]));

        nint window = await app.OpenAsync($"camera:{PrinterId}", AppWindowKind.Camera);
        AutomationElement element = app.Element(window);
        await Ui.InvokeAsync(element, "CameraLayoutButton");
        await Wait.UntilAsync(() => Ui.IsShown(element, "CameraLayoutOverlay"), TimeSpan.FromSeconds(10), "the layout mode");
        await WindowActivation.ActivateAsync(window);
        Screenshots.Save(window, "camera-layout-mode");

        // Status (top left by default) to the bottom right anchor, with the real mouse in steps.
        await DragCardAsync(window, element, "status", Center((await Ui.FindAsync(element, "CameraLayoutAnchorBottomRight")).BoundingRectangle), "camera-layout-dragging");

        await Ui.InvokeAsync(element, "CameraLayoutDone");
        await Wait.UntilAsync(() => !Ui.IsShown(element, "CameraLayoutOverlay"), TimeSpan.FromSeconds(10), "the layout mode to end");
        await Wait.UntilAsync(() => AnchorInSettings(app, PrinterId, "status") == "BottomRight", TimeSpan.FromSeconds(10), "the status widget in the bottom right anchor of settings.json");
        Screenshots.Save(window, "camera-layout-saved");
        // The window buttons are back (hidden in the layout mode).
        Assert.True(Ui.IsShown(element, "CameraLayoutButton"));
    }

    /// <summary>
    /// The context menu of the image (right click with the real mouse) enters the mode; the keyboard lifts the status card
    /// (Space), moves it to the next anchor (→) and drops it (Space); F2 opens the popover (emphasis, size S), Esc closes it
    /// and a second Esc ends the mode without writing anything. Then a mouse drag, "Restore defaults" and "Done" leave the
    /// default layout (which is not written).
    /// </summary>
    [Fact]
    public async Task ContextMenuKeyboardPopoverEscapeAndRestoreDefaults()
    {
        E2EFixture.Require();
        await using WidgetRun run = await WidgetRun.StartAsync(fixture, "layout-keyboard", new WidgetRunOptions { Timing = LongPrint });
        try
        {
            await run.GuardAsync("layout-keyboard", async () =>
            {
                AppInstance app = run.App;
                nint window = await run.OpenCameraAsync();
                AutomationElement element = app.Element(window);
                await Wait.UntilAsync(() => CameraWidgets.IsShown(element, CameraWidgets.Status), TimeSpan.FromSeconds(60), "the status card");

                // Right click on the image: the context menu with "Edit layout".
                Rectangle image = CameraWidgets.ImageArea(window, element);
                Point middle = Center(image);
                Assert.Equal(window, Native.TopLevelWindowAt(middle));
                Mouse.Position = middle;
                Mouse.Click(MouseButton.Right);
                AutomationElement item = await FindInAppAsync(app, "CameraLayoutMenuItem");
                Screenshots.Save(window, "layout-context-menu");
                item.Patterns.Invoke.Pattern.Invoke();
                await Wait.UntilAsync(() => Ui.IsShown(element, "CameraLayoutOverlay"), ModeTimeout, "the layout mode from the context menu");
                await WindowActivation.ActivateAsync(window, new Point(image.Left + (image.Width / 2), image.Top + (image.Height / 2)));

                // Keyboard: lift the status card, one anchor to the right (top centre), drop it.
                await FocusAsync(window, "CameraLayoutWidget-status");
                WindowActivation.TypeInto(window, VirtualKeyShort.SPACE);
                await Task.Delay(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);
                WindowActivation.TypeInto(window, VirtualKeyShort.RIGHT);
                await Task.Delay(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);
                WindowActivation.TypeInto(window, VirtualKeyShort.SPACE);
                await Wait.UntilAsync(() => InZone(element, "status", "TopCenter"), ModeTimeout, "the status card in the top centre anchor (keyboard)");
                string announcement = Ui.Text(element, "CameraLayoutAnnouncement");
                _report.AppendLine("Announcement after the keyboard move: " + announcement);
                Assert.Contains(AppStrings.Get("en", "CameraOverlayWidgetStatus"), announcement, StringComparison.Ordinal);
                Screenshots.Save(window, "layout-keyboard-moved");

                // F2: the popover; emphasis off, size S; Esc closes it.
                WindowActivation.TypeInto(window, VirtualKeyShort.F2);
                AutomationElement emphasize = await FindInAppAsync(app, "CameraLayoutEmphasize");
                Screenshots.Save(window, "layout-popover");
                Press(emphasize);
                Press(await FindInAppAsync(app, "CameraLayoutSizeS"));
                await Task.Delay(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);
                WindowActivation.TypeInto(window, VirtualKeyShort.ESCAPE);
                await Wait.UntilAsync(() => TryFindInApp(app, "CameraLayoutPopover") is null, ModeTimeout, "Esc to close the popover");
                Assert.True(Ui.IsShown(element, "CameraLayoutOverlay"), "The first Esc (closing the popover) ended the layout mode.");

                // The second Esc ends the mode and drops the changes.
                await WindowActivation.ActivateAsync(window);
                WindowActivation.TypeInto(window, VirtualKeyShort.ESCAPE);
                await Wait.UntilAsync(() => !Ui.IsShown(element, "CameraLayoutOverlay"), ModeTimeout, "the second Esc to end the layout mode");
                await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
                Assert.Null(OverlayInSettings(app, WidgetRun.PrinterId));
                await Wait.UntilAsync(() => CameraWidgets.AnchorOf(element, CameraWidgets.Status) == "TopLeft", ModeTimeout, "the status card back in the top left anchor");
                Screenshots.Save(window, "layout-escaped");

                // Mouse drag, then "Restore defaults" and "Done": the default layout, nothing written.
                await Ui.InvokeAsync(element, "CameraLayoutButton");
                await Wait.UntilAsync(() => Ui.IsShown(element, "CameraLayoutOverlay"), ModeTimeout, "the layout mode from the button");
                await DragCardAsync(window, element, "status", Center((await Ui.FindAsync(element, "CameraLayoutAnchorBottomRight")).BoundingRectangle), "layout-before-reset");
                await Wait.UntilAsync(() => InZone(element, "status", "BottomRight"), ModeTimeout, "the status card in the bottom right anchor (mouse)");
                await Ui.InvokeAsync(element, "CameraLayoutReset");
                await Wait.UntilAsync(() => InZone(element, "status", "TopLeft"), ModeTimeout, "the status card back in the top left anchor after Restore defaults");
                await Ui.InvokeAsync(element, "CameraLayoutDone");
                await Wait.UntilAsync(() => !Ui.IsShown(element, "CameraLayoutOverlay"), ModeTimeout, "Done to end the layout mode");
                await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
                string? anchor = AnchorInSettings(app, WidgetRun.PrinterId, "status");
                Assert.True(anchor is null or "TopLeft", $"The status widget is saved in {anchor} after Restore defaults.");
                Assert.Equal("TopLeft", CameraWidgets.AnchorOf(element, CameraWidgets.Status));
            });
        }
        finally
        {
            Screenshots.SaveText(_report.ToString(), "layout-keyboard-report");
        }
    }

    /// <summary>
    /// A changed order within an anchor (times dragged above the cool-down card) and a move to another anchor, saved with
    /// "Done", come back after a restart of the app and apply to full screen; the mini preview and full screen offer no
    /// layout mode.
    /// </summary>
    [Fact]
    public async Task ReorderedLayoutSurvivesARestartAndAppliesToFullScreen()
    {
        E2EFixture.Require();
        await using WidgetRun run = await WidgetRun.StartAsync(fixture, "layout-restart", new WidgetRunOptions { Timing = LongPrint });
        try
        {
            await run.GuardAsync("layout-restart", async () =>
            {
                AppInstance app = run.App;
                nint window = await run.OpenCameraAsync();
                AutomationElement element = app.Element(window);
                await Wait.UntilAsync(() => CameraWidgets.IsShown(element, CameraWidgets.Times), TimeSpan.FromSeconds(60), "the times card");
                await Ui.InvokeAsync(element, "CameraLayoutButton");
                await Wait.UntilAsync(() => Ui.IsShown(element, "CameraLayoutOverlay"), ModeTimeout, "the layout mode");
                await WindowActivation.ActivateAsync(window);

                // Status to the bottom right anchor.
                await DragCardAsync(window, element, "status", Center((await Ui.FindAsync(element, "CameraLayoutAnchorBottomRight")).BoundingRectangle), "layout-restart-status");
                await Wait.UntilAsync(() => InZone(element, "status", "BottomRight"), ModeTimeout, "the status card in the bottom right anchor");

                // Times (at the bottom edge) above the cool-down card in the bottom left anchor.
                Rectangle cooldown = LayoutCard(element, "cooldown");
                Rectangle times = LayoutCard(element, "times");
                _report.AppendLine(CultureInfo.InvariantCulture, $"Before the reorder: times {times}, cool-down {cooldown}");
                Assert.True(times.Top > cooldown.Top, $"The times card {times} is not below the cool-down card {cooldown} in the default layout.");
                await DragCardAsync(window, element, "times", new Point(cooldown.Left + (cooldown.Width / 2), cooldown.Top + Math.Max(3, cooldown.Height / 5)), "layout-restart-reorder");
                await Wait.UntilAsync(() => LayoutCard(element, "times").Top < LayoutCard(element, "cooldown").Top, ModeTimeout, "the times card above the cool-down card");
                Screenshots.Save(window, "layout-restart-reordered");

                await Ui.InvokeAsync(element, "CameraLayoutDone");
                await Wait.UntilAsync(() => !Ui.IsShown(element, "CameraLayoutOverlay"), ModeTimeout, "Done to end the layout mode");
                await Wait.UntilAsync(
                    () => AnchorInSettings(app, WidgetRun.PrinterId, "status") == "BottomRight" && OrderInSettings(app, WidgetRun.PrinterId, "BottomLeft") is ["cooldown", "times", ..],
                    ModeTimeout,
                    "settings.json with status in the bottom right anchor and cool-down before times (from the edge) in the bottom left one");

                // After a restart of the app.
                await app.RestartAsync();
                window = await run.OpenCameraAsync();
                element = app.Element(window);
                await Wait.UntilAsync(() => CameraWidgets.AnchorOf(element, CameraWidgets.Status) == "BottomRight", TimeSpan.FromSeconds(60), "the status card in the bottom right anchor after the restart");
                await Ui.InvokeAsync(element, "CameraLayoutButton");
                await Wait.UntilAsync(() => Ui.IsShown(element, "CameraLayoutOverlay"), ModeTimeout, "the layout mode after the restart");
                Assert.True(LayoutCard(element, "times").Top < LayoutCard(element, "cooldown").Top, "The order in the bottom left anchor was not kept by the restart.");
                Screenshots.Save(window, "layout-restart-after");
                await WindowActivation.ActivateAsync(window);
                WindowActivation.TypeInto(window, VirtualKeyShort.ESCAPE);
                await Wait.UntilAsync(() => !Ui.IsShown(element, "CameraLayoutOverlay"), ModeTimeout, "Esc to end the layout mode");

                // Full screen: the same layout, no layout mode.
                await Ui.InvokeAsync(element, "CameraFullScreenButton");
                await Wait.UntilAsync(() => WindowRect(window).Equals(Native.MonitorOf(window).Monitor), ModeTimeout, "full screen");
                Native.Rect monitor = Native.MonitorOf(window).Monitor;
                Rectangle area = new(monitor.Left, monitor.Top, monitor.Width, monitor.Height);
                await Wait.UntilAsync(
                    () => CameraWidgets.AnchorOf(element, CameraWidgets.Status) == "BottomRight" && CameraWidgets.PlacementProblem(CameraWidgets.Card(element, CameraWidgets.Status)!.BoundingRectangle, area, "BottomRight") is null,
                    ModeTimeout,
                    "the status card in the bottom right corner of full screen");
                Assert.Null(Ui.TryFind(element, "CameraLayoutButton"));
                Screenshots.Save(window, "layout-restart-full-screen");

                // The mini preview: no layout mode either.
                await Ui.InvokeAsync(element, "CameraMiniButton");
                await Wait.UntilAsync(() => Ui.IsShown(element, "CameraMiniInfoStrip"), ModeTimeout, "the mini preview");
                Assert.Null(Ui.TryFind(element, "CameraLayoutButton"));
            });
        }
        finally
        {
            Screenshots.SaveText(_report.ToString(), "layout-restart-report");
        }
    }

    /// <summary>
    /// "Done" in the camera window while the settings window is open with an unsaved change of another field (the printer's
    /// name): the settings window takes over the saved layout and keeps its change, so its "Save" writes both (T-077 Q3, T-092).
    /// </summary>
    [Fact]
    public async Task DoneWhileTheSettingsAreOpenKeepsTheirUnsavedChange()
    {
        E2EFixture.Require();
        const string Renamed = "Core One renamed in E2E";
        await using WidgetRun run = await WidgetRun.StartAsync(fixture, "layout-settings", new WidgetRunOptions { Timing = LongPrint });
        await run.GuardAsync("layout-settings", async () =>
        {
            AppInstance app = run.App;
            nint settings = await app.OpenAsync("settings", AppWindowKind.Settings);
            AutomationElement settingsElement = app.Element(settings);
            (await Ui.ItemsAsync(settingsElement, "SettingsPrinterList", 1))[0].Patterns.SelectionItem.Pattern.Select();
            await Ui.SelectAsync(settingsElement, "PrinterTabConnection");
            await Ui.SetTextAsync(settingsElement, "PrinterName", Renamed);

            nint window = await run.OpenCameraAsync();
            AutomationElement element = app.Element(window);
            await Ui.InvokeAsync(element, "CameraLayoutButton");
            await Wait.UntilAsync(() => Ui.IsShown(element, "CameraLayoutOverlay"), ModeTimeout, "the layout mode");
            await WindowActivation.ActivateAsync(window);
            await DragCardAsync(window, element, "status", Center((await Ui.FindAsync(element, "CameraLayoutAnchorBottomRight")).BoundingRectangle), "layout-settings-dragging");
            await Ui.InvokeAsync(element, "CameraLayoutDone");
            await Wait.UntilAsync(() => AnchorInSettings(app, WidgetRun.PrinterId, "status") == "BottomRight", ModeTimeout, "the layout saved from the camera window");
            Assert.NotEqual(Renamed, NameInSettings(app, WidgetRun.PrinterId));

            // The settings window still has the new name, and its Save keeps the layout saved meanwhile.
            Assert.Equal(Renamed, (await Ui.FindAsync(settingsElement, "PrinterName")).Patterns.Value.Pattern.Value.ValueOrDefault);
            Screenshots.Save(settings, "layout-settings-before-save");
            await CameraWindowSettings.CloseAsync(settings, settingsElement, save: true);
            await Wait.UntilAsync(() => NameInSettings(app, WidgetRun.PrinterId) == Renamed, ModeTimeout, "the new name in settings.json");
            Assert.Equal("BottomRight", AnchorInSettings(app, WidgetRun.PrinterId, "status"));
            Assert.Equal("BottomRight", CameraWidgets.AnchorOf(element, CameraWidgets.Status));
        });
    }

    /// <summary>Drags the layout card of <paramref name="widgetId"/> with the real mouse in steps to <paramref name="target"/>.</summary>
    private static async Task DragCardAsync(nint window, AutomationElement element, string widgetId, Point target, string screenshot)
    {
        Rectangle card = (await Ui.FindAsync(element, "CameraLayoutWidget-" + widgetId)).BoundingRectangle;
        Point start = Center(card);
        Assert.Equal(window, Native.TopLevelWindowAt(start));
        Mouse.Position = start;
        Mouse.Down(MouseButton.Left);
        try
        {
            for (int step = 1; step <= 10; step++)
            {
                Mouse.Position = new Point(start.X + ((target.X - start.X) * step / 10), start.Y + ((target.Y - start.Y) * step / 10));
                await Task.Delay(30, TestContext.Current.CancellationToken);
            }

            Screenshots.Save(window, screenshot);
        }
        finally
        {
            Mouse.Up(MouseButton.Left);
        }
    }

    private static Point Center(Rectangle rectangle) => new(rectangle.Left + (rectangle.Width / 2), rectangle.Top + (rectangle.Height / 2));

    private static Rectangle LayoutCard(AutomationElement element, string widgetId) =>
        Ui.TryFind(element, "CameraLayoutWidget-" + widgetId)?.BoundingRectangle ?? Rectangle.Empty;

    /// <summary>The layout card of <paramref name="widgetId"/> lies in the zone of <paramref name="anchor"/>.</summary>
    private static bool InZone(AutomationElement element, string widgetId, string anchor)
    {
        Rectangle card = LayoutCard(element, widgetId);
        Rectangle zone = Ui.TryFind(element, "CameraLayoutAnchor" + anchor)?.BoundingRectangle ?? Rectangle.Empty;
        return !card.IsEmpty && zone.Contains(Center(card));
    }

    /// <summary>Focuses an element of the window with Tab (at most 12 presses).</summary>
    private async Task FocusAsync(nint window, string automationId)
    {
        List<string> seen = [];
        for (int i = 0; i < 12; i++)
        {
            string focused = fixture.Automation.FocusedElement()?.Properties.AutomationId.ValueOrDefault ?? string.Empty;
            if (focused == automationId)
            {
                return;
            }

            seen.Add(focused);
            WindowActivation.TypeInto(window, VirtualKeyShort.TAB);
            await Task.Delay(TimeSpan.FromMilliseconds(250), TestContext.Current.CancellationToken);
        }

        Assert.Fail($"Tab never focused {automationId} (focused: {string.Join(" → ", seen)}).");
    }

    /// <summary>Invokes, selects or toggles an element (buttons, radio buttons, toggle switches).</summary>
    private static void Press(AutomationElement element)
    {
        if (element.Patterns.Invoke.TryGetPattern(out FlaUI.Core.Patterns.IInvokePattern? invoke) && invoke is not null)
        {
            invoke.Invoke();
        }
        else if (element.Patterns.SelectionItem.TryGetPattern(out FlaUI.Core.Patterns.ISelectionItemPattern? selection) && selection is not null)
        {
            selection.Select();
        }
        else if (element.Patterns.Toggle.TryGetPattern(out FlaUI.Core.Patterns.ITogglePattern? toggle) && toggle is not null)
        {
            toggle.Toggle();
        }
        else
        {
            throw new InvalidOperationException($"{WindowChecks.Describe(element)} can be neither invoked, selected nor toggled.");
        }
    }

    /// <summary>An element in any window of the app (popups such as context menus and flyouts are windows of their own).</summary>
    private static AutomationElement? TryFindInApp(AppInstance app, string automationId)
    {
        foreach (nint hwnd in app.Windows())
        {
            try
            {
                if (app.Element(hwnd).FindFirstDescendant(cf => cf.ByAutomationId(automationId)) is { } found && !found.Properties.IsOffscreen.ValueOrDefault)
                {
                    return found;
                }
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or TimeoutException or FlaUI.Core.Exceptions.ElementNotAvailableException)
            {
                // A popup closing right now.
            }
        }

        return null;
    }

    private static async Task<AutomationElement> FindInAppAsync(AppInstance app, string automationId)
    {
        AutomationElement? found = null;
        await Wait.UntilAsync(() => (found = TryFindInApp(app, automationId)) is not null, ModeTimeout, $"'{automationId}' in a window of the app");
        return found!;
    }

    /// <summary>The <c>camera.overlay</c> section of the printer in the test settings file, or <see langword="null"/>.</summary>
    private static JsonElement? OverlayInSettings(AppInstance app, string printerId)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(app.ReadSettings());
            foreach (JsonElement printer in document.RootElement.GetProperty("printers").EnumerateArray())
            {
                if (printer.GetProperty("id").GetString() == printerId)
                {
                    return printer.TryGetProperty("camera", out JsonElement camera) && camera.TryGetProperty("overlay", out JsonElement overlay) ? overlay.Clone() : null;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // Being written: read again.
        }

        return null;
    }

    /// <summary>The anchor of <paramref name="widgetId"/> in the printer's overlay section of the test settings file.</summary>
    private static string? AnchorInSettings(AppInstance app, string printerId, string widgetId) =>
        OverlayInSettings(app, printerId) is { } overlay && overlay.TryGetProperty("widgets", out JsonElement widgets)
            ? widgets.EnumerateArray().Where(widget => widget.GetProperty("id").GetString() == widgetId).Select(static widget => widget.GetProperty("anchor").GetString()).FirstOrDefault()
            : null;

    /// <summary>The widgets of <paramref name="anchor"/> in the saved order (from the window edge inward).</summary>
    private static List<string> OrderInSettings(AppInstance app, string printerId, string anchor) =>
        OverlayInSettings(app, printerId) is { } overlay && overlay.TryGetProperty("widgets", out JsonElement widgets)
            ? [.. widgets.EnumerateArray().Where(widget => widget.GetProperty("anchor").GetString() == anchor).Select(static widget => widget.GetProperty("id").GetString() ?? string.Empty)]
            : [];

    private static string? NameInSettings(AppInstance app, string printerId)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(app.ReadSettings());
            return document.RootElement.GetProperty("printers").EnumerateArray()
                .Where(printer => printer.GetProperty("id").GetString() == printerId)
                .Select(static printer => printer.GetProperty("name").GetString())
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    private static Native.Rect WindowRect(nint hwnd)
    {
        Assert.True(Native.GetWindowRect(hwnd, out Native.Rect rect));
        return rect;
    }
}
