using CommunityToolkit.Mvvm.Input;

using LayerPeek.App.Cameras.Recording;

namespace LayerPeek.App.Cameras;

/// <summary>
/// The timelapse button of the camera window (T-161, user decisions 2026-11-04; the whole feature is experimental): in every
/// mode right before the record button, it starts a manual timelapse of this printer and stops a running one (manual or
/// automatic). Like the record button it is hidden with the other window buttons while idle; while a timelapse runs it shows
/// its symbol with "TL" and stays visible (normal window, mini preview, full screen), dimmed while no frames are taken (the
/// print is paused). The tip and accessible name say what a click does, with "experimental", or why it is disabled
/// (starting, stopping, no recorder). The state is read only on a change of this printer, together with the recording
/// (<see cref="RefreshRecording"/>); no timer, no animation.
/// </summary>
public sealed partial class CameraViewModel
{
    private TimelapseControlState _timelapseState;

    /// <summary>Starts (idle) or stops (running) the timelapse; disabled while it starts or stops and without a recorder.</summary>
    public IRelayCommand ToggleTimelapseCommand => field ??= new RelayCommand(
        ToggleTimelapse,
        () => !_disposed && _recordings is not null && _timelapseState.IsEnabled);

    /// <summary>The window has the timelapse button (the app's recordings are there).</summary>
    public bool HasTimelapseButton => _recordings is not null;

    /// <summary>Starting, running or stopping: the button shows the timelapse indicator and stays visible.</summary>
    public bool IsTakingTimelapse => _timelapseState.IsActive;

    /// <summary>The timelapse runs but takes no frames now (the print is paused): the indicator is dimmed.</summary>
    public bool IsTimelapsePaused => _timelapseState.IsPaused;

    /// <summary>Tip and accessible name of the button: what a click does, or why it is disabled.</summary>
    public string TimelapseToolTip => _timelapseState.ButtonToolTip;

    /// <summary>Acts on what the button shows: Stop while the indicator shows, Start otherwise.</summary>
    private void ToggleTimelapse()
    {
        if (_disposed || _recordings is not { } recordings || !_timelapseState.IsEnabled)
        {
            return;
        }

        if (_timelapseState.IsActive)
        {
            recordings.StopTimelapse(PrinterId);
        }
        else
        {
            recordings.StartTimelapse(PrinterId);
        }
    }

    /// <summary>UI thread: the timelapse button follows its state; raises only what changed.</summary>
    private void RefreshTimelapse(TimelapseControlState state)
    {
        if (state == _timelapseState)
        {
            return;
        }

        TimelapseControlState old = _timelapseState;
        _timelapseState = state;
        if (old.IsActive != state.IsActive)
        {
            OnPropertyChanged(nameof(IsTakingTimelapse));
        }

        if (old.IsPaused != state.IsPaused)
        {
            OnPropertyChanged(nameof(IsTimelapsePaused));
        }

        OnPropertyChanged(nameof(TimelapseToolTip));
        ToggleTimelapseCommand.NotifyCanExecuteChanged();
    }
}
