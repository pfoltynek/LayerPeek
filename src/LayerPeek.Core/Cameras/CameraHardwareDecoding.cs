namespace LayerPeek.Core.Cameras;

/// <summary>
/// "Hardware decoding" of the camera video (T-203, user decision 2026-11-12): one value for every camera,
/// <see cref="Settings.AppSettings.CameraHardwareDecoding"/>. Core knows only the user's choice; which decoder "automatic"
/// means on the running OS (and whether it is available) is decided by the decoder (LayerPeek.Camera.FFmpeg).
/// </summary>
public enum CameraHardwareDecoding
{
    /// <summary>The default: the graphics hardware decodes the camera window's video where the OS rule allows it, otherwise software.</summary>
    Automatic,

    /// <summary>Always software: no system decoder, less memory (the safety switch of T-192, question 5).</summary>
    Off,
}
