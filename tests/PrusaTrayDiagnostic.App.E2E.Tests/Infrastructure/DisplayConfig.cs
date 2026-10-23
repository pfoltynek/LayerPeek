using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

using Microsoft.Win32;

namespace PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;

/// <summary>
/// The Connecting and Configuring Displays (CCD) API of Windows for the display changes of T-066
/// (https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-querydisplayconfig): the active paths and
/// modes, monitor names, and the <b>undocumented</b> DPI scale packets −3 (read) and −4 (set) of
/// <c>DisplayConfigGetDeviceInfo</c> / <c>DisplayConfigSetDeviceInfo</c> (accepted by the user 2026-10-22; information
/// from https://github.com/lihas/windows-DPI-scaling-sample, no code copied). The CCD data is in physical pixels whatever
/// the DPI awareness of the process; <see cref="EffectiveDpi"/> needs a per-monitor aware process (the fixture sets it).
/// </summary>
[SupportedOSPlatform("windows")]
internal static unsafe partial class DisplayConfig
{
    public const uint QdcAllPaths = 0x1;
    public const uint QdcOnlyActivePaths = 0x2;

    public const uint SdcTopologyExtend = 0x4;
    public const uint SdcUseDatabaseCurrent = 0xF;
    public const uint SdcUseSuppliedDisplayConfig = 0x20;
    public const uint SdcValidate = 0x40;
    public const uint SdcApply = 0x80;
    public const uint SdcAllowChanges = 0x400;

    // SDC_SAVE_TO_DATABASE (0x200) is deliberately not defined: the tests never persist a topology (checked by a test).

    /// <summary>DISPLAYCONFIG_PATH_MODE_IDX_INVALID.</summary>
    public const uint ModeIndexInvalid = 0xFFFF_FFFF;

    public const int ModeInfoTypeSource = 1;
    public const int ModeInfoTypeTarget = 2;

    private const int ErrorSuccess = 0;
    private const int ErrorInsufficientBuffer = 122;

    private const int DeviceInfoGetSourceName = 1;
    private const int DeviceInfoGetTargetName = 2;

    /// <summary>Undocumented: read the DPI scale of a source (minimum, current, maximum step relative to the recommended one).</summary>
    private const int DeviceInfoGetDpiScale = -3;

    /// <summary>Undocumented: set the DPI scale of a source (step relative to the recommended one).</summary>
    private const int DeviceInfoSetDpiScale = -4;

    /// <summary>MDT_EFFECTIVE_DPI of <c>GetDpiForMonitor</c>.</summary>
    private const int MdtEffectiveDpi = 0;

    /// <summary>PROCESS_QUERY_LIMITED_INFORMATION: enough for <c>GetSystemDpiForProcess</c>, also on Explorer.</summary>
    private const uint ProcessQueryLimitedInformation = 0x1000;

    [StructLayout(LayoutKind.Sequential)]
    public struct Luid : IEquatable<Luid>
    {
        public uint LowPart;
        public int HighPart;

        public readonly bool Equals(Luid other) => LowPart == other.LowPart && HighPart == other.HighPart;

        public override readonly bool Equals(object? obj) => obj is Luid other && Equals(other);

        public override readonly int GetHashCode() => HashCode.Combine(LowPart, HighPart);

        public override readonly string ToString() => $"{HighPart:X8}{LowPart:X8}";
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PathSourceInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rational
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PathTargetInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public int OutputTechnology;
        public int Rotation;
        public int Scaling;
        public Rational RefreshRate;
        public int ScanLineOrdering;
        public int TargetAvailable;
        public uint StatusFlags;
    }

    /// <summary>DISPLAYCONFIG_PATH_INFO (72 bytes).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PathInfo
    {
        public PathSourceInfo SourceInfo;
        public PathTargetInfo TargetInfo;
        public uint Flags;
    }

    /// <summary>DISPLAYCONFIG_MODE_INFO (64 bytes); only the source mode of the union is read, the rest is kept as is.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    public struct ModeInfo
    {
        [FieldOffset(0)]
        public int InfoType;

        [FieldOffset(4)]
        public uint Id;

        [FieldOffset(8)]
        public Luid AdapterId;

        /// <summary>DISPLAYCONFIG_SOURCE_MODE.width (desktop size in physical pixels, already rotated).</summary>
        [FieldOffset(16)]
        public uint SourceWidth;

        [FieldOffset(20)]
        public uint SourceHeight;

        [FieldOffset(24)]
        public int SourcePixelFormat;

        /// <summary>DISPLAYCONFIG_SOURCE_MODE.position: the top left corner on the desktop; the primary monitor is at 0,0.</summary>
        [FieldOffset(28)]
        public int SourceX;

        [FieldOffset(32)]
        public int SourceY;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DeviceInfoHeader
    {
        public int Type;
        public uint Size;
        public Luid AdapterId;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SourceDeviceName
    {
        public DeviceInfoHeader Header;
        public fixed char ViewGdiDeviceName[32];
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TargetDeviceName
    {
        public DeviceInfoHeader Header;
        public uint Flags;
        public int OutputTechnology;
        public ushort EdidManufactureId;
        public ushort EdidProductCodeId;
        public uint ConnectorInstance;
        public fixed char MonitorFriendlyDeviceName[64];
        public fixed char MonitorDevicePath[128];
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DpiScaleGet
    {
        public DeviceInfoHeader Header;
        public int MinimumStep;
        public int CurrentStep;
        public int MaximumStep;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DpiScaleSet
    {
        public DeviceInfoHeader Header;
        public int Step;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MonitorInfoEx
    {
        public int Size;
        public Native.Rect Monitor;
        public Native.Rect Work;
        public uint Flags;
        public fixed char Device[32];
    }

    /// <summary>The active paths and the modes they reference, as <c>QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS)</c> returns them.</summary>
    public sealed record Configuration(PathInfo[] Paths, ModeInfo[] Modes);

    /// <summary>One active monitor (path) in physical pixels.</summary>
    public sealed record Monitor(
        int PathIndex,
        Luid SourceAdapter,
        uint SourceId,
        string DevicePath,
        string Name,
        string GdiName,
        int Left,
        int Top,
        int Width,
        int Height,
        int Rotation)
    {
        /// <summary>The primary monitor is the one at 0,0 of the desktop.</summary>
        public bool Primary => Left == 0 && Top == 0;
    }

    /// <summary>The DPI scale steps of a source as the undocumented packet −3 reports them.</summary>
    public readonly record struct DpiScaleSteps(int MinimumStep, int CurrentStep, int MaximumStep);

    public static Configuration Query(uint flags = QdcOnlyActivePaths)
    {
        for (int attempt = 0; ; attempt++)
        {
            Check(GetDisplayConfigBufferSizes(flags, out uint pathCount, out uint modeCount), "GetDisplayConfigBufferSizes");
            PathInfo[] paths = new PathInfo[pathCount];
            ModeInfo[] modes = new ModeInfo[modeCount];
            int result;
            fixed (PathInfo* pathPointer = paths)
            fixed (ModeInfo* modePointer = modes)
            {
                result = QueryDisplayConfig(flags, ref pathCount, pathPointer, ref modeCount, modePointer, 0);
            }

            if (result == ErrorInsufficientBuffer && attempt < 5)
            {
                // The configuration changed between the two calls.
                continue;
            }

            Check(result, "QueryDisplayConfig");
            return new Configuration(paths[..(int)pathCount], modes[..(int)modeCount]);
        }
    }

    /// <summary>The monitors of the active paths of <paramref name="configuration"/> (one per path, in path order).</summary>
    public static List<Monitor> Monitors(Configuration configuration)
    {
        List<Monitor> monitors = [];
        for (int i = 0; i < configuration.Paths.Length; i++)
        {
            PathInfo path = configuration.Paths[i];
            (string name, string devicePath) = TargetName(path.TargetInfo.AdapterId, path.TargetInfo.Id);
            ModeInfo source = path.SourceInfo.ModeInfoIdx < configuration.Modes.Length
                ? configuration.Modes[path.SourceInfo.ModeInfoIdx]
                : default;
            monitors.Add(new Monitor(
                i,
                path.SourceInfo.AdapterId,
                path.SourceInfo.Id,
                devicePath,
                name,
                SourceGdiName(path.SourceInfo.AdapterId, path.SourceInfo.Id),
                source.SourceX,
                source.SourceY,
                (int)source.SourceWidth,
                (int)source.SourceHeight,
                path.TargetInfo.Rotation));
        }

        return monitors;
    }

    /// <summary>Applies paths and modes (<c>SetDisplayConfig</c>); returns the Win32 error code (0 = success).</summary>
    public static int Set(PathInfo[] paths, ModeInfo[] modes, uint flags)
    {
        fixed (PathInfo* pathPointer = paths)
        fixed (ModeInfo* modePointer = modes)
        {
            return SetDisplayConfig((uint)paths.Length, paths.Length == 0 ? null : pathPointer, (uint)modes.Length, modes.Length == 0 ? null : modePointer, flags);
        }
    }

    /// <summary>A topology from the database (<c>SetDisplayConfig</c> without paths); returns the Win32 error code.</summary>
    public static int SetTopology(uint flags) => SetDisplayConfig(0, null, 0, null, flags);

    public static DpiScaleSteps GetDpiScale(Luid adapter, uint sourceId)
    {
        DpiScaleGet packet = new() { Header = Header(DeviceInfoGetDpiScale, sizeof(DpiScaleGet), adapter, sourceId) };
        Check(DisplayConfigGetDeviceInfo(&packet.Header), "DisplayConfigGetDeviceInfo(DPI scale)");
        return new DpiScaleSteps(packet.MinimumStep, packet.CurrentStep, packet.MaximumStep);
    }

    /// <summary>Sets the DPI scale step of a source; returns the Win32 error code (0 = success).</summary>
    public static int SetDpiScale(Luid adapter, uint sourceId, int step)
    {
        DpiScaleSet packet = new() { Header = Header(DeviceInfoSetDpiScale, sizeof(DpiScaleSet), adapter, sourceId), Step = step };
        return DisplayConfigSetDeviceInfo(&packet.Header);
    }

    /// <summary>Monitor name (EDID friendly name, may be empty) and device path (stable id of the monitor) of a target.</summary>
    public static (string Name, string DevicePath) TargetName(Luid adapter, uint targetId)
    {
        TargetDeviceName packet = new() { Header = Header(DeviceInfoGetTargetName, sizeof(TargetDeviceName), adapter, targetId) };
        Check(DisplayConfigGetDeviceInfo(&packet.Header), "DisplayConfigGetDeviceInfo(target name)");
        return (new string(packet.MonitorFriendlyDeviceName), new string(packet.MonitorDevicePath));
    }

    /// <summary>The GDI device name of a source (<c>\\.\DISPLAY1</c>), as <c>GetMonitorInfo</c> reports it.</summary>
    public static string SourceGdiName(Luid adapter, uint sourceId)
    {
        SourceDeviceName packet = new() { Header = Header(DeviceInfoGetSourceName, sizeof(SourceDeviceName), adapter, sourceId) };
        Check(DisplayConfigGetDeviceInfo(&packet.Header), "DisplayConfigGetDeviceInfo(source name)");
        return new string(packet.ViewGdiDeviceName);
    }

    /// <summary>The monitors <c>EnumDisplayMonitors</c> reports: GDI device name, handle and bounds.</summary>
    public static List<(string Device, nint Handle, Native.Rect Bounds)> DisplayMonitors()
    {
        List<(string, nint, Native.Rect)> monitors = [];
        EnumDisplayMonitors(
            0,
            0,
            (monitor, _, _, _) =>
            {
                MonitorInfoEx info = new() { Size = sizeof(MonitorInfoEx) };
                if (GetMonitorInfo(monitor, &info))
                {
                    monitors.Add((new string(info.Device), monitor, info.Monitor));
                }

                return true;
            },
            0);
        return monitors;
    }

    /// <summary>Effective DPI of the monitor with the GDI device name (MDT_EFFECTIVE_DPI), or <see langword="null"/>.</summary>
    public static uint? EffectiveDpi(string gdiName)
    {
        foreach ((string device, nint handle, _) in DisplayMonitors())
        {
            if (string.Equals(device, gdiName, StringComparison.OrdinalIgnoreCase) && GetDpiForMonitor(handle, MdtEffectiveDpi, out uint dpiX, out _) == 0)
            {
                return dpiX;
            }
        }

        return null;
    }

    /// <summary>
    /// The system DPI of this process (<c>GetDpiForSystem</c>): the DPI of the primary monitor when the process started (at
    /// sign-in for Explorer and everything started before a scale change), unchanged while the process runs.
    /// </summary>
    public static uint SystemDpi() => GetDpiForSystem();

    /// <summary>
    /// The system DPI of another process (<c>GetSystemDpiForProcess</c>): a process started after a scale change at run time
    /// has the new DPI of the primary monitor, while the test runner and Explorer keep the one of their start (T-070).
    /// </summary>
    public static uint SystemDpiOf(int processId)
    {
        nint process = OpenProcess(ProcessQueryLimitedInformation, inheritHandle: false, (uint)processId);
        if (process == 0)
        {
            throw new InvalidOperationException($"OpenProcess failed for process {processId} (error {Marshal.GetLastPInvokeError()}).");
        }

        try
        {
            uint dpi = GetSystemDpiForProcess(process);
            return dpi != 0 ? dpi : throw new InvalidOperationException($"GetSystemDpiForProcess failed for process {processId}.");
        }
        finally
        {
            CloseHandle(process);
        }
    }

    /// <summary>Windows version with build and revision (for the results), e.g. "10.0.26300.1234 (25H2)".</summary>
    public static string WindowsBuild()
    {
        using RegistryKey? version = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        string build = version?.GetValue("CurrentBuild") as string ?? Environment.OSVersion.Version.Build.ToString(CultureInfo.InvariantCulture);
        string revision = version?.GetValue("UBR") is int ubr ? ubr.ToString(CultureInfo.InvariantCulture) : "?";
        string display = version?.GetValue("DisplayVersion") as string ?? "?";
        return $"{Environment.OSVersion.Version.Major}.{Environment.OSVersion.Version.Minor}.{build}.{revision} ({display})";
    }

    /// <summary>
    /// Windows build, system DPI and every active monitor with its scale and effective DPI, for the results of a run.
    /// Never throws (an unreadable part is reported in the text).
    /// </summary>
    public static string Report()
    {
        StringBuilder text = new();
        try
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"Windows {WindowsBuild()}; systemDpi={SystemDpi()}");
            foreach (Monitor monitor in Monitors(Query()))
            {
                string scale;
                try
                {
                    DpiScaleSteps steps = GetDpiScale(monitor.SourceAdapter, monitor.SourceId);
                    scale = DisplayScaleSteps.TryDescribe(steps);
                }
                catch (InvalidOperationException ex)
                {
                    scale = $"scale unreadable ({ex.Message})";
                }

                text.AppendLine(CultureInfo.InvariantCulture,
                    $"  {Show(monitor)}: {scale}, monitorDpi={EffectiveDpi(monitor.GdiName)?.ToString(CultureInfo.InvariantCulture) ?? "?"}");
            }
        }
        catch (Exception ex)
        {
            // Only a report: whatever fails is written into it instead of failing the run.
            text.AppendLine(CultureInfo.InvariantCulture, $"  display state unreadable: {ex.Message}");
        }

        return text.ToString();
    }

    public static string Show(Monitor monitor) =>
        $"{(monitor.Name.Length == 0 ? "(no name)" : monitor.Name)} {monitor.GdiName} {monitor.Width}x{monitor.Height} at {monitor.Left},{monitor.Top}"
        + $"{(monitor.Rotation is 2 or 4 ? " rotated" : string.Empty)}{(monitor.Primary ? " primary" : string.Empty)}";

    private static DeviceInfoHeader Header(int type, int size, Luid adapter, uint id) =>
        new() { Type = type, Size = (uint)size, AdapterId = adapter, Id = id };

    private static void Check(int result, string call)
    {
        if (result != ErrorSuccess)
        {
            throw new InvalidOperationException($"{call} failed with Win32 error {result}.");
        }
    }

    private delegate bool MonitorEnumProc(nint monitor, nint hdc, nint rect, nint data);

    [LibraryImport("user32.dll")]
    private static partial int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [LibraryImport("user32.dll")]
    private static partial int QueryDisplayConfig(uint flags, ref uint pathCount, PathInfo* paths, ref uint modeCount, ModeInfo* modes, nint currentTopologyId);

    [LibraryImport("user32.dll")]
    private static partial int SetDisplayConfig(uint pathCount, PathInfo* paths, uint modeCount, ModeInfo* modes, uint flags);

    [LibraryImport("user32.dll")]
    private static partial int DisplayConfigGetDeviceInfo(DeviceInfoHeader* packet);

    [LibraryImport("user32.dll")]
    private static partial int DisplayConfigSetDeviceInfo(DeviceInfoHeader* packet);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc callback, nint data);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfo(nint monitor, MonitorInfoEx* info);

    [LibraryImport("shcore.dll")]
    private static partial int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForSystem();

    [LibraryImport("user32.dll")]
    private static partial uint GetSystemDpiForProcess(nint process);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);
}
