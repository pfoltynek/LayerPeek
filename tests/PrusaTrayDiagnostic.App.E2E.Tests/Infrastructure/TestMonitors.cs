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

    /// <summary>
    /// The <c>camera.window.monitor</c> object of <c>settings.json</c> that chooses this monitor, as the settings store it when
    /// the user picks it among <paramref name="connected"/>: with the paths of the other monitors with the same name
    /// (<c>knownTwinPaths</c>, T-071 Q5, only when there are any). <see langword="null"/>: no twins.
    /// </summary>
    public JsonObject CameraMonitorJson(IReadOnlyList<TestMonitor>? connected = null)
    {
        JsonObject json = new()
        {
            ["displayName"] = AppName,
            ["width"] = Bounds.Width,
            ["height"] = Bounds.Height,
            ["devicePath"] = DevicePath,
        };
        List<string> twins = connected is null ? [] : TestMonitors.KnownTwinPaths(this, connected);
        if (twins.Count > 0)
        {
            json["knownTwinPaths"] = new JsonArray([.. twins.Select(static path => (JsonNode?)JsonValue.Create(path))]);
        }

        return json;
    }

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

    /// <summary>
    /// The <c>knownTwinPaths</c> the settings store with <paramref name="saved"/> when it is picked among
    /// <paramref name="connected"/> (T-071 Q5, <c>CameraMonitorId.FromScreen</c>): the device paths of the other monitors
    /// with the same name, in the app's order, each once; none when <paramref name="saved"/> has no path.
    /// </summary>
    public static List<string> KnownTwinPaths(TestMonitor saved, IReadOnlyList<TestMonitor> connected)
    {
        List<string> twins = [];
        if (saved.DevicePath.Length == 0)
        {
            return twins;
        }

        foreach (TestMonitor monitor in Ordered(connected))
        {
            if (monitor.AppName == saved.AppName
                && monitor.DevicePath.Length > 0
                && !string.Equals(monitor.DevicePath, saved.DevicePath, StringComparison.OrdinalIgnoreCase)
                && !twins.Contains(monitor.DevicePath, StringComparer.OrdinalIgnoreCase))
            {
                twins.Add(monitor.DevicePath);
            }
        }

        return twins;
    }

    /// <summary>
    /// The connected monitor the app uses for the saved <paramref name="saved"/> with <paramref name="knownTwinPaths"/>
    /// (T-035 Q4/Q8, T-071 Q4/Q5, <c>CameraWindowDefaultPlacement.SelectScreen</c>): with a device path the monitor with
    /// that path, else the only monitor with the same name and resolution when it is no known twin (the same monitor on
    /// another connector); without a path (older settings) the same name and resolution, else the same name (first from
    /// the left). <see langword="null"/>: "not connected", the camera on the primary monitor. A switched-off monitor
    /// therefore never matches a twin that was connected when it was chosen.
    /// </summary>
    public static TestMonitor? Matching(TestMonitor saved, IReadOnlyList<string> knownTwinPaths, IReadOnlyList<TestMonitor> connected)
    {
        List<TestMonitor> ordered = Ordered(connected);
        bool SameNameAndResolution(TestMonitor m) => m.AppName == saved.AppName && m.Bounds.Width == saved.Bounds.Width && m.Bounds.Height == saved.Bounds.Height;
        if (saved.DevicePath.Length > 0)
        {
            if (ordered.FirstOrDefault(m => string.Equals(m.DevicePath, saved.DevicePath, StringComparison.OrdinalIgnoreCase)) is { } byPath)
            {
                return byPath;
            }

            List<TestMonitor> candidates = [.. ordered.Where(SameNameAndResolution)];
            return candidates is [{ } only] && !knownTwinPaths.Contains(only.DevicePath, StringComparer.OrdinalIgnoreCase) ? only : null;
        }

        return ordered.FirstOrDefault(SameNameAndResolution) ?? ordered.FirstOrDefault(m => m.AppName == saved.AppName);
    }

    /// <summary>The item of a saved monitor that is not connected ("… – not connected").</summary>
    public static string NotConnectedChoice(string language, string name, int width, int height) =>
        AppStrings.Format(language, "SettingsCameraWindowMonitorNotConnected", IsFriendlyName(name) ? name : AppStrings.Get(language, "SettingsCameraWindowMonitorGeneric"), width, height);

    /// <summary>A name people recognise, not the GDI device name <c>\\.\DISPLAY1</c> (the app's rule).</summary>
    private static bool IsFriendlyName(string name) => !name.StartsWith(@"\\.\", StringComparison.Ordinal);
}
