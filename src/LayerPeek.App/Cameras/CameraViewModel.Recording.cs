using CommunityToolkit.Mvvm.Input;

using LayerPeek.App.Cameras.Recording;
using LayerPeek.Core.Cameras.Recording;

namespace LayerPeek.App.Cameras;

/// <summary>
/// The recording button of the camera window (T-146, user decisions 2026-11-03): in every mode (normal window, mini preview,
/// full screen) next to the pin, it starts a manual recording of this printer's camera and stops a running one (manual or
/// automatic). While anything records, the button shows a red dot with "REC" and stays visible without the pointer over the
/// window, so the indicator is in the window and in the mini preview. While the recording starts or stops, and when this
/// build cannot record, the button is disabled with the reason as its tip. Without recordings (tests) there is no button.
/// Costs nothing while idle: no timer, no animation; the state is read only when the service reports a change of this
/// printer (rare).
/// <para>
/// T-148: a printer with a dashcam also has "Save the last N min" in the context menu of the window (normal window and mini
/// preview), enabled while the last minutes are held, otherwise disabled with the reason as its tip.
/// </para>
/// <para>T-161: the timelapse button right before the record button (<c>CameraViewModel.Timelapse.cs</c>).</para>
/// </summary>
public sealed partial class CameraViewModel
{
    private ICameraRecordings? _recordings;
    private Action? _refreshRecording;
    private RecordingControlState _recordingState;
    private DashcamControlState _dashcamState;

    /// <summary>Starts (idle) or stops (recording) the recording; disabled while it starts or stops and without a recorder.</summary>
    public IRelayCommand ToggleRecordingCommand { get; private set; } = null!;

    /// <summary>The window has the recording button (the app's recordings are there).</summary>
    public bool HasRecordingButton => _recordings is not null;

    /// <summary>Starting, recording or stopping: the button shows the red dot with "REC" and stays visible.</summary>
    public bool IsRecording => _recordingState.IsActive;

    /// <summary>Tip and accessible name of the button: what a click does, or why it is disabled.</summary>
    public string RecordingToolTip => _recordingState.ButtonToolTip;

    /// <summary>"Save the last N min" (T-148): the printer's dashcam saves now; enabled while it holds the last minutes.</summary>
    public IRelayCommand SaveDashcamCommand { get; private set; } = null!;

    /// <summary>The printer has a dashcam: the context menu has "Save the last N min".</summary>
    public bool HasDashcam => _recordings is not null && _dashcamState.IsShown;

    /// <summary>The text of the context menu item, with the length of the dashcam and its access key.</summary>
    public string SaveDashcamText => _dashcamState.Text;

    /// <summary>Why "Save the last N min" is disabled; <see langword="null"/> (no tip) while it is enabled.</summary>
    public string? SaveDashcamToolTip => _dashcamState.DisabledReason;

    private void CreateRecording(ICameraRecordings? recordings)
    {
        _recordings = recordings;
        ToggleRecordingCommand = new RelayCommand(ToggleRecording, () => !_disposed && _recordings is not null && _recordingState.IsEnabled);
        SaveDashcamCommand = new RelayCommand(SaveDashcam, () => !_disposed && _recordings is not null && _dashcamState.IsEnabled);
        if (recordings is null)
        {
            return;
        }

        _refreshRecording = RefreshRecording;
        // Subscribed before reading the state: a change in between only causes one more (idempotent) refresh.
        recordings.Changed += OnRecordingChanged;
        CameraRecordingStatus status = recordings.GetStatus(PrinterId);
        _recordingState = new(recordings.IsAvailable, status.Activity);
        _dashcamState = DashcamControlState.Of(status);
        _timelapseState = TimelapseControlState.Of(recordings.IsTimelapseAvailable, status.Timelapse);
    }

    private void DisposeRecording()
    {
        if (_recordings is not null)
        {
            _recordings.Changed -= OnRecordingChanged;
        }
    }

    /// <summary>The language changed: the tips and the dashcam item are formatted again.</summary>
    private void RefreshRecordingTexts()
    {
        OnPropertyChanged(nameof(RecordingToolTip));
        OnPropertyChanged(nameof(SaveDashcamText));
        OnPropertyChanged(nameof(SaveDashcamToolTip));
        OnPropertyChanged(nameof(TimelapseToolTip));
    }

    private void SaveDashcam()
    {
        if (!_disposed && _recordings is { } recordings && _dashcamState.IsEnabled)
        {
            recordings.SaveDashcam(PrinterId);
        }
    }

    /// <summary>Acts on what the button shows: Stop while it shows "REC", Start otherwise (never a state that changed meanwhile).</summary>
    private void ToggleRecording()
    {
        if (_disposed || _recordings is not { } recordings || !_recordingState.IsEnabled)
        {
            return;
        }

        if (_recordingState.IsActive)
        {
            recordings.Stop(PrinterId);
        }
        else
        {
            recordings.Start(PrinterId);
        }
    }

    /// <summary>Any thread: some printer's recording changed; this window reads its own state on the UI thread.</summary>
    private void OnRecordingChanged(object? sender, string printerId)
    {
        if (_refreshRecording is { } refresh && string.Equals(printerId, PrinterId, StringComparison.OrdinalIgnoreCase))
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }
            }

            _dispatcher.Post(refresh);
        }
    }

    /// <summary>UI thread: shows the current state; raises only what changed.</summary>
    private void RefreshRecording()
    {
        if (_disposed || _recordings is not { } recordings)
        {
            return;
        }

        // One read of the status for all three controls.
        CameraRecordingStatus status = recordings.GetStatus(PrinterId);
        RefreshDashcam(DashcamControlState.Of(status));
        RefreshTimelapse(TimelapseControlState.Of(recordings.IsTimelapseAvailable, status.Timelapse));
        RecordingControlState state = new(recordings.IsAvailable, status.Activity);
        if (state == _recordingState)
        {
            return;
        }

        bool wasActive = _recordingState.IsActive;
        _recordingState = state;
        if (wasActive != state.IsActive)
        {
            OnPropertyChanged(nameof(IsRecording));
        }

        OnPropertyChanged(nameof(RecordingToolTip));
        ToggleRecordingCommand.NotifyCanExecuteChanged();
    }

    /// <summary>UI thread: the dashcam item follows its state; raises only what changed.</summary>
    private void RefreshDashcam(DashcamControlState state)
    {
        if (state == _dashcamState)
        {
            return;
        }

        DashcamControlState old = _dashcamState;
        _dashcamState = state;
        if (old.IsShown != state.IsShown)
        {
            OnPropertyChanged(nameof(HasDashcam));
        }

        if (old.Minutes != state.Minutes)
        {
            OnPropertyChanged(nameof(SaveDashcamText));
        }

        OnPropertyChanged(nameof(SaveDashcamToolTip));
        SaveDashcamCommand.NotifyCanExecuteChanged();
    }
}
