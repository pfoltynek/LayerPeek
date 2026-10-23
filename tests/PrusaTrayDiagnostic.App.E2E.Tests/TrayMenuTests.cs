using System.Diagnostics;
using System.Globalization;
using System.Text;

using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

using PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;
using PrusaTrayDiagnostic.Simulator.Camera;

namespace PrusaTrayDiagnostic.App.E2E.Tests;

/// <summary>
/// The real tray menu (T-042) from the Windows notification area: the test instance's icon (tooltip prefix <c>[E2E]</c>,
/// often in the overflow flyout) is right-clicked with the real mouse (the only step that moves it), and the menu, an
/// Avalonia popup of the app, is read and used through UI Automation: printer submenu, "Show camera" / "Hide camera",
/// Diagnostics… (main menu since 2026-10-21), Settings…, About and Quit with their access keys.
/// </summary>
public sealed class TrayMenuTests(E2EFixture fixture)
{
    private const string PrinterId = "e2e-tray";

    [Fact]
    public async Task RealTrayMenuShowsThePrinterSubmenuAndTogglesTheCamera()
    {
        E2EFixture.Require();
        await using RtspCamera camera = Simulators.StartCamera();
        await using AppInstance app = await AppInstance.StartAsync(
            fixture,
            "tray-menu",
            TestSettings.Json("en", [new TestPrinter(PrinterId, "Core One", "127.0.0.1:1", Camera: camera.StreamUri)]));
        // The menu follows the monitored printers: wait until the camera window can open (same moment the menu has the item).
        nint cameraWindow = await app.OpenAsync($"camera:{PrinterId}", AppWindowKind.Camera);
        await app.SendAsync($"camera:{PrinterId}");
        await Wait.UntilAsync(() => !Native.IsWindowVisible(cameraWindow), TimeSpan.FromSeconds(10), "the camera window to close");

        // Open: "Show camera" (a plain item, no check mark since 2026-10-22), the camera window opens.
        AutomationElement printerItem = await OpenPrinterSubmenuAsync(app, "tray-menu-1");
        AutomationElement cameraItem = await SubmenuItemAsync(app, printerItem, "Show camera");
        nint submenu = MenuWindowOf(app, cameraItem);
        // "Open PrusaLink" is first on screen (see the screenshot) but not reported as a menu item by UI Automation.
        string[] submenuItems = [.. app.Element(submenu).FindAllDescendants(cf => cf.ByControlType(ControlType.MenuItem)).Select(static item => item.Name)];
        Assert.Equal("Show camera", submenuItems[^1]);
        Assert.DoesNotContain("Hide camera", submenuItems);
        Assert.DoesNotContain("Diagnostics…", submenuItems);
        Screenshots.Save(submenu, "tray-menu-2-printer-submenu");
        cameraItem.Patterns.Invoke.Pattern.Invoke();
        nint opened = await app.WaitForWindowAsync(AppWindowKind.Camera, [cameraWindow]);

        // Close: "Hide camera" now, the window closes.
        printerItem = await OpenPrinterSubmenuAsync(app, "tray-menu-3");
        cameraItem = await SubmenuItemAsync(app, printerItem, "Hide camera");
        cameraItem.Patterns.Invoke.Pattern.Invoke();
        await Wait.UntilAsync(() => !Native.IsWindowVisible(opened), TimeSpan.FromSeconds(10), "the camera window to close from the menu");

        // Diagnostics… of the main menu (2026-10-21, enabled with a printer) opens the diagnostics window.
        AutomationElement menu = await OpenMenuAsync(app);
        AutomationElement diagnosticsItem = menu.FindAllDescendants(cf => cf.ByControlType(ControlType.MenuItem)).First(item => item.Name == "Diagnostics…");
        Assert.True(diagnosticsItem.IsEnabled);
        diagnosticsItem.Patterns.Invoke.Pattern.Invoke();
        await app.WaitForWindowAsync(AppWindowKind.Diagnostics);

        // Settings… from the menu opens the settings window.
        menu = await OpenMenuAsync(app);
        AutomationElement settingsItem = menu.FindAllDescendants(cf => cf.ByControlType(ControlType.MenuItem)).First(item => item.Name == "Settings…");
        settingsItem.Patterns.Invoke.Pattern.Invoke();
        await app.WaitForWindowAsync(AppWindowKind.Settings);
    }

    /// <summary>Right-clicks the test instance's tray icon and returns the menu (checking its top level); also used by T-070.</summary>
    internal static async Task<AutomationElement> OpenMenuAsync(AppInstance app, string? screenshot = null)
    {
        StringBuilder report = new();
        Stopwatch clock = Stopwatch.StartNew();
        void Trace(string line) => report.AppendLine(CultureInfo.InvariantCulture, $"{clock.ElapsedMilliseconds,6} ms  {line}");
        try
        {
            return await OpenMenuCoreAsync(app, screenshot, Trace);
        }
        catch (Exception ex)
        {
            Trace($"Failed: {ex}");
            throw new InvalidOperationException($"Opening the tray menu failed ({ex.Message}); diagnostics: {SaveMenuDiagnostics(app, report, Trace)}", ex);
        }
    }

    /// <summary>
    /// What the next person needs to analyse a failed menu opening without reproducing it: the trace of the attempt, the
    /// desktop (notification area, popups), the test icons and the app's windows now, and the app's log.
    /// </summary>
    private static string SaveMenuDiagnostics(AppInstance app, StringBuilder report, Action<string> trace)
    {
        try
        {
            Native.GetCursorPos(out Native.Point cursor);
            trace($"Now: cursor=({cursor.X},{cursor.Y}) foreground={DescribeWindow(Native.GetForegroundWindow())}");
            foreach (nint hwnd in app.Windows())
            {
                trace($"App window: {DescribeWindow(hwnd)}");
            }

            foreach (string icon in NotificationArea.DescribeAll(app.Automation))
            {
                trace($"Icon now: {icon}");
            }

            trace($"Screenshot: {Screenshots.SaveScreen("tray-menu-failure-screen")}");
        }
        catch (Exception ex)
        {
            trace($"Collecting the diagnostics failed: {ex}");
        }

        // Called while the menu failure is being reported: a failure here must not replace that one.
        try
        {
            report.AppendLine().AppendLine("App log:").AppendLine(app.ReadLog());
            return Screenshots.SaveText(report.ToString(), "tray-menu-failure");
        }
        catch (Exception ex)
        {
            return $"(not saved: {ex.Message})";
        }
    }

    private static string DescribeWindow(nint hwnd)
    {
        Native.GetWindowRect(hwnd, out Native.Rect rect);
        Native.GetWindowThreadProcessId(hwnd, out uint process);
        return $"0x{hwnd:X} pid={process} class={Native.ClassName(hwnd)} title='{Native.Title(hwnd)}' rect={rect} visible={Native.IsWindowVisible(hwnd)}";
    }

    private static async Task<AutomationElement> OpenMenuCoreAsync(AppInstance app, string? screenshot, Action<string> trace)
    {
        List<nint> before = app.Windows();
        foreach (nint hwnd in before)
        {
            trace($"App window before: {DescribeWindow(hwnd)}");
        }

        (IReadOnlyList<AutomationElement> icons, bool openedOverflow) = await NotificationArea.FindTestIconsAsync(app.Automation, trace);
        AutomationElement? menu = null;
        HashSet<nint> seen = [];
        try
        {
            // Several matches: icons of killed test instances can linger; only the live one opens a menu of this app.
            for (int i = 0; i < icons.Count; i++)
            {
                trace($"Right-click #{i}: {NotificationArea.Describe(icons[i])}");
                NotificationArea.RightClick(icons[i]);
                Native.GetCursorPos(out Native.Point cursor);
                trace($"Clicked at ({cursor.X},{cursor.Y}), foreground={DescribeWindow(Native.GetForegroundWindow())}");
                try
                {
                    await Wait.UntilAsync(
                        () =>
                        {
                            List<nint> now = app.Windows();
                            foreach (nint hwnd in now.Where(hwnd => !before.Contains(hwnd) && seen.Add(hwnd)))
                            {
                                trace($"New window: {DescribeWindow(hwnd)}");
                            }

                            foreach (nint hwnd in seen.Where(hwnd => !now.Contains(hwnd)).ToArray())
                            {
                                trace($"Window gone: 0x{hwnd:X}");
                                seen.Remove(hwnd);
                            }

                            return (menu = now.Where(hwnd => !before.Contains(hwnd)).Select(app.Element)
                                .FirstOrDefault(window => window.FindFirstDescendant(cf => cf.ByControlType(ControlType.MenuItem)) is not null)) is not null;
                        },
                        TimeSpan.FromSeconds(icons.Count == 1 ? 10 : 3),
                        "the tray menu");
                    trace($"Menu: 0x{menu!.Properties.NativeWindowHandle.ValueOrDefault:X}");
                    break;
                }
                catch (TimeoutException) when (i < icons.Count - 1)
                {
                    // A ghost icon: try the next one.
                }
            }
        }
        finally
        {
            if (menu is null)
            {
                NotificationArea.CloseOverflow(openedOverflow);
            }
        }

        AutomationElement[] items = menu!.FindAllChildren().SelectMany(static child => child.FindAllChildren()).Where(static e => e.ControlType == ControlType.MenuItem).ToArray();
        if (items.Length == 0)
        {
            items = menu.FindAllDescendants(cf => cf.ByControlType(ControlType.MenuItem));
        }

        string[] names = [.. items.Select(static item => item.Name)];
        Assert.True(names.Length >= 5, $"Menu items: {string.Join(" | ", names)}");
        Assert.StartsWith("Core One", names[0], StringComparison.Ordinal);
        Assert.Equal(["Diagnostics…", "Settings…", "About", "Quit"], names[^4..]);
        Assert.Equal(["D", "S", "A", "Q"], items[^4..].Select(static item => item.Properties.AccessKey.ValueOrDefault ?? string.Empty).Select(static key => key.Replace("Alt+", string.Empty, StringComparison.OrdinalIgnoreCase)).ToArray());
        if (screenshot is not null)
        {
            Screenshots.Save(menu.Properties.NativeWindowHandle.ValueOrDefault, screenshot);
        }

        return menu;
    }

    private static async Task<AutomationElement> OpenPrinterSubmenuAsync(AppInstance app, string screenshot)
    {
        AutomationElement menu = await OpenMenuAsync(app, screenshot);
        AutomationElement printer = menu.FindFirstDescendant(cf => cf.ByControlType(ControlType.MenuItem))!;
        Assert.True(printer.Patterns.ExpandCollapse.IsSupported, $"'{printer.Name}' is not a submenu.");
        printer.Patterns.ExpandCollapse.Pattern.Expand();
        return printer;
    }

    /// <summary>The item named <paramref name="name"/> of the expanded submenu (in the item or a popup window of the app).</summary>
    private static async Task<AutomationElement> SubmenuItemAsync(AppInstance app, AutomationElement parent, string name)
    {
        AutomationElement? found = null;
        await Wait.UntilAsync(
            () => (found = parent.FindAllDescendants(cf => cf.ByControlType(ControlType.MenuItem)).FirstOrDefault(item => item.Name == name)
                ?? app.Windows().Select(app.Element).SelectMany(window => window.FindAllDescendants(cf => cf.ByControlType(ControlType.MenuItem)))
                    .FirstOrDefault(item => item.Name == name)) is not null,
            TimeSpan.FromSeconds(10),
            $"the submenu item '{name}'");
        return found!;
    }

    private static nint MenuWindowOf(AppInstance app, AutomationElement item)
    {
        foreach (nint hwnd in app.Windows())
        {
            if (app.Element(hwnd).FindAllDescendants(cf => cf.ByControlType(ControlType.MenuItem)).Any(candidate => candidate.Equals(item)))
            {
                return hwnd;
            }
        }

        return app.Windows()[0];
    }
}
