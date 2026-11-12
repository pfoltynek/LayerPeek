using FFmpeg.AutoGen.Abstractions;

using LayerPeek.Core.Cameras;

namespace LayerPeek.Camera.FFmpeg;

/// <summary>
/// The one place that decides which hardware decoder "hardware decoding" means (T-203, user decisions 2026-11-12, T-192
/// questions 2–4): <see cref="CameraHardwareDecoding.Off"/> is always software; <see cref="CameraHardwareDecoding.Automatic"/>
/// is VideoToolbox on macOS and software on Windows until the measurement of T-125 and a decision of the user change it
/// (then only this rule changes, the saved setting stays). Other device types (NVDEC, QSV, AMF, Vulkan) are never used.
/// </summary>
internal static class HardwareDecoderSelection
{
    /// <summary>The device <paramref name="mode"/> asks for on the running OS; <c>AV_HWDEVICE_TYPE_NONE</c> is software.</summary>
    public static AVHWDeviceType Select(CameraHardwareDecoding mode) => Select(mode, OperatingSystem.IsMacOS());

    /// <summary>The device <paramref name="mode"/> asks for on macOS (<paramref name="isMacOS"/>) or elsewhere (Windows).</summary>
    public static AVHWDeviceType Select(CameraHardwareDecoding mode, bool isMacOS) => mode switch
    {
        CameraHardwareDecoding.Off => AVHWDeviceType.AV_HWDEVICE_TYPE_NONE,
        // Windows (D3D11VA, T-206) stays software until T-125 measured it and the user decided.
        CameraHardwareDecoding.Automatic => isMacOS ? AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX : AVHWDeviceType.AV_HWDEVICE_TYPE_NONE,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown hardware decoding setting."),
    };

    /// <summary>
    /// The device of a value of the Debug test switch <c>LAYERPEEK_TEST_HW_DECODER</c> (T-205, T-125: <c>off</c>,
    /// <c>videotoolbox</c> or <c>d3d11va</c>, any case, surrounding spaces ignored);
    /// <see langword="false"/> for any other value.
    /// </summary>
    public static bool TryParseTestOverride(string? value, out AVHWDeviceType device)
    {
        ReadOnlySpan<char> name = value.AsSpan().Trim();
        if (name.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            device = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;
            return true;
        }

        if (name.Equals("videotoolbox", StringComparison.OrdinalIgnoreCase))
        {
            device = AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX;
            return true;
        }

        if (name.Equals("d3d11va", StringComparison.OrdinalIgnoreCase))
        {
            device = AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA;
            return true;
        }

        device = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;
        return false;
    }

    /// <summary>The name of a decoding path in the log: <c>software</c>, or FFmpeg's name of the device type.</summary>
    public static string Name(AVHWDeviceType device) => device switch
    {
        AVHWDeviceType.AV_HWDEVICE_TYPE_NONE => "software",
        AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX => "videotoolbox",
        AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA => "d3d11va",
        _ => $"device type {(int)device}",
    };
}
