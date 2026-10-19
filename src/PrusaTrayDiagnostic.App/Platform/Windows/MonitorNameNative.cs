using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PrusaTrayDiagnostic.App.Platform.Windows;

/// <summary>
/// What Windows reports for a monitor (T-035): <paramref name="Name"/> is the friendly name of the display settings (from
/// the monitor's EDID, e.g. "DELL U2719D"), else the GDI device name (<c>\.\DISPLAY1</c>); <paramref name="DevicePath"/>
/// identifies the physical monitor on its connector (<c>monitorDevicePath</c>), <see langword="null"/> when unknown.
/// </summary>
internal readonly record struct MonitorIdentity(string Name, string? DevicePath);

/// <summary>
/// Names and device paths of monitors (T-035) through the display configuration API.
/// </summary>
/// <remarks>
/// Avalonia 12.1.4 implements the name lookup (<c>WinScreen.GetDisplayName</c>), but on Windows 11 it reports an empty
/// <c>Screen.DisplayName</c> for monitors that do have a friendly name (verified 2026-10-19 with two DELL U2719D), so the
/// app asks Windows itself; Avalonia has no device path at all. Blittable structures only (<c>[LibraryImport]</c>,
/// Native AOT). Fixed buffers are read up to their first NUL, never beyond their capacity.
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
    /// The identity of the monitor <paramref name="monitor"/> (<c>HMONITOR</c>); <see langword="null"/> when Windows reports
    /// no name for it.
    /// </summary>
    public static MonitorIdentity? GetIdentity(nint monitor)
    {
        MonitorInfoEx info = default;
        info.Size = (uint)sizeof(MonitorInfoEx);
        if (monitor == 0 || !GetMonitorInfo(monitor, &info))
        {
            return null;
        }

        string device = Text(info.Device, 32);
        (string? friendly, string? devicePath) = GetTarget(device);
        string? name = !string.IsNullOrWhiteSpace(friendly) ? friendly
            : !string.IsNullOrWhiteSpace(device) ? device
            : null;
        return name is null ? null : new MonitorIdentity(name, string.IsNullOrWhiteSpace(devicePath) ? null : devicePath);
    }

    /// <summary>The friendly name and device path of the active display path whose source is the GDI device <paramref name="device"/>.</summary>
    private static (string? FriendlyName, string? DevicePath) GetTarget(string device)
    {
        // The configuration may change between the two calls (a monitor plugged in): retry a few times.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out uint pathCount, out uint modeCount) != ErrorSuccess || pathCount == 0 || pathCount > 1024 || modeCount > 4096)
            {
                return default;
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
                    return default;
                }

                for (int i = 0; i < pathCount; i++)
                {
                    byte* path = pathBuffer + (i * PathInfoSize);
                    SourceDeviceName source = default;
                    source.Header = Header(DisplayConfigDeviceInfoGetSourceName, (uint)sizeof(SourceDeviceName), path);
                    if (DisplayConfigGetDeviceInfo(&source.Header) != ErrorSuccess || !string.Equals(Text(source.GdiDeviceName, 32), device, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    TargetDeviceName target = default;
                    target.Header = Header(DisplayConfigDeviceInfoGetTargetName, (uint)sizeof(TargetDeviceName), path + TargetInfoOffset);
                    return DisplayConfigGetDeviceInfo(&target.Header) == ErrorSuccess
                        ? (Text(target.MonitorFriendlyDeviceName, 64), Text(target.MonitorDevicePath, 128))
                        : default;
                }

                return default;
            }
        }

        return default;
    }

    /// <summary>A fixed UTF-16 buffer of <paramref name="capacity"/> characters up to its first NUL.</summary>
    private static string Text(char* buffer, int capacity)
    {
        ReadOnlySpan<char> text = new(buffer, capacity);
        int end = text.IndexOf('\0');
        return new string(end < 0 ? text : text[..end]);
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
