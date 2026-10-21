using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

using PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;
using PrusaTrayDiagnostic.Simulator.Camera;

namespace PrusaTrayDiagnostic.App.E2E.Tests;

/// <summary>
/// The real tray menu (T-042) from the Windows notification area: the test instance's icon (tooltip prefix <c>[E2E]</c>,
/// often in the overflow flyout) is right-clicked with the real mouse (the only step that moves it), and the menu, an
/// Avalonia popup of the app, is read and used through UI Automation: printer submenu, checked Camera toggle, Settings…,
/// About and Quit with their access keys.
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

        // Open: Camera unchecked, the camera window opens.
        AutomationElement printerItem = await OpenPrinterSubmenuAsync(app, "tray-menu-1");
        AutomationElement cameraItem = await SubmenuItemAsync(app, printerItem, "Camera");
        Assert.Equal(ToggleState.Off, ToggleStateOf(cameraItem));
        nint submenu = MenuWindowOf(app, cameraItem);
        // "Open PrusaLink in the browser" is first on screen (see the screenshot) but not reported as a menu item by UI Automation.
        string[] submenuItems = [.. app.Element(submenu).FindAllDescendants(cf => cf.ByControlType(ControlType.MenuItem)).Select(static item => item.Name)];
        Assert.Equal(["Diagnostics…", "Camera"], submenuItems[^2..]);
        Screenshots.Save(submenu, "tray-menu-2-printer-submenu");
        cameraItem.Patterns.Invoke.Pattern.Invoke();
        nint opened = await app.WaitForWindowAsync(AppWindowKind.Camera, [cameraWindow]);

        // Close: Camera checked now, the window closes.
        printerItem = await OpenPrinterSubmenuAsync(app, "tray-menu-3");
        cameraItem = await SubmenuItemAsync(app, printerItem, "Camera");
        Assert.Equal(ToggleState.On, ToggleStateOf(cameraItem));
        cameraItem.Patterns.Invoke.Pattern.Invoke();
        await Wait.UntilAsync(() => !Native.IsWindowVisible(opened), TimeSpan.FromSeconds(10), "the camera window to close from the menu");

        // Settings… from the menu opens the settings window.
        AutomationElement menu = await OpenMenuAsync(app);
        AutomationElement settingsItem = menu.FindAllDescendants(cf => cf.ByControlType(ControlType.MenuItem)).First(item => item.Name == "Settings…");
        settingsItem.Patterns.Invoke.Pattern.Invoke();
        await app.WaitForWindowAsync(AppWindowKind.Settings);
    }

    /// <summary>Right-clicks the test instance's tray icon and returns the menu (checking its top level).</summary>
    private static async Task<AutomationElement> OpenMenuAsync(AppInstance app, string? screenshot = null)
    {
        List<nint> before = app.Windows();
        (IReadOnlyList<AutomationElement> icons, bool openedOverflow) = await NotificationArea.FindTestIconsAsync(app.Automation.GetDesktop());
        AutomationElement? menu = null;
        try
        {
            // Several matches: icons of killed test instances can linger; only the live one opens a menu of this app.
            for (int i = 0; i < icons.Count; i++)
            {
                icons[i].RightClick();
                try
                {
                    await Wait.UntilAsync(
                        () => (menu = app.Windows().Where(hwnd => !before.Contains(hwnd)).Select(app.Element)
                            .FirstOrDefault(window => window.FindFirstDescendant(cf => cf.ByControlType(ControlType.MenuItem)) is not null)) is not null,
                        TimeSpan.FromSeconds(icons.Count == 1 ? 10 : 3),
                        "the tray menu");
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
        Assert.True(names.Length >= 4, $"Menu items: {string.Join(" | ", names)}");
        Assert.StartsWith("Core One", names[0], StringComparison.Ordinal);
        Assert.Equal(["Settings…", "About", "Quit"], names[^3..]);
        Assert.Equal(["S", "A", "Q"], items[^3..].Select(static item => item.Properties.AccessKey.ValueOrDefault ?? string.Empty).Select(static key => key.Replace("Alt+", string.Empty, StringComparison.OrdinalIgnoreCase)).ToArray());
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

    private static ToggleState ToggleStateOf(AutomationElement item) =>
        item.Patterns.Toggle.IsSupported ? item.Patterns.Toggle.Pattern.ToggleState.ValueOrDefault : ToggleState.Indeterminate;

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
