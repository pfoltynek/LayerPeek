using System.Runtime.CompilerServices;

using LayerPeek.Core.Cameras;

namespace LayerPeek.Camera.FFmpeg.Tests;

/// <summary>
/// T-203: the suite runs with the default of the setting (<see cref="CameraHardwareDecoding.Automatic"/>: the display tests
/// decode through VideoToolbox on macOS); with <c>LAYERPEEK_TEST_CAMERA_HARDWARE_DECODING=off</c> every factory of the
/// suite starts with <see cref="CameraHardwareDecoding.Off"/> instead, so the same tests check the software path. Tests of
/// hardware decoding itself set their mode explicitly.
/// </summary>
internal static class HardwareDecodingTestMode
{
    public const string Variable = "LAYERPEEK_TEST_CAMERA_HARDWARE_DECODING";

    [ModuleInitializer]
    internal static void Apply()
    {
        if (string.Equals(Environment.GetEnvironmentVariable(Variable)?.Trim(), "off", StringComparison.OrdinalIgnoreCase))
        {
            FFmpegCameraFeedFactory.DefaultHardwareDecoding = CameraHardwareDecoding.Off;
        }
    }
}
