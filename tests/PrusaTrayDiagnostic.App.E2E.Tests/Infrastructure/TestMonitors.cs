using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json.Nodes;

namespace PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;

/// <summary>
/// An active monitor as the test finds it on its own (T-071): GDI name and bounds from <c>EnumDisplayMonitors</c>, the
/// EDID friendly name and the stable <c>monitorDevicePath</c> from <c>QueryDisplayConfig</c>, the effective DPI from
/// <c>GetDpiForMonitor</c>. Bounds and work area in physical pixels.
/// </summary>
internal sealed record TestMonitor(string GdiName, string FriendlyName, string DevicePath, Native.Rect Bounds, Native.Rect Work, uint Dpi)
{
    /// <summary>The primary monitor is the one at 0,0 of the desktop.</summary>
    public bool Primary => Bounds.Left == 0 && Bounds.Top == 0;

    /// <summary>The name the app shows and stores (T-035): the friendly name, or the GDI name when there is none.</summary>
    public string AppName => FriendlyName.Length > 0 ? FriendlyName : GdiName;

    /// <summary>The <c>camera.window.monitor</c> object of <c>settings.json</c> that chooses this monitor.</summary>
    public JsonObject CameraMonitorJson() => new()
    {
        ["displayName"] = AppName,
        ["width"] = Bounds.Width,
        ["height"] = Bounds.Height,
        ["devicePath"] = DevicePath,
    };

    public override string ToString() => $"{AppName} {GdiName} {Bounds} work {Work} {Dpi} dpi{(Primary ? " primary" : string.Empty)}";
}

/// <summary>The monitors of this machine and the monitor choices the camera window settings should offer for them.</summary>
[SupportedOSPlatform("windows")]
internal static class TestMonitors
{
    /// <summary>The active monitors, ordered like the app numbers them (left to right, then top to bottom).</summary>
    public static List<TestMonitor> All()
    {
        Dictionary<string, DisplayConfig.Monitor> paths = DisplayConfig.Monitors(DisplayConfig.Query())
            .GroupBy(static monitor => monitor.GdiName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
        List<TestMonitor> monitors = [];
        foreach ((string device, nint handle, Native.Rect bounds) in DisplayConfig.DisplayMonitors())
        {
            Native.MonitorInfo info = new() { Size = Marshal.SizeOf<Native.MonitorInfo>() };
            Native.Rect work = Native.GetMonitorInfo(handle, ref info) ? info.Work : bounds;
            paths.TryGetValue(device, out DisplayConfig.Monitor? path);
            monitors.Add(new TestMonitor(device, path?.Name ?? string.Empty, path?.DevicePath ?? string.Empty, bounds, work, DisplayConfig.EffectiveDpi(device) ?? 96));
        }

        return Ordered(monitors);
    }

    /// <summary>The monitor a camera test chooses: the first non-primary one, or the primary one on a single monitor.</summary>
    public static TestMonitor ChosenForCamera(IReadOnlyList<TestMonitor> monitors) =>
        monitors.FirstOrDefault(static monitor => !monitor.Primary) ?? Primary(monitors);

    public static TestMonitor Primary(IReadOnlyList<TestMonitor> monitors) =>
        monitors.FirstOrDefault(static monitor => monitor.Primary) ?? throw new InvalidOperationException("No primary monitor (none at 0,0).");

    /// <summary>The monitor <paramref name="hwnd"/> is on (most of it), from a fresh list.</summary>
    public static TestMonitor Of(nint hwnd)
    {
        Native.Rect bounds = Native.MonitorOf(hwnd).Monitor;
        return All().FirstOrDefault(monitor => monitor.Bounds.Equals(bounds))
            ?? throw new InvalidOperationException($"The window's monitor {bounds} is not among the active monitors.");
    }

    /// <summary>Left to right, then top to bottom (<c>AvaloniaScreenCatalog</c> of the app).</summary>
    public static List<TestMonitor> Ordered(IEnumerable<TestMonitor> monitors) =>
        [.. monitors.OrderBy(static monitor => monitor.Bounds.Left).ThenBy(static monitor => monitor.Bounds.Top)];

    /// <summary>
    /// The items the monitor choice of the camera window settings shows for <paramref name="monitors"/> when the saved
    /// monitor is connected (or the primary one is saved): "Primary monitor", then every monitor numbered from left to
    /// right with its name and resolution (<c>PrinterEditorViewModel.CreateMonitorOptions</c>, texts from the resx).
    /// </summary>
    public static List<string> ExpectedChoices(string language, IReadOnlyList<TestMonitor> monitors)
    {
        List<string> choices = [AppStrings.Get(language, "SettingsCameraWindowMonitorPrimary")];
        List<TestMonitor> ordered = Ordered(monitors);
        for (int i = 0; i < ordered.Count; i++)
        {
            TestMonitor monitor = ordered[i];
            choices.Add(IsFriendlyName(monitor.AppName)
                ? AppStrings.Format(language, "SettingsCameraWindowMonitorNumbered", i + 1, monitor.AppName, monitor.Bounds.Width, monitor.Bounds.Height)
                : AppStrings.Format(language, "SettingsCameraWindowMonitorNumberedUnnamed", i + 1, monitor.Bounds.Width, monitor.Bounds.Height));
        }

        return choices;
    }

    /// <summary>The item of a saved monitor that is not connected ("… – not connected").</summary>
    public static string NotConnectedChoice(string language, string name, int width, int height) =>
        AppStrings.Format(language, "SettingsCameraWindowMonitorNotConnected", IsFriendlyName(name) ? name : AppStrings.Get(language, "SettingsCameraWindowMonitorGeneric"), width, height);

    /// <summary>A name people recognise, not the GDI device name <c>\\.\DISPLAY1</c> (the app's rule).</summary>
    private static bool IsFriendlyName(string name) => !name.StartsWith(@"\\.\", StringComparison.Ordinal);
}
