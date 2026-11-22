using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;

using LayerPeek.App.Cameras;

namespace LayerPeek.App.Windows;

/// <summary>
/// T-251 (design <c>docs/design/mini-preview-fps-badge.md</c>, user decision A 2026-11-22): the frame rate badge of the
/// mini preview gives way to the window buttons. It keeps <see cref="BadgeGap"/> to the leftmost button that is drawn: at
/// rest only the buttons that stay visible without the pointer (the checked pin, REC while recording, TL while a timelapse
/// runs), while the pointer is over the window or a button has keyboard focus every button of the row. Where it does not
/// fit, it gets the class <c>cramped</c> and fades out like the buttons; it comes back as soon as there is room. The text is
/// never cut.
/// </summary>
/// <remarks>
/// The check runs once after layout whenever something it depends on changed (the badge's or the buttons' size, the window
/// size, the pointer over the window, the keyboard focus in the buttons, the recording, timelapse or pin state, the badge
/// shown), never per frame.
/// </remarks>
public sealed partial class CameraWindow
{
    /// <summary>The room between the badge and the buttons.</summary>
    private const double BadgeGap = 8;

    /// <summary>Without any button drawn at rest, the badge keeps this distance (the buttons' margin) to the right edge.</summary>
    private const double BadgeRightEdge = 6;

    private Action? _updateFrameRateBadgeRoom;
    private bool _frameRateBadgeRoomQueued;

    /// <summary>Whether the badge gave way to the buttons (tests).</summary>
    internal bool IsFrameRateBadgeCramped => FrameRateBadge.Classes.Contains("cramped");

    /// <summary>Called once from the constructor.</summary>
    private void WatchFrameRateBadgeRoom()
    {
        _updateFrameRateBadgeRoom = UpdateFrameRateBadgeRoom;
        FrameRateBadge.SizeChanged += OnFrameRateBadgeRoomChanged;
        WindowButtons.SizeChanged += OnFrameRateBadgeRoomChanged;
        Root.SizeChanged += OnFrameRateBadgeRoomChanged;
        Root.PropertyChanged += OnFrameRateBadgeRoomPropertyChanged;
        WindowButtons.PropertyChanged += OnFrameRateBadgeRoomPropertyChanged;
    }

    /// <summary>The view model changed a property the room of the badge depends on.</summary>
    private void OnFrameRateBadgeViewModelPropertyChanged(string? propertyName)
    {
        if (propertyName is nameof(CameraViewModel.IsFrameRateBadgeVisible) or nameof(CameraViewModel.IsRecording)
            or nameof(CameraViewModel.IsTakingTimelapse) or nameof(CameraViewModel.IsTopmost)
            or nameof(CameraViewModel.HasRecordingButton) or nameof(CameraViewModel.HasTimelapseButton))
        {
            ScheduleFrameRateBadgeRoom();
        }
    }

    private void OnFrameRateBadgeRoomChanged(object? sender, SizeChangedEventArgs e) => ScheduleFrameRateBadgeRoom();

    private void OnFrameRateBadgeRoomPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsPointerOverProperty || e.Property == IsKeyboardFocusWithinProperty)
        {
            ScheduleFrameRateBadgeRoom();
        }
    }

    /// <summary>Once, after the bindings and the layout of this change (the buttons' classes follow the view model).</summary>
    private void ScheduleFrameRateBadgeRoom()
    {
        if (_frameRateBadgeRoomQueued || _updateFrameRateBadgeRoom is null)
        {
            return;
        }

        _frameRateBadgeRoomQueued = true;
        Dispatcher.UIThread.Post(_updateFrameRateBadgeRoom, DispatcherPriority.Loaded);
    }

    private void UpdateFrameRateBadgeRoom()
    {
        _frameRateBadgeRoomQueued = false;
        if (_closed || !FrameRateBadge.IsEffectivelyVisible || FrameRateBadge.TranslatePoint(default, Root) is not { } badge)
        {
            return;
        }

        bool everyButton = Root.IsPointerOver || WindowButtons.IsKeyboardFocusWithin;
        double left = Root.Bounds.Width - BadgeRightEdge;
        if (WindowButtons.IsVisible)
        {
            foreach (Control button in WindowButtons.Children)
            {
                if (button.IsVisible && (everyButton || IsDrawnAtRest(button)) && button.TranslatePoint(default, Root) is { } at)
                {
                    left = Math.Min(left, at.X);
                }
            }
        }

        FrameRateBadge.Classes.Set("cramped", badge.X + FrameRateBadge.Bounds.Width + BadgeGap > left);
    }

    /// <summary>The buttons the styles of CameraWindow.axaml keep visible without the pointer.</summary>
    private static bool IsDrawnAtRest(Control button) => button switch
    {
        ToggleButton { IsChecked: true } pin when pin.Classes.Contains("topmostPin") => true,
        Button record when record.Classes.Contains("record") => record.Classes.Contains("recording"),
        Button timelapse when timelapse.Classes.Contains("timelapse") => timelapse.Classes.Contains("active"),
        _ => false,
    };
}
