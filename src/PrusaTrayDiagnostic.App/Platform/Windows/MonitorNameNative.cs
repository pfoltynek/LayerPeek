using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PrusaTrayDiagnostic.App.Platform.Windows;

/// <summary>
/// Names of monitors (T-035): the friendly name Windows shows in the display settings (from the monitor's EDID, e.g.
/// "DELL U2719D") through the display configuration API, else the GDI device name (<c>\\.\DISPLAY1</c>).
/// </summary>
/// <remarks>
/// Avalonia 12.1.4 implements the same lookup (<c>WinScreen.GetDisplayName</c>), but on Windows 11 it reports an empty
/// <c>Screen.DisplayName</c> for monitors that do have a friendly name (verified 2026-10-19 with two DELL U2719D), so the
/// app asks Windows itself. Blittable structures only (<c>[LibraryImport]</c>, Native AOT).
/// </remarks>
[SupportedOSPlatform("windows")]
internal static unsafe partial class MonitorNameNative
{
    private const uint QdcOnlyActivePaths = 0x2;
    private const uint DisplayConfigDeviceInfoGetSourceName = 1;
    private const uint DisplayConfigDeviceInfoGetTargetName = 2;
    private const int ErrorSuccess = 0;
    private const int ErrorInsufficientBuffer = 122;

    /// <summary><c>sizeof(DISPLAYCONFIG_PATH_INFO)</c>: source (20) + target (48) + flags (4).</summary>
    private const int PathInfoSize = 72;

    /// <summary><c>sizeof(DISPLAYCONFIG_MODE_INFO)</c> (the union is 8-byte aligned).</summary>
    private const int ModeInfoSize = 64;

    private const int TargetInfoOffset = 20;

    /// <summary>
    /// The friendly name of the monitor <paramref name="monitor"/> (<c>HMONITOR</c>), else its GDI device name;
    /// <see langword="null"/> when Windows reports neither.
    /// </summary>
    public static string? GetName(nint monitor)
    {
        MonitorInfoEx info = default;
        info.Size = (uint)sizeof(MonitorInfoEx);
        if (monitor == 0 || !GetMonitorInfo(monitor, &info))
        {
            return null;
        }

        string device = new(info.Device);
        string? friendly = GetFriendlyName(device);
        return !string.IsNullOrWhiteSpace(friendly) ? friendly
            : !string.IsNullOrWhiteSpace(device) ? device
            : null;
    }

    /// <summary>The friendly name of the active display path whose source is the GDI device <paramref name="device"/>.</summary>
    private static string? GetFriendlyName(string device)
    {
        // The configuration may change between the two calls (a monitor plugged in): retry a few times.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out uint pathCount, out uint modeCount) != ErrorSuccess || pathCount == 0 || pathCount > 1024 || modeCount > 4096)
            {
                return null;
            }

            byte[] paths = new byte[pathCount * PathInfoSize];
            byte[] modes = new byte[Math.Max(modeCount, 1) * ModeInfoSize];
            fixed (byte* pathBuffer = paths)
            fixed (byte* modeBuffer = modes)
            {
                int result = QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, pathBuffer, ref modeCount, modeBuffer, 0);
                if (result == ErrorInsufficientBuffer)
                {
                    continue;
                }

                if (result != ErrorSuccess)
                {
                    return null;
                }

                for (int i = 0; i < pathCount; i++)
                {
                    byte* path = pathBuffer + (i * PathInfoSize);
                    SourceDeviceName source = default;
                    source.Header = Header(DisplayConfigDeviceInfoGetSourceName, (uint)sizeof(SourceDeviceName), path);
                    if (DisplayConfigGetDeviceInfo(&source.Header) != ErrorSuccess || !string.Equals(new string(source.GdiDeviceName), device, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    TargetDeviceName target = default;
                    target.Header = Header(DisplayConfigDeviceInfoGetTargetName, (uint)sizeof(TargetDeviceName), path + TargetInfoOffset);
                    return DisplayConfigGetDeviceInfo(&target.Header) == ErrorSuccess ? new string(target.MonitorFriendlyDeviceName) : null;
                }

                return null;
            }
        }

        return null;
    }

    /// <summary>A request header for the adapter and id at the start of a <c>DISPLAYCONFIG_PATH_SOURCE_INFO</c> / <c>_TARGET_INFO</c>.</summary>
    private static DeviceInfoHeader Header(uint type, uint size, byte* info) => new()
    {
        Type = type,
        Size = size,
        AdapterIdLowPart = *(uint*)info,
        AdapterIdHighPart = *(int*)(info + 4),
        Id = *(uint*)(info + 8),
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfoEx
    {
        public uint Size;
        public int MonitorLeft;
        public int MonitorTop;
        public int MonitorRight;
        public int MonitorBottom;
        public int WorkLeft;
        public int WorkTop;
        public int WorkRight;
        public int WorkBottom;
        public uint Flags;
        public fixed char Device[32];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInfoHeader
    {
        public uint Type;
        public uint Size;
        public uint AdapterIdLowPart;
        public int AdapterIdHighPart;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SourceDeviceName
    {
        public DeviceInfoHeader Header;
        public fixed char GdiDeviceName[32];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TargetDeviceName
    {
        public DeviceInfoHeader Header;
        public uint Flags;
        public uint OutputTechnology;
        public ushort EdidManufactureId;
        public ushort EdidProductCodeId;
        public uint ConnectorInstance;
        public fixed char MonitorFriendlyDeviceName[64];
        public fixed char MonitorDevicePath[128];
    }

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfo(nint monitor, MonitorInfoEx* info);

    [LibraryImport("user32.dll")]
    private static partial int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [LibraryImport("user32.dll")]
    private static partial int QueryDisplayConfig(uint flags, ref uint pathCount, byte* paths, ref uint modeCount, byte* modes, nint currentTopologyId);

    [LibraryImport("user32.dll")]
    private static partial int DisplayConfigGetDeviceInfo(DeviceInfoHeader* request);
}
