using FFmpeg.AutoGen.Abstractions;

using Microsoft.Extensions.Logging;

namespace LayerPeek.Camera.FFmpeg;

/// <summary>
/// The log lines of hardware decoding (T-203): one per start (or switch) of full decoding and one per fallback to
/// software, never one per frame. Without the camera address.
/// </summary>
internal static partial class HardwareDecodingLog
{
    /// <summary>Full decoding starts or switches at a key frame: which path it takes, and why software.</summary>
    public static void Decoding(ILogger logger, AVHWDeviceType device, string? softwareReason)
    {
        if (device == AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)
        {
            LogDecodingInSoftware(logger, softwareReason ?? "no camera window");
        }
        else
        {
            LogDecoding(logger, new DeviceName(device));
        }
    }

    /// <summary>A hardware failure: the connection continues in software.</summary>
    public static void HardwareFailed(ILogger logger, AVHWDeviceType device, string stage, int error, int failures) =>
        LogHardwareFailed(logger, new DeviceName(device), stage, new ErrorText(error), error, failures, HardwareDecodingHistory.MaxConsecutiveFailures);

    /// <summary>Mapping failed: the frames of the session are copied instead.</summary>
    public static void MappingFailed(ILogger logger, AVHWDeviceType device, int error) =>
        LogMappingFailed(logger, new DeviceName(device), new ErrorText(error), error);

    /// <summary>The Debug test switch was read (<paramref name="known"/>) or had an unknown value (software then).</summary>
    public static void TestOverride(ILogger logger, string value, bool known, AVHWDeviceType device)
    {
        if (known)
        {
            LogTestOverride(logger, new DeviceName(device));
        }
        else
        {
            LogUnknownTestOverride(logger, value);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Camera video decoding: {Path}.")]
    private static partial void LogDecoding(ILogger logger, DeviceName path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Camera video decoding: software ({Reason}).")]
    private static partial void LogDecodingInSoftware(ILogger logger, string reason);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Hardware video decoding ({Device}) failed while {Stage}: {Error} (code {Code}); this connection continues in software ({Failures} of {MaxFailures} failures in a row before the camera stays in software).")]
    private static partial void LogHardwareFailed(ILogger logger, DeviceName device, string stage, ErrorText error, int code, int failures, int maxFailures);

    [LoggerMessage(Level = LogLevel.Information, Message = "Mapping hardware video frames ({Device}) failed: {Error} (code {Code}); they are copied instead.")]
    private static partial void LogMappingFailed(ILogger logger, DeviceName device, ErrorText error, int code);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unknown value '{Value}' of the hardware decoder test switch (off, videotoolbox or d3d11va); software is used.")]
    private static partial void LogUnknownTestOverride(ILogger logger, string value);

    [LoggerMessage(Level = LogLevel.Information, Message = "Hardware decoder test switch: {Path} for every camera window.")]
    private static partial void LogTestOverride(ILogger logger, DeviceName path);

    /// <summary>The name of a decoding path, formatted only when the line is written.</summary>
    private readonly struct DeviceName(AVHWDeviceType device)
    {
        public override string ToString() => HardwareDecoderSelection.Name(device);
    }

    /// <summary>FFmpeg's text of an error, formatted only when the line is written.</summary>
    private readonly struct ErrorText(int error)
    {
        public override string ToString() => FFmpegDecoder.Describe(error);
    }
}
