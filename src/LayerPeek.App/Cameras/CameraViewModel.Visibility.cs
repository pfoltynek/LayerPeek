using Microsoft.Extensions.Logging;

using LayerPeek.Core.Display;

namespace LayerPeek.App.Cameras;

/// <summary>
/// T-186 (user decision 2026-11-11): the window takes frames only while somebody can see it. It is active when it is
/// shown and not minimized (<see cref="SetActive"/>), not cloaked by the window manager (<see cref="SetCloaked"/>: Windows,
/// another virtual desktop) and the screen can be seen (<see cref="IDisplayActivityService"/>: the session is unlocked and
/// the display is on). Otherwise its lease is released like when minimized; recording, the dashcam, timelapses and print
/// failure detection hold leases of their own and go on. Back to visible, the lease is taken again and the picture comes
/// back from the next key frame the decoder gets (the shared connection is still open while another consumer holds it or
/// within the hub's linger time; the decoder restarts at a key frame with the remembered SPS/PPS, T-207). Applies to every
/// mode, the always-on-top mini preview too.
/// </summary>
public sealed partial class CameraViewModel
{
    private readonly IDisplayActivityService _displayActivity;
    private readonly Action _applyScreenVisibility;

    // UI thread only: what the window and the screen report; _active is all three together.
    private bool _windowShown;
    private bool _cloaked;
    private bool _screenVisible;

    /// <summary>Whether the screen counts as visible for this window (tests).</summary>
    internal bool IsScreenVisible => _screenVisible;

    /// <summary>
    /// The window manager hides the shown window without minimizing it (<see langword="true"/>: Windows, another virtual
    /// desktop): the stream pauses like when minimized, and resumes when the window is shown again.
    /// </summary>
    public void SetCloaked(bool cloaked)
    {
        if (_disposed || _cloaked == cloaked)
        {
            return;
        }

        _cloaked = cloaked;
        UpdateActive();
    }

    private void SubscribeScreenVisibility()
    {
        _displayActivity.ScreenVisibilityChanged += OnScreenVisibilityChanged;
        // After subscribing: a change in between only causes one more (idempotent) update.
        _screenVisible = _displayActivity.IsScreenVisible;
    }

    private void UnsubscribeScreenVisibility() => _displayActivity.ScreenVisibilityChanged -= OnScreenVisibilityChanged;

    // Any thread.
    private void OnScreenVisibilityChanged(object? sender, EventArgs e)
    {
        try
        {
            _dispatcher.Post(_applyScreenVisibility);
        }
        catch (Exception ex)
        {
            // The UI is gone (quitting): nothing to pause or resume.
            LogScreenVisibilityNotPosted(ex);
        }
    }

    // UI thread.
    private void ApplyScreenVisibility()
    {
        if (_disposed)
        {
            return;
        }

        bool visible = _displayActivity.IsScreenVisible;
        if (_screenVisible != visible)
        {
            _screenVisible = visible;
            UpdateActive();
        }
    }

    /// <summary>UI thread, after the window, its cloaking or the screen changed: starts or stops the stream when the result changed.</summary>
    private void UpdateActive()
    {
        bool active = _windowShown && !_cloaked && _screenVisible;
        if (_disposed || _active == active)
        {
            return;
        }

        _active = active;
        if (active)
        {
            // Shown again (opened, restored, unlocked, display on, back on this desktop): a reconnect wait ends (T-188).
            StartStream(shortensReconnectWait: true);
        }
        else
        {
            StopStream();
        }

        // T-194: the time to the next attempt only while visible (no timer while minimized, hidden or closed).
        RefreshNextAttempt();
        // T-251: so is the frame rate of the mini preview.
        UpdateFrameRateBadge();
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "The camera window did not follow the screen visibility because the UI is shutting down.")]
    private partial void LogScreenVisibilityNotPosted(Exception exception);
}
