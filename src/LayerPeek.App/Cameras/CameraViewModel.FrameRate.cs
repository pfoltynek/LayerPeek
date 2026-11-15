using LayerPeek.Core.Cameras;

namespace LayerPeek.App.Cameras;

/// <summary>
/// T-185: the frame rate of the mini preview. Its small image needs only a few frames per second, so its lease takes at most
/// the global <see cref="Core.Settings.AppSettings.MiniPreviewFrameRate"/> (user decisions 2026-11-10 and 2026-11-11); the
/// frames in between are decoded but neither converted, copied nor shown. The normal window and full screen (also entered
/// from the mini preview) always get the camera's full rate. A switch of the mode and new settings apply to the next frame
/// of the running lease, without reconnecting.
/// </summary>
public sealed partial class CameraViewModel
{
    // UI thread: the limit of the settings (null: none).
    private int? _miniFrameRate;

    /// <summary>The frame rate limit the current run asks the hub for (tests); <see langword="null"/>: none, or no run.</summary>
    internal int? RequestedFrameRate => _lease?.MaxFrameRate;

    /// <summary>The limit for the current mode: the setting's in the mini preview, none otherwise.</summary>
    private int? FrameRateLimit => _mode == CameraWindowMode.Mini ? _miniFrameRate : null;

    /// <summary>UI thread: the mode or the setting changed; the running lease takes the new limit from its next frame on.</summary>
    private void ApplyFrameRate() => _lease?.SetMaxFrameRate(FrameRateLimit);

    /// <summary>UI thread: the saved settings have this limit (also right after "Save" with the mini preview open).</summary>
    private void SetMiniFrameRate(int? rate)
    {
        if (_miniFrameRate != rate)
        {
            _miniFrameRate = rate;
            ApplyFrameRate();
        }
    }
}
