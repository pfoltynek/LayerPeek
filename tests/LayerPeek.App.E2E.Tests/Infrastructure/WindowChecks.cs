using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace LayerPeek.App.E2E.Tests.Infrastructure;

/// <summary>Assertions about the real windows of the app (Win32 styles, sizes, taskbar properties, UI Automation).</summary>
internal static class WindowChecks
{
    /// <summary>Client size of the settings and diagnostics windows in DIP (T-033, T-041).</summary>
    public const int AuxiliaryWidth = 780;

    public const int AuxiliaryHeight = 508;

    /// <summary>Scaling of the window's monitor (1.0 = 96 DPI).</summary>
    public static double Scaling(nint hwnd) => Native.GetDpiForWindow(hwnd) / 96.0;

    /// <summary>Client size in DIP, rounded.</summary>
    public static (int Width, int Height) ClientSizeDip(nint hwnd)
    {
        Assert.True(Native.GetClientRect(hwnd, out Native.Rect client));
        double scaling = Scaling(hwnd);
        return ((int)Math.Round(client.Width / scaling), (int)Math.Round(client.Height / scaling));
    }

    /// <summary>Only the close button in the title bar (T-040, T-041): no minimize and no maximize box.</summary>
    public static void AssertCloseButtonOnly(nint hwnd, string context)
    {
        long style = Native.Style(hwnd);
        Assert.True((style & Native.WsSysMenu) != 0, $"{context}: the window has no system menu (close button).");
        Assert.True((style & Native.WsMinimizeBox) == 0, $"{context}: the window has a minimize button.");
        Assert.True((style & Native.WsMaximizeBox) == 0, $"{context}: the window has a maximize button.");
    }

    /// <summary>
    /// The designed client size 780×508 DIP, or (on a smaller work area) the size clamped to the work area (T-033/T-041):
    /// never larger than designed, never larger than the work area.
    /// </summary>
    public static void AssertAuxiliarySize(nint hwnd, string context)
    {
        (int width, int height) = ClientSizeDip(hwnd);
        Native.MonitorInfo monitor = Native.MonitorOf(hwnd);
        double scaling = Scaling(hwnd);
        double workWidth = monitor.Work.Width / scaling;
        double workHeight = monitor.Work.Height / scaling;
        bool fits = workWidth >= AuxiliaryWidth + 40 && workHeight >= AuxiliaryHeight + 80;
        if (fits)
        {
            Assert.True(Math.Abs(width - AuxiliaryWidth) <= 1 && Math.Abs(height - AuxiliaryHeight) <= 1, $"{context}: client {width}×{height} DIP, expected {AuxiliaryWidth}×{AuxiliaryHeight}.");
        }
        else
        {
            Assert.True(width <= AuxiliaryWidth && height <= AuxiliaryHeight, $"{context}: client {width}×{height} DIP exceeds the designed size.");
            Assert.True(Native.GetWindowRect(hwnd, out Native.Rect outer) && outer.Width <= monitor.Work.Width && outer.Height <= monitor.Work.Height, $"{context}: the window is larger than the work area.");
        }
    }

    /// <summary>PreventPinning and the AppUserModelID of the app on the window (T-043), read back from the shell.</summary>
    public static void AssertTaskbarProperties(nint hwnd, string context)
    {
        Assert.True(TaskbarProperties.PreventPinning(hwnd) == true, $"{context}: PreventPinning is not set.");
        Assert.Equal(AppSwitches.AppUserModelId, TaskbarProperties.AppUserModelId(hwnd));
    }

    /// <summary>The window carries an icon for the title bar and the taskbar (T-025).</summary>
    public static void AssertHasIcon(nint hwnd, string context)
    {
        Assert.True(Native.WindowIcon(hwnd, big: true) != 0, $"{context}: the window has no large icon.");
        Assert.True(Native.WindowIcon(hwnd, big: false) != 0, $"{context}: the window has no small icon.");
    }

    /// <summary>
    /// Nothing in the window can scroll and no scroll bar is visible (T-033, T-040, T-041): every element with the UIA
    /// scroll pattern reports neither vertical nor horizontal scrolling, and no scroll bar is on screen.
    /// </summary>
    public static void AssertNoScrolling(AutomationElement window, string context)
    {
        List<string> problems = ScrollingProblems(window);
        Assert.True(problems.Count == 0, $"{context}: {string.Join("; ", problems)}");
    }

    /// <summary>What scrolls or shows a scroll bar in the window (empty when nothing does).</summary>
    public static List<string> ScrollingProblems(AutomationElement window)
    {
        List<string> problems = [];
        foreach (AutomationElement element in window.FindAllDescendants())
        {
            try
            {
                if (element.Patterns.Scroll.TryGetPattern(out FlaUI.Core.Patterns.IScrollPattern? scroll) && scroll is not null)
                {
                    if (scroll.VerticallyScrollable.ValueOrDefault)
                    {
                        problems.Add($"{Describe(element)} scrolls vertically ({scroll.VerticalViewSize.ValueOrDefault:0.#} % visible)");
                    }

                    if (scroll.HorizontallyScrollable.ValueOrDefault)
                    {
                        problems.Add($"{Describe(element)} scrolls horizontally ({scroll.HorizontalViewSize.ValueOrDefault:0.#} % visible)");
                    }
                }

                if (element.Properties.ControlType.ValueOrDefault == ControlType.ScrollBar && !element.Properties.IsOffscreen.ValueOrDefault)
                {
                    problems.Add($"{Describe(element)}: visible scroll bar");
                }
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or FlaUI.Core.Exceptions.PropertyNotSupportedException)
            {
                // The element went away meanwhile (a list being refreshed); the next check sees the new state.
            }
        }

        return problems;
    }

    public static string Describe(AutomationElement element)
    {
        string id = element.Properties.AutomationId.ValueOrDefault ?? string.Empty;
        string type = element.Properties.ControlType.ValueOrDefault.ToString();
        return id.Length > 0 ? $"{type} '{id}'" : type;
    }
}
