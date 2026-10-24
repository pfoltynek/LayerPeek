using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace LayerPeek.App.E2E.Tests.Infrastructure;

/// <summary>One active monitor of a topology, in physical pixels; identified by <see cref="DevicePath"/>.</summary>
internal sealed record DisplayMonitorState(string DevicePath, string Name, string GdiName, int Left, int Top, int Width, int Height, int Rotation)
{
    /// <summary>The primary monitor is the one at 0,0.</summary>
    public bool Primary => Left == 0 && Top == 0;

    public override string ToString() =>
        $"{(Name.Length == 0 ? "(no name)" : Name)} {GdiName} {Width}x{Height} at {Left},{Top}{(Rotation is 2 or 4 ? " rotated" : string.Empty)}{(Primary ? " primary" : string.Empty)}";
}

/// <summary>
/// The active display topology: the paths and modes of <c>QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS)</c> exactly as
/// returned (base64 of the structures, for <c>SetDisplayConfig</c>) and the readable monitors (the i-th monitor is the
/// i-th path).
/// </summary>
internal sealed record DisplayTopologyState(string Paths, string Modes, IReadOnlyList<DisplayMonitorState> Monitors)
{
    public override string ToString() => string.Join("; ", Monitors);
}

/// <summary>Pure parts of the topology change (unit-tested).</summary>
internal static class DisplayTopology
{
    public static string Encode<T>(T[] items)
        where T : unmanaged =>
        Convert.ToBase64String(MemoryMarshal.AsBytes(items.AsSpan()));

    public static unsafe T[] Decode<T>(string base64)
        where T : unmanaged
    {
        byte[] bytes = Convert.FromBase64String(base64);
        if (bytes.Length % sizeof(T) != 0)
        {
            throw new InvalidOperationException($"{bytes.Length} bytes are not a whole number of {typeof(T).Name} ({sizeof(T)} bytes).");
        }

        return MemoryMarshal.Cast<byte, T>(bytes).ToArray();
    }

    /// <summary>
    /// The configuration without the path <paramref name="index"/>: the modes only the remaining paths reference, in
    /// the order of their first reference, with the indices of the paths renumbered.
    /// </summary>
    public static (DisplayConfig.PathInfo[] Paths, DisplayConfig.ModeInfo[] Modes) WithoutPath(DisplayConfig.PathInfo[] paths, DisplayConfig.ModeInfo[] modes, int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, paths.Length);
        List<DisplayConfig.PathInfo> kept = [];
        List<DisplayConfig.ModeInfo> keptModes = [];
        Dictionary<uint, uint> renumbered = [];
        for (int i = 0; i < paths.Length; i++)
        {
            if (i == index)
            {
                continue;
            }

            DisplayConfig.PathInfo path = paths[i];
            path.SourceInfo.ModeInfoIdx = Keep(path.SourceInfo.ModeInfoIdx);
            path.TargetInfo.ModeInfoIdx = Keep(path.TargetInfo.ModeInfoIdx);
            kept.Add(path);
        }

        return ([.. kept], [.. keptModes]);

        uint Keep(uint mode)
        {
            if (mode == DisplayConfig.ModeIndexInvalid || mode >= modes.Length)
            {
                return DisplayConfig.ModeIndexInvalid;
            }

            if (!renumbered.TryGetValue(mode, out uint now))
            {
                now = (uint)keptModes.Count;
                keptModes.Add(modes[mode]);
                renumbered[mode] = now;
            }

            return now;
        }
    }

    /// <summary>How <paramref name="actual"/> differs from <paramref name="expected"/> (count, position, size, rotation, primary).</summary>
    public static List<string> Differences(IReadOnlyList<DisplayMonitorState> expected, IReadOnlyList<DisplayMonitorState> actual)
    {
        List<string> differences = [];
        foreach (DisplayMonitorState wanted in expected)
        {
            DisplayMonitorState? now = actual.FirstOrDefault(m => string.Equals(m.DevicePath, wanted.DevicePath, StringComparison.OrdinalIgnoreCase));
            if (now is null)
            {
                differences.Add($"{wanted} is not active.");
            }
            else if (now.Left != wanted.Left || now.Top != wanted.Top || now.Width != wanted.Width || now.Height != wanted.Height || now.Rotation != wanted.Rotation)
            {
                differences.Add($"{wanted.Name} {wanted.DevicePath}: expected {wanted}, now {now}.");
            }
        }

        foreach (DisplayMonitorState extra in actual.Where(m => !expected.Any(e => string.Equals(e.DevicePath, m.DevicePath, StringComparison.OrdinalIgnoreCase))))
        {
            differences.Add($"{extra} is active but should not be.");
        }

        return differences;
    }
}

/// <summary>
/// Switching a non-primary monitor off as if it were unplugged (T-066): <c>SetDisplayConfig</c> with the active paths
/// without the monitor's path, <c>SDC_APPLY | SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_ALLOW_CHANGES</c> and never
/// <c>SDC_SAVE_TO_DATABASE</c> (after a restart Windows use the stored configuration with every monitor). The restore
/// applies the recorded paths and modes; when that fails (e.g. new adapter LUIDs after a restart) it falls back to
/// <c>SDC_TOPOLOGY_EXTEND</c> and then <c>SDC_USE_DATABASE_CURRENT</c>, and checks count, resolution, position,
/// rotation and the primary monitor. The primary monitor is never switched off.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class MonitorDisconnectKind() : SystemChangeKind<DisplayTopologyState>("MonitorDisconnect", SystemChangeTraits.MonitorOff, SystemChangeJson.Default.DisplayTopologyState)
{
    /// <summary>How long a topology change may take.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>Flags of applying supplied paths and modes (never <c>SDC_SAVE_TO_DATABASE</c>).</summary>
    internal const uint ApplySupplied = DisplayConfig.SdcApply | DisplayConfig.SdcUseSuppliedDisplayConfig | DisplayConfig.SdcAllowChanges;

    /// <summary>Flags of the restore fallbacks from the database, in order (never <c>SDC_SAVE_TO_DATABASE</c>).</summary>
    internal static readonly (string Name, uint Flags)[] RestoreFallbacks =
    [
        ("SDC_TOPOLOGY_EXTEND", DisplayConfig.SdcApply | DisplayConfig.SdcTopologyExtend),
        ("SDC_USE_DATABASE_CURRENT", DisplayConfig.SdcApply | DisplayConfig.SdcUseDatabaseCurrent),
    ];

    /// <summary>Before every other kind: the scale of a switched-off monitor can only go back once it is on again.</summary>
    public override int RestoreOrder => -10;

    public override Task<DisplayTopologyState> ReadStateAsync(string? key, CancellationToken cancellationToken) => Task.FromResult(Read());

    public override bool Matches(DisplayTopologyState original, DisplayTopologyState current) =>
        DisplayTopology.Differences(original.Monitors, current.Monitors).Count == 0;

    public override string Describe(string? key, DisplayTopologyState original) =>
        $"Display topology: {original}.{Environment.NewLine}"
        + "By hand: Win+P > Extend; then Settings > System > Display: the arrangement, resolution, orientation and the main display as listed.";

    /// <summary>
    /// <paramref name="state"/> without the monitor <paramref name="devicePath"/>, for <see cref="SystemChange{TState}.ApplyAsync"/>;
    /// the primary monitor is refused.
    /// </summary>
    public static DisplayTopologyState WithoutMonitor(DisplayTopologyState state, string devicePath)
    {
        int index = state.Monitors.ToList().FindIndex(m => string.Equals(m.DevicePath, devicePath, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            throw new InvalidOperationException($"No active monitor '{devicePath}'.");
        }

        if (state.Monitors[index].Primary)
        {
            throw new InvalidOperationException($"{state.Monitors[index]} is the primary monitor; it is never switched off.");
        }

        (DisplayConfig.PathInfo[] paths, DisplayConfig.ModeInfo[] modes) = DisplayTopology.WithoutPath(
            DisplayTopology.Decode<DisplayConfig.PathInfo>(state.Paths),
            DisplayTopology.Decode<DisplayConfig.ModeInfo>(state.Modes),
            index);
        return new DisplayTopologyState(DisplayTopology.Encode(paths), DisplayTopology.Encode(modes), [.. state.Monitors.Where((_, i) => i != index)]);
    }

    /// <summary>Applies the supplied paths and modes (never saved to the database) and waits until they are active.</summary>
    protected override async Task ApplyCoreAsync(string? key, DisplayTopologyState state, CancellationToken cancellationToken)
    {
        DisplayTopologyState current = Read();
        if (Matches(state, current))
        {
            return;
        }

        DisplayConfig.PathInfo[] paths = DisplayTopology.Decode<DisplayConfig.PathInfo>(state.Paths);
        DisplayConfig.ModeInfo[] modes = DisplayTopology.Decode<DisplayConfig.ModeInfo>(state.Modes);
        // Checked on the structures that are applied, not on the readable list next to them.
        List<DisplayConfig.Monitor> applied = DisplayConfig.Monitors(new DisplayConfig.Configuration(paths, modes));
        foreach (DisplayMonitorState primary in current.Monitors.Where(static m => m.Primary))
        {
            if (!applied.Any(m => m.Primary && string.Equals(m.DevicePath, primary.DevicePath, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Refusing a topology without the primary monitor {primary} at 0,0.");
            }
        }

        int result = DisplayConfig.Set(paths, modes, ApplySupplied);
        if (result != 0)
        {
            throw new InvalidOperationException($"SetDisplayConfig failed with Win32 error {result}.");
        }

        if (!await WaitForAsync(state, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException($"The topology did not change within {Timeout.TotalSeconds:0} s: {string.Join(" ", DisplayTopology.Differences(state.Monitors, Read().Monitors))}");
        }
    }

    /// <summary>The recorded paths and modes; fallbacks <c>SDC_TOPOLOGY_EXTEND</c> and <c>SDC_USE_DATABASE_CURRENT</c>.</summary>
    protected override async Task RestoreCoreAsync(string? key, DisplayTopologyState original, CancellationToken cancellationToken)
    {
        if (Matches(original, Read()))
        {
            return;
        }

        List<string> attempts = [];
        (string Name, Func<int> Apply)[] ways =
        [
            ("recorded paths and modes", () => DisplayConfig.Set(DisplayTopology.Decode<DisplayConfig.PathInfo>(original.Paths), DisplayTopology.Decode<DisplayConfig.ModeInfo>(original.Modes), ApplySupplied)),
            .. RestoreFallbacks.Select(static fallback => (fallback.Name, (Func<int>)(() => DisplayConfig.SetTopology(fallback.Flags)))),
        ];
        foreach ((string name, Func<int> apply) in ways)
        {
            int result = apply();
            if (result == 0 && await WaitForAsync(original, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            attempts.Add(result == 0 ? $"{name}: applied, but {string.Join(" ", DisplayTopology.Differences(original.Monitors, Read().Monitors))}" : $"{name}: Win32 error {result}");
        }

        throw new InvalidOperationException($"The display topology could not be restored ({string.Join("; ", attempts)}).");
    }

    private static DisplayTopologyState Read()
    {
        DisplayConfig.Configuration configuration = DisplayConfig.Query();
        List<DisplayMonitorState> monitors = [.. DisplayConfig.Monitors(configuration).Select(static m =>
            new DisplayMonitorState(m.DevicePath, m.Name, m.GdiName, m.Left, m.Top, m.Width, m.Height, m.Rotation))];
        return new DisplayTopologyState(DisplayTopology.Encode(configuration.Paths), DisplayTopology.Encode(configuration.Modes), monitors);
    }

    private async Task<bool> WaitForAsync(DisplayTopologyState state, CancellationToken cancellationToken)
    {
        try
        {
            await Wait.UntilAsync(
                () =>
                {
                    try
                    {
                        return Matches(state, Read());
                    }
                    catch (InvalidOperationException)
                    {
                        // The configuration is changing right now.
                        return false;
                    }
                },
                Timeout,
                "the display topology").ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return false;
        }
    }
}

/// <summary>
/// A visible window of another process before a monitor went off (only checked afterwards, never moved). Only the process
/// name and the rectangle reach the results, never the title of another app's window (user decision 2026-10-22).
/// </summary>
internal sealed record OtherAppWindow(long Handle, string Process, int Left, int Top, int Right, int Bottom)
{
    public override string ToString() => $"{Process} ({Left},{Top})-({Right},{Bottom})";
}

/// <summary>A monitor that is off now and the windows of other apps that were on it.</summary>
internal sealed record MonitorOff(DisplayMonitorState Monitor, IReadOnlyList<OtherAppWindow> Windows);

/// <summary>Visible windows of other processes; Windows moves them when a monitor goes off and back when it returns.</summary>
[SupportedOSPlatform("windows")]
internal static partial class OtherAppWindows
{
    /// <summary>Tolerance of the position check (DPI rounding).</summary>
    public const int Tolerance = 8;

    /// <summary>
    /// Visible, not minimized, not cloaked top-level windows of other processes whose centre is on
    /// <paramref name="monitor"/> (physical pixels; the process is per-monitor aware).
    /// </summary>
    public static List<OtherAppWindow> On(DisplayMonitorState monitor)
    {
        List<OtherAppWindow> windows = [];
        Native.EnumWindows(
            (hwnd, _) =>
            {
                Native.GetWindowThreadProcessId(hwnd, out uint processId);
                if (processId == (uint)Environment.ProcessId || !Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd) || IsCloaked(hwnd)
                    || !Native.GetWindowRect(hwnd, out Native.Rect rect) || rect.Width <= 0 || rect.Height <= 0)
                {
                    return true;
                }

                int x = rect.Left + (rect.Width / 2);
                int y = rect.Top + (rect.Height / 2);
                if (x >= monitor.Left && x < monitor.Left + monitor.Width && y >= monitor.Top && y < monitor.Top + monitor.Height)
                {
                    windows.Add(new OtherAppWindow(hwnd, ProcessName((int)processId), rect.Left, rect.Top, rect.Right, rect.Bottom));
                }

                return true;
            },
            0);
        return windows;
    }

    /// <summary>The window's rectangle now, or <see langword="null"/> when it is gone, hidden or minimized (not checked).</summary>
    public static Native.Rect? Now(long handle)
    {
        nint hwnd = (nint)handle;
        return IsWindow(hwnd) && Native.IsWindowVisible(hwnd) && !Native.IsIconic(hwnd) && Native.GetWindowRect(hwnd, out Native.Rect rect) ? rect : null;
    }

    /// <summary>
    /// The windows of <paramref name="before"/> that still exist but are not back at their rectangle (within
    /// <paramref name="tolerance"/> on every edge); closed, hidden or minimized ones are not checked.
    /// </summary>
    public static List<string> NotReturned(IReadOnlyList<OtherAppWindow> before, Func<long, Native.Rect?> now, int tolerance = Tolerance)
    {
        List<string> missing = [];
        foreach (OtherAppWindow window in before)
        {
            if (now(window.Handle) is not { } rect)
            {
                continue;
            }

            if (Math.Abs(rect.Left - window.Left) > tolerance || Math.Abs(rect.Top - window.Top) > tolerance
                || Math.Abs(rect.Right - window.Right) > tolerance || Math.Abs(rect.Bottom - window.Bottom) > tolerance)
            {
                missing.Add($"{window} is now at {rect}");
            }
        }

        return missing;
    }

    private static string ProcessName(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return "?";
        }
        catch (InvalidOperationException)
        {
            return "?";
        }
    }

    private static bool IsCloaked(nint hwnd) => DwmGetWindowAttribute(hwnd, DwmwaCloaked, out int cloaked, sizeof(int)) == 0 && cloaked != 0;

    private const uint DwmwaCloaked = 14;

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmGetWindowAttribute(nint hwnd, uint attribute, out int value, int size);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindow(nint hwnd);
}

/// <summary>Switching a monitor off and back with the checks of T-066.</summary>
[SupportedOSPlatform("windows")]
internal static class MonitorDisconnectChanges
{
    /// <summary>
    /// Switches off the non-primary monitor <paramref name="devicePath"/> (default: the first one) after recording the
    /// windows of other apps on it, and checks within 10 s that <c>EnumDisplayMonitors</c> no longer returns it. Without a
    /// second monitor the test is skipped.
    /// </summary>
    public static async Task<MonitorOff> SwitchOffAsync(this SystemChange<DisplayTopologyState> change, string? devicePath = null, CancellationToken cancellationToken = default)
    {
        DisplayTopologyState current = await change.ReadAsync(cancellationToken);
        DisplayMonitorState? monitor = devicePath is null
            ? current.Monitors.FirstOrDefault(static m => !m.Primary)
            : current.Monitors.FirstOrDefault(m => string.Equals(m.DevicePath, devicePath, StringComparison.OrdinalIgnoreCase));
        if (monitor is null)
        {
            Assert.Skip(devicePath is null ? "Needs a second (not primary) monitor." : $"The monitor '{devicePath}' is not active.");
            throw new InvalidOperationException("Unreachable.");
        }

        List<OtherAppWindow> windows = OtherAppWindows.On(monitor);
        int before = DisplayConfig.DisplayMonitors().Count;
        await change.ApplyAsync(MonitorDisconnectKind.WithoutMonitor(current, monitor.DevicePath), cancellationToken);
        await Wait.UntilAsync(
            () =>
            {
                List<(string Device, nint Handle, Native.Rect Bounds)> now = DisplayConfig.DisplayMonitors();
                return now.Count == before - 1 && !now.Any(m => string.Equals(m.Device, monitor.GdiName, StringComparison.OrdinalIgnoreCase));
            },
            MonitorDisconnectKind.Timeout,
            $"EnumDisplayMonitors to stop returning {monitor}");
        return new MonitorOff(monitor, windows);
    }

    /// <summary>
    /// Brings the topology back (keeping the change open), checks it and waits up to 10 s for the windows of other apps
    /// to be back where they were; returns those that are not (Windows moves them back, the test never does).
    /// </summary>
    public static async Task<List<string>> RestoreAndCheckWindowsAsync(this SystemChange<DisplayTopologyState> change, MonitorOff off, CancellationToken cancellationToken = default)
    {
        await change.RestoreAsync(cancellationToken);
        List<string> missing = [];
        try
        {
            await Wait.UntilAsync(() => (missing = OtherAppWindows.NotReturned(off.Windows, OtherAppWindows.Now)).Count == 0, MonitorDisconnectKind.Timeout, "the windows of other apps to come back");
        }
        catch (TimeoutException)
        {
            // Reported by the caller.
        }

        return missing;
    }

    public static string Describe(MonitorOff off)
    {
        StringBuilder text = new();
        text.AppendLine(CultureInfo.InvariantCulture, $"Switched off: {off.Monitor}");
        text.AppendLine(CultureInfo.InvariantCulture, $"Windows of other apps on it before ({off.Windows.Count}):");
        foreach (OtherAppWindow window in off.Windows)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"  {window}");
        }

        return text.ToString();
    }
}
