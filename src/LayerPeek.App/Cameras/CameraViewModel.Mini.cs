using LayerPeek.App.Cameras.Mini;
using LayerPeek.App.Tray;
using LayerPeek.Core.Cooldown;
using LayerPeek.Core.History;

namespace LayerPeek.App.Cameras;

/// <summary>
/// The mini preview part of the camera window (T-089, T-090): the info strip below the image with its remembered rows and
/// the aspect ratio of the stream, which the window follows with its height (<see cref="CameraMiniGeometry"/>).
/// </summary>
public sealed partial class CameraViewModel
{
    // UI thread: the size of the last frame shown (compared per frame without any floating point work).
    private int _frameWidth;
    private int _frameHeight;
    private double? _videoAspectRatio;

    /// <summary>The info strip below the image of the mini preview (hidden in the other modes).</summary>
    public CameraMiniStripViewModel MiniStrip { get; }

    /// <summary>
    /// Height / width of the stream from the frames shown; <see langword="null"/> until the first frame (the mini preview
    /// then uses 16:9, T-089 Q5). A new camera address keeps the last ratio until its first frame, so that the mini preview
    /// does not change its height twice.
    /// </summary>
    public double? VideoAspectRatio
    {
        get => _videoAspectRatio;
        private set => SetProperty(ref _videoAspectRatio, value);
    }

    /// <summary>
    /// The strip of the mini preview with the rows remembered for the printer (T-090; also with "remember last placement"
    /// off, which is only about the position and size).
    /// </summary>
    private CameraMiniStripViewModel CreateMiniStrip(IUiDispatcher dispatcher, CooldownMonitor? cooldown, TelemetryHistory? history, TimeProvider clock, CameraMiniRows rows)
    {
        CameraMiniStripViewModel strip = new(PrinterId, dispatcher, cooldown, history, clock, rows);
        strip.RowsChanged += OnMiniRowsChanged;
        return strip;
    }

    /// <summary>
    /// The user expanded or collapsed a strip row: remembered right away for the printer, like the mode (T-090 Q2/Q3), also
    /// with "remember last placement" off; the rest of the remembered state stays as it is.
    /// </summary>
    private void OnMiniRowsChanged(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        CameraWindowState stored = _stateStore.Get(PrinterId);
        _stateStore.Set(PrinterId, stored with { MiniRows = MiniStrip.Rows });
    }

    /// <summary>UI thread: a frame of <paramref name="width"/> × <paramref name="height"/> pixels was shown.</summary>
    private void OnFrameShown(int width, int height)
    {
        if (width == _frameWidth && height == _frameHeight)
        {
            return;
        }

        _frameWidth = width;
        _frameHeight = height;
        if (CameraMiniGeometry.AspectRatioOf(width, height) is { } ratio)
        {
            VideoAspectRatio = ratio;
        }
    }
}
