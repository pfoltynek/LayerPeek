using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

using LayerPeek.App.E2E.Tests.Infrastructure;
using LayerPeek.Simulator.Camera;

namespace LayerPeek.App.E2E.Tests;

/// <summary>
/// The real tray menu (T-042) from the Windows notification area: the test instance's icon (tooltip prefix <c>[E2E]</c>,
/// often in the overflow flyout) is right-clicked with the real mouse (the only step that moves it), and the menu, an
/// Avalonia popup of the app, is read and used through UI Automation: printer submenu, "Show camera" / "Hide camera"
/// (a plain item without a check mark, also after the camera window is closed with its close button, T-042 point 7),
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
        (TrayMenuWindow menu, AutomationElement[] submenu, nint submenuWindow) = await OpenPrinterSubmenuAsync(app, "tray-menu-1");
        AutomationElement cameraItem = CameraItem(submenu, "Show camera");
        string[] submenuItems = [.. submenu.Select(static item => item.Name)];
        Assert.Equal(["Open PrusaLink", "Show camera"], submenuItems);
        Assert.DoesNotContain("Diagnostics…", submenuItems);
        Screenshots.Save(submenuWindow, "tray-menu-2-printer-submenu");
        cameraItem.Patterns.Invoke.Pattern.Invoke();
        nint opened = await app.WaitForWindowAsync(AppWindowKind.Camera, [cameraWindow]);

        // Close: "Hide camera" now (no check mark either), the window closes.
        (menu, submenu, _) = await OpenPrinterSubmenuAsync(app, "tray-menu-3");
        // T-102: the open window also has "Bring camera to front", never a second "Show camera" (2026-10-31).
        Assert.Equal(["Open PrusaLink", "Bring camera to front", "Hide camera"], submenu.Select(static item => item.Name).ToArray());
        cameraItem = CameraItem(submenu, "Hide camera");
        cameraItem.Patterns.Invoke.Pattern.Invoke();
        await Wait.UntilAsync(() => !Native.IsWindowVisible(opened), TimeSpan.FromSeconds(10), "the camera window to close from the menu");

        // Closed with its own close button (WM_CLOSE through the window pattern): the menu says "Show camera" again.
        (menu, submenu, _) = await OpenPrinterSubmenuAsync(app, "tray-menu-4");
        CameraItem(submenu, "Show camera").Patterns.Invoke.Pattern.Invoke();
        nint reopened = await app.WaitForWindowAsync(AppWindowKind.Camera, [cameraWindow, opened]);
        await app.CloseWindowAsync(reopened);
        (menu, submenu, submenuWindow) = await OpenPrinterSubmenuAsync(app, "tray-menu-5");
        CameraItem(submenu, "Show camera");
        Screenshots.Save(submenuWindow, "tray-menu-6-after-close-button");
        await TrayMenu.CloseAsync(menu);

        // Diagnostics… of the main menu (2026-10-21, enabled with a printer) opens the diagnostics window.
        menu = await OpenMenuAsync(app);
        AutomationElement diagnosticsItem = menu.Item("Diagnostics…");
        Assert.True(diagnosticsItem.IsEnabled);
        diagnosticsItem.Patterns.Invoke.Pattern.Invoke();
        await app.WaitForWindowAsync(AppWindowKind.Diagnostics);

        // Settings… from the menu opens the settings window.
        menu = await OpenMenuAsync(app);
        menu.Item("Settings…").Patterns.Invoke.Pattern.Invoke();
        await app.WaitForWindowAsync(AppWindowKind.Settings);
    }

    /// <summary>The camera item of a submenu: a plain item (no toggle state "on"), named <paramref name="name"/>.</summary>
    private static AutomationElement CameraItem(AutomationElement[] submenu, string name)
    {
        AutomationElement item = Assert.Single(submenu, candidate => candidate.Name is "Show camera" or "Hide camera");
        Assert.Equal(name, item.Name);
        Assert.Equal("C", TrayMenu.AccessKeyOf(item));
        Assert.True(
            !item.Patterns.Toggle.IsSupported || item.Patterns.Toggle.Pattern.ToggleState.ValueOrDefault != ToggleState.On,
            $"'{name}' shows a check mark (toggle state on).");
        return item;
    }

    /// <summary>Right-clicks the test instance's tray icon and returns the menu (checking its top level).</summary>
    private static async Task<TrayMenuWindow> OpenMenuAsync(AppInstance app, string? screenshot = null)
    {
        TrayMenuWindow menu = await TrayMenu.OpenWithMouseAsync(app);
        AutomationElement[] items = menu.Items();
        string[] names = [.. items.Select(static item => item.Name)];
        Assert.True(names.Length >= 5, $"Menu items: {string.Join(" | ", names)}");
        Assert.StartsWith("Core One", names[0], StringComparison.Ordinal);
        Assert.Equal(["Diagnostics…", "Settings…", "About", "Quit"], names[^4..]);
        Assert.Equal(["D", "S", "A", "Q"], items[^4..].Select(TrayMenu.AccessKeyOf).ToArray());
        if (screenshot is not null)
        {
            Screenshots.Save(menu.Handle, screenshot);
        }

        return menu;
    }

    private static async Task<(TrayMenuWindow Menu, AutomationElement[] Submenu, nint SubmenuWindow)> OpenPrinterSubmenuAsync(AppInstance app, string screenshot)
    {
        TrayMenuWindow menu = await OpenMenuAsync(app, screenshot);
        (AutomationElement[] items, nint window) = await TrayMenu.ExpandAsync(app, menu, menu.Items()[0]);
        return (menu, items, window);
    }
}
