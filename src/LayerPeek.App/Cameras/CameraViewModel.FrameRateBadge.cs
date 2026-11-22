using System.Globalization;

using Microsoft.Extensions.Logging;

using LayerPeek.App.Resources;

namespace LayerPeek.App.Cameras;

/// <summary>
/// T-251 (user decisions 2026-11-22, design <c>docs/design/mini-preview-fps-badge.md</c>): the mini preview shows the
/// frame rate it really shows and its limit ("14 / 15 fps", "24 fps / no limit" for Smooth) in the top left corner of the
/// picture, unless <see cref="Core.Settings.AppSettings.ShowMiniPreviewFrameRate"/> is off.
/// <para>
/// The badge is visible only while the picture streams in the mini preview of a shown window. From then on every frame
/// shown is recorded in a <see cref="FrameRateMeter"/> (a timestamp into a fixed ring: nothing allocated per frame), and a
/// periodic timer refreshes the value once a second from the last 2 s; until its first refresh the badge shows a dash.
/// The text changes only when the rounded value or the limit changes. Hiding the badge (another mode, minimized, no
/// picture, the option off, closing) disposes the timer, so nothing runs while it cannot be seen; showing it again starts
/// a new measurement.
/// </para>
/// </summary>
public sealed partial class CameraViewModel
{
    /// <summary>How far back the shown frames count.</summary>
    private static readonly TimeSpan FrameRateWindow = TimeSpan.FromSeconds(2);

    /// <summary>How often the value is refreshed (at most once a second, T-251).</summary>
    private static readonly TimeSpan FrameRateRefresh = TimeSpan.FromSeconds(1);

    /// <summary>A rate below two digits is padded with this figure space (as wide as a digit), so the badge does not jump.</summary>
    private const string FigureSpace = "\u2007";

    // A figure dash: as wide as a digit, like the padding.
    private const string NoValue = "\u2012";

    // UI thread only: the frames shown while the badge is visible; the option of the settings; whether the badge is
    // visible; the shown rate of the last refresh (null: none yet, the dash); the texts; the refresh timer (null: hidden).
    private FrameRateMeter? _shownFrames;
    private bool _showFrameRateBadge = true;
    private bool _isFrameRateBadgeVisible;
    private int? _shownFrameRate;
    private string _frameRateBadgeText = string.Empty;
    private string _frameRateBadgeName = string.Empty;
    private ITimer? _frameRateBadgeTimer;

    // UI thread only: counts the times the badge was shown, so that a refresh posted by the timer of an earlier showing
    // (just before it was hidden) changes nothing.
    private int _frameRateBadgeShowing;

    /// <summary>The badge is shown: the picture streams in the mini preview of a shown window and the option is on.</summary>
    public bool IsFrameRateBadgeVisible => _isFrameRateBadgeVisible;

    /// <summary>"14 / 15 fps", a figure dash in place of the value until the first refresh, "24 fps / no limit" for Smooth.</summary>
    public string FrameRateBadgeText => _frameRateBadgeText;

    /// <summary>The badge for screen readers: "Frame rate 14 of 15 fps" (plain numbers, no live region).</summary>
    public string FrameRateBadgeName => _frameRateBadgeName;

    /// <summary>Whether the timer that refreshes the badge exists (tests: none while it is hidden).</summary>
    internal bool HasFrameRateBadgeTimer => _frameRateBadgeTimer is not null;

    /// <summary>The badge's text for the shown rate <paramref name="actual"/> (<see langword="null"/>: not measured yet) and <paramref name="limit"/>.</summary>
    internal static string FormatFrameRateBadge(int? actual, int? limit)
    {
        string value = actual?.ToString(CultureInfo.CurrentCulture) ?? NoValue;
        if (value.Length < 2)
        {
            value = FigureSpace + value;
        }

        return limit is { } max
            ? Format(Strings.CameraMiniFrameRateBadge, value, max)
            : Format(Strings.CameraMiniFrameRateBadgeNoLimit, value);
    }

    /// <summary>The badge's name for screen readers: the plain numbers, no padding; "measuring" instead of the dash.</summary>
    internal static string FormatFrameRateBadgeName(int? actual, int? limit) => (actual, limit) switch
    {
        (null, { } max) => Format(Strings.CameraMiniFrameRateBadgeNameMeasuring, max),
        (null, null) => Strings.CameraMiniFrameRateBadgeNameMeasuringNoLimit,
        ({ } value, { } max) => Format(Strings.CameraMiniFrameRateBadgeName, value, max),
        ({ } value, null) => Format(Strings.CameraMiniFrameRateBadgeNameNoLimit, value),
    };

    private static string Format(string format, object? arg0, object? arg1) => string.Format(CultureInfo.CurrentCulture, format, arg0, arg1);

    /// <summary>UI thread: the saved option (also right after "Save" with the mini preview open).</summary>
    private void SetShowFrameRateBadge(bool show)
    {
        if (_showFrameRateBadge != show)
        {
            _showFrameRateBadge = show;
            UpdateFrameRateBadge();
        }
    }

    /// <summary>
    /// UI thread, after anything the badge depends on changed (mode, activity, status, a frame shown, the settings,
    /// disposing): shows it with a new measurement or hides it and its timer. Cheap when nothing changed (once per frame).
    /// </summary>
    private void UpdateFrameRateBadge()
    {
        bool visible = !_disposed && _showFrameRateBadge && _active && IsMini && _lease is not null && HasFrame && !IsStatusVisible;
        if (visible == _isFrameRateBadgeVisible)
        {
            return;
        }

        _isFrameRateBadgeVisible = visible;
        if (visible)
        {
            _shownFrames ??= new FrameRateMeter(FrameRateWindow, _timeProvider.TimestampFrequency);
            _shownFrames.Reset();
            _shownFrameRate = null;
            RefreshFrameRateBadgeTexts();
            FrameRateBadgeTick tick = new(this, ++_frameRateBadgeShowing);
            _frameRateBadgeTimer = _timeProvider.CreateTimer(static state => ((FrameRateBadgeTick)state!).Due(), tick, FrameRateRefresh, FrameRateRefresh);
        }
        else
        {
            CancelFrameRateBadgeRefresh();
        }

        OnPropertyChanged(nameof(IsFrameRateBadgeVisible));
    }

    /// <summary>UI thread, a frame was shown: counted while the badge is visible.</summary>
    private void OnFrameShownForBadge()
    {
        UpdateFrameRateBadge();
        if (_isFrameRateBadgeVisible)
        {
            _shownFrames!.Record(_timeProvider.GetTimestamp());
        }
    }

    /// <summary>UI thread: the texts for the current value, limit and language; raised only when they changed.</summary>
    private void RefreshFrameRateBadgeTexts()
    {
        int? limit = _miniFrameRate;
        string text = FormatFrameRateBadge(_shownFrameRate, limit);
        if (!string.Equals(text, _frameRateBadgeText, StringComparison.Ordinal))
        {
            _frameRateBadgeText = text;
            OnPropertyChanged(nameof(FrameRateBadgeText));
        }

        string name = FormatFrameRateBadgeName(_shownFrameRate, limit);
        if (!string.Equals(name, _frameRateBadgeName, StringComparison.Ordinal))
        {
            _frameRateBadgeName = name;
            OnPropertyChanged(nameof(FrameRateBadgeName));
        }
    }

    /// <summary>UI thread, once a second while the badge is visible: the rate of the last 2 s, rounded.</summary>
    private void RefreshFrameRateBadge(int showing)
    {
        // A refresh posted just before the badge was hidden (and maybe shown again since, with a new measurement).
        if (showing != _frameRateBadgeShowing || !_isFrameRateBadgeVisible || _shownFrames is null)
        {
            return;
        }

        int rate = (int)Math.Round(_shownFrames.RateAt(_timeProvider.GetTimestamp()), MidpointRounding.AwayFromZero);
        if (_shownFrameRate != rate)
        {
            _shownFrameRate = rate;
            RefreshFrameRateBadgeTexts();
        }
    }

    private void CancelFrameRateBadgeRefresh()
    {
        _frameRateBadgeTimer?.Dispose();
        _frameRateBadgeTimer = null;
    }

    /// <summary>The timer state of one showing of the badge: posts its refresh without allocating per tick.</summary>
    private sealed class FrameRateBadgeTick
    {
        private readonly CameraViewModel _owner;
        private readonly Action _refresh;

        public FrameRateBadgeTick(CameraViewModel owner, int showing)
        {
            _owner = owner;
            _refresh = () => owner.RefreshFrameRateBadge(showing);
        }

        // Timer thread.
        public void Due()
        {
            try
            {
                _owner._dispatcher.Post(_refresh);
            }
            catch (Exception ex)
            {
                // The UI is gone (quitting): nothing to show any more.
                _owner.LogFrameRateBadgeNotPosted(ex);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "The frame rate of the mini preview was not refreshed because the UI is shutting down.")]
    private partial void LogFrameRateBadgeNotPosted(Exception exception);
}
