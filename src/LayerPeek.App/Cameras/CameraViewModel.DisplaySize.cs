using Microsoft.Extensions.Logging;

using LayerPeek.Core.Cameras;

namespace LayerPeek.App.Cameras;

/// <summary>
/// T-184: the size the camera frames are decoded at for the window: the area of the image in device pixels, so that the
/// mini preview and a small window do not convert (and copy) the camera's full resolution.
/// </summary>
/// <remarks>
/// Every new size costs the decoder a new conversion context and the window a new bitmap, so the requested size is rounded
/// up to <see cref="VideoSizeStep"/> device pixels (small changes, e.g. the mini preview correcting its height by a pixel,
/// change nothing) and, once the run has its first size, a change is requested only after the area has not changed for
/// <see cref="VideoSizeSettleTime"/> (dragging an edge or the animation of full screen is one change, not dozens). In
/// between the renderer scales the previous frames. The timer exists only after the first change and runs only until it
/// fired; an idle window has none running.
/// </remarks>
public sealed partial class CameraViewModel
{
    /// <summary>The requested frame size is rounded up to multiples of this (device pixels).</summary>
    internal const int VideoSizeStep = 32;

    /// <summary>A changed image area is requested once it has not changed for this long.</summary>
    internal static readonly TimeSpan VideoSizeSettleTime = TimeSpan.FromMilliseconds(250);

    private readonly Action _applyDisplaySize;

    // UI thread only: the size for the current image area (null: not laid out yet, full resolution), and the one the lease
    // was given.
    private CameraFrameSize? _videoSize;
    private CameraFrameSize? _requestedVideoSize;
    private ITimer? _videoSizeTimer;

    /// <summary>The frame size the current run asks the hub for (tests); <see langword="null"/>: full resolution or no run.</summary>
    internal CameraFrameSize? RequestedVideoSize => _lease?.DisplaySize;

    /// <summary>
    /// UI thread: the image is drawn in an area of <paramref name="width"/> × <paramref name="height"/> DIP on a monitor
    /// with <paramref name="scaling"/> (the window was laid out, resized, switched modes or moved to another monitor). An
    /// empty area (not laid out, minimized) keeps the last size.
    /// </summary>
    internal void SetVideoArea(double width, double height, double scaling)
    {
        if (_disposed || VideoSizeOf(width, height, scaling) is not { } size || size == _videoSize)
        {
            return;
        }

        _videoSize = size;
        if (_lease is null)
        {
            // The next run starts with it.
            return;
        }

        if (_requestedVideoSize is null)
        {
            // The first layout of a run that started in full resolution: at once.
            ApplyDisplaySize();
            return;
        }

        _videoSizeTimer ??= _timeProvider.CreateTimer(static state => ((CameraViewModel)state!).OnVideoSizeSettled(), this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _videoSizeTimer.Change(VideoSizeSettleTime, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// The frame size for an image area of <paramref name="width"/> × <paramref name="height"/> DIP at
    /// <paramref name="scaling"/>: device pixels rounded up to <see cref="VideoSizeStep"/>, at most
    /// <see cref="CameraFrameSize.MaxSide"/>; <see langword="null"/> for an empty or invalid area.
    /// </summary>
    internal static CameraFrameSize? VideoSizeOf(double width, double height, double scaling)
    {
        if (!(width > 0 && height > 0 && scaling > 0 && double.IsFinite(width * scaling) && double.IsFinite(height * scaling)))
        {
            return null;
        }

        return new CameraFrameSize(Side(width * scaling), Side(height * scaling));

        // A tiny tolerance: 640 DIP at 125 % is 800.0000001 px in floating point, not 801.
        static int Side(double pixels)
        {
            double rounded = Math.Ceiling((Math.Ceiling(pixels - 1e-6) / VideoSizeStep)) * VideoSizeStep;
            return (int)Math.Clamp(rounded, VideoSizeStep, CameraFrameSize.MaxSide);
        }
    }

    // Timer thread.
    private void OnVideoSizeSettled()
    {
        try
        {
            _dispatcher.Post(_applyDisplaySize);
        }
        catch (Exception ex)
        {
            // The UI is gone (quitting): the window closes anyway.
            LogVideoSizeNotPosted(ex);
        }
    }

    /// <summary>UI thread: the current run asks for the size of the current image area (nothing when it has it already).</summary>
    private void ApplyDisplaySize()
    {
        if (_disposed || _lease is not { } lease || _requestedVideoSize == _videoSize)
        {
            return;
        }

        _requestedVideoSize = _videoSize;
        lease.SetDisplaySize(_videoSize);
    }

    private void DisposeDisplaySize()
    {
        _videoSizeTimer?.Dispose();
        _videoSizeTimer = null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The camera image size could not be applied because the UI is shutting down.")]
    private partial void LogVideoSizeNotPosted(Exception exception);
}
