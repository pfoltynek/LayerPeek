using System.Text.Json;

using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

using LayerPeek.App.E2E.Tests.Infrastructure;

namespace LayerPeek.App.E2E.Tests;

/// <summary>
/// The settings and diagnostics windows of the real app remember their size and maximized state across a restart
/// (T-094): settings maximized through UI Automation (<c>WindowPattern</c>) and closed with Cancel, diagnostics resized
/// through UI Automation (<c>TransformPattern</c>); after quitting and starting the app again with the same data folder,
/// settings open maximized (restoring gives the default size) and diagnostics at the size they were given. Only what the
/// headless tests cannot show: the real window frame, the real maximize and <c>windows.json</c> across processes.
/// </summary>
public sealed class AuxiliaryWindowSizeTests(E2EFixture fixture)
{
    private static readonly TimeSpan StateTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Smaller than the default (fits every screen), larger than the minimum 640 × 400.</summary>
    private const int ResizedWidth = 700;

    private const int ResizedHeight = 450;

    [Fact]
    public async Task MaximizedSettingsAndResizedDiagnosticsSurviveARestart()
    {
        E2EFixture.Require();
        await using AppInstance app = await AppInstance.StartAsync(fixture, "window-size-restart", TestSettings.Json("en", []));

        // Settings: maximized like the title bar button does it, then closed with Cancel (window state is still remembered).
        nint settings = await app.OpenAsync("settings", AppWindowKind.Settings);
        WindowChecks.AssertCloseAndMaximizeButtons(settings, "settings");
        WindowChecks.AssertAuxiliarySize(settings, "settings at first");
        AutomationElement settingsElement = app.Element(settings);
        FlaUI.Core.Patterns.IWindowPattern settingsWindow = settingsElement.Patterns.Window.Pattern;
        Assert.True(settingsWindow.CanMaximize.Value, "UI Automation reports no maximize for the settings window.");
        settingsWindow.SetWindowVisualState(WindowVisualState.Maximized);
        await Wait.UntilAsync(() => Native.IsZoomed(settings), StateTimeout, "the settings window to maximize");
        Screenshots.Save(settings, "window-size-1-settings-maximized");
        AssertWorkAreaFilled(settings, "maximized settings");
        WindowChecks.AssertNoScrolling(settingsElement, "maximized settings");
        await Ui.InvokeAsync(settingsElement, "SettingsCancel");
        await Wait.UntilAsync(() => !Native.IsWindowVisible(settings), StateTimeout, "the settings to close");

        // Diagnostics: resized (outside the size/move loop, like a window manager or UI Automation does it), then closed.
        nint diagnostics = await app.OpenAsync("diagnostics", AppWindowKind.Diagnostics);
        WindowChecks.AssertCloseAndMaximizeButtons(diagnostics, "diagnostics");
        AutomationElement diagnosticsElement = app.Element(diagnostics);
        ResizeClient(diagnosticsElement, diagnostics, ResizedWidth, ResizedHeight);
        await Wait.UntilAsync(() => IsClientSize(diagnostics, ResizedWidth, ResizedHeight), StateTimeout, $"the diagnostics client to become {ResizedWidth}×{ResizedHeight} DIP");
        await app.CloseWindowAsync(diagnostics);

        await app.RestartAsync();
        AssertRemembered(app.DataDirectory);

        // Settings open maximized again; restoring returns to the default size, fitted to the work area and centred.
        nint settingsAgain = await app.OpenAsync("settings", AppWindowKind.Settings);
        await Wait.UntilAsync(() => Native.IsZoomed(settingsAgain), StateTimeout, "the settings window to open maximized after the restart");
        Screenshots.Save(settingsAgain, "window-size-2-settings-maximized-after-restart");
        AutomationElement settingsAgainElement = app.Element(settingsAgain);
        Assert.Equal(WindowVisualState.Maximized, settingsAgainElement.Patterns.Window.Pattern.WindowVisualState.Value);
        settingsAgainElement.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Normal);
        await Wait.UntilAsync(() => !Native.IsZoomed(settingsAgain), StateTimeout, "the settings window to restore");
        WindowChecks.AssertAuxiliarySize(settingsAgain, "settings restored after the restart");
        AssertCentredOnItsMonitor(settingsAgain, "settings restored after the restart");
        await app.CloseWindowAsync(settingsAgain);

        // Diagnostics open at the size they were given, not maximized.
        nint diagnosticsAgain = await app.OpenAsync("diagnostics", AppWindowKind.Diagnostics);
        Screenshots.Save(diagnosticsAgain, "window-size-3-diagnostics-after-restart");
        Assert.False(Native.IsZoomed(diagnosticsAgain), "The diagnostics window opened maximized.");
        Assert.True(IsClientSize(diagnosticsAgain, ResizedWidth, ResizedHeight), $"The diagnostics client is {WindowChecks.ClientSizeDip(diagnosticsAgain)} DIP, expected {ResizedWidth}×{ResizedHeight}.");
        AssertCentredOnItsMonitor(diagnosticsAgain, "diagnostics after the restart");
        await app.CloseWindowAsync(diagnosticsAgain);

        // Restored and closed: no longer maximized next time.
        await app.RestartAsync();
        nint settingsThird = await app.OpenAsync("settings", AppWindowKind.Settings);
        Assert.False(Native.IsZoomed(settingsThird), "The settings window opened maximized although it was restored before closing.");
        WindowChecks.AssertAuxiliarySize(settingsThird, "settings after the second restart");
        Assert.DoesNotContain(" ERR ", app.ReadLog(), StringComparison.Ordinal);
    }

    /// <summary>Resizes the whole window (UI Automation <c>TransformPattern</c>) so that its client gets the given DIP size.</summary>
    private static void ResizeClient(AutomationElement element, nint hwnd, int clientWidth, int clientHeight)
    {
        Assert.True(Native.GetWindowRect(hwnd, out Native.Rect outer));
        Assert.True(Native.GetClientRect(hwnd, out Native.Rect client));
        double scaling = WindowChecks.Scaling(hwnd);
        FlaUI.Core.Patterns.ITransformPattern transform = element.Patterns.Transform.Pattern;
        Assert.True(transform.CanResize.Value, "UI Automation cannot resize the window.");
        transform.Resize(
            Math.Round(clientWidth * scaling) + (outer.Width - client.Width),
            Math.Round(clientHeight * scaling) + (outer.Height - client.Height));
    }

    private static bool IsClientSize(nint hwnd, int width, int height)
    {
        (int actualWidth, int actualHeight) = WindowChecks.ClientSizeDip(hwnd);
        return Math.Abs(actualWidth - width) <= 1 && Math.Abs(actualHeight - height) <= 1;
    }

    /// <summary>A maximized window covers the work area of its monitor (its invisible borders may stick out).</summary>
    private static void AssertWorkAreaFilled(nint hwnd, string context)
    {
        Native.MonitorInfo monitor = Native.MonitorOf(hwnd);
        Assert.True(Native.GetClientRect(hwnd, out Native.Rect client));
        Assert.True(client.Width >= monitor.Work.Width - 32, $"{context}: client {client.Width} px wide on a work area of {monitor.Work.Width} px.");
        Assert.True(client.Height >= monitor.Work.Height - 100, $"{context}: client {client.Height} px high on a work area of {monitor.Work.Height} px.");
    }

    /// <summary>The position is not remembered: the window is centred on the work area of its monitor.</summary>
    private static void AssertCentredOnItsMonitor(nint hwnd, string context)
    {
        Native.MonitorInfo monitor = Native.MonitorOf(hwnd);
        Assert.True(Native.GetWindowRect(hwnd, out Native.Rect outer));
        int centreX = (outer.Left + outer.Right) / 2;
        int workCentreX = (monitor.Work.Left + monitor.Work.Right) / 2;
        Assert.True(Math.Abs(centreX - workCentreX) <= 24, $"{context}: the window centre {centreX} is not at the work area centre {workCentreX}.");
        Assert.True(outer.Top >= monitor.Work.Top - 1 && outer.Bottom <= monitor.Work.Bottom + 16, $"{context}: the window is not on the work area.");
    }

    /// <summary><c>windows.json</c> next to <c>settings.json</c> (T-094): settings maximized, diagnostics with their size.</summary>
    private static void AssertRemembered(string dataDirectory)
    {
        string path = Path.Combine(dataDirectory, "windows.json");
        Assert.True(File.Exists(path), "windows.json was not written.");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        Screenshots.SaveText(File.ReadAllText(path), "window-size-windows-json");
        JsonElement windows = document.RootElement.GetProperty("windows");
        Assert.True(windows.GetProperty("settings").GetProperty("maximized").GetBoolean());
        JsonElement diagnostics = windows.GetProperty("diagnostics");
        Assert.False(diagnostics.GetProperty("maximized").GetBoolean());
        Assert.Equal(ResizedWidth, diagnostics.GetProperty("width").GetDouble(), 1.0);
        Assert.Equal(ResizedHeight, diagnostics.GetProperty("height").GetDouble(), 1.0);
    }
}
