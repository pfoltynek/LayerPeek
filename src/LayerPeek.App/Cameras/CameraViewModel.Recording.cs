using CommunityToolkit.Mvvm.Input;

using LayerPeek.App.Cameras.Recording;

namespace LayerPeek.App.Cameras;

/// <summary>
/// The recording button of the camera window (T-146, user decisions 2026-11-03): in every mode (normal window, mini preview,
/// full screen) next to the pin, it starts a manual recording of this printer's camera and stops a running one (manual or
/// automatic). While anything records, the button shows a red dot with "REC" and stays visible without the pointer over the
/// window, so the indicator is in the window and in the mini preview. While the recording starts or stops, and when this
/// build cannot record, the button is disabled with the reason as its tip. Without recordings (tests) there is no button.
/// Costs nothing while idle: no timer, no animation; the state is read only when the service reports a change of this
/// printer (rare).
/// </summary>
public sealed partial class CameraViewModel
{
    private ICameraRecordings? _recordings;
    private Action? _refreshRecording;
    private RecordingControlState _recordingState;

    /// <summary>Starts (idle) or stops (recording) the recording; disabled while it starts or stops and without a recorder.</summary>
    public IRelayCommand ToggleRecordingCommand { get; private set; } = null!;

    /// <summary>The window has the recording button (the app's recordings are there).</summary>
    public bool HasRecordingButton => _recordings is not null;

    /// <summary>Starting, recording or stopping: the button shows the red dot with "REC" and stays visible.</summary>
    public bool IsRecording => _recordingState.IsActive;

    /// <summary>Tip and accessible name of the button: what a click does, or why it is disabled.</summary>
    public string RecordingToolTip => _recordingState.ButtonToolTip;

    private void CreateRecording(ICameraRecordings? recordings)
    {
        _recordings = recordings;
        ToggleRecordingCommand = new RelayCommand(ToggleRecording, () => !_disposed && _recordings is not null && _recordingState.IsEnabled);
        if (recordings is null)
        {
            return;
        }

        _refreshRecording = RefreshRecording;
        // Subscribed before reading the state: a change in between only causes one more (idempotent) refresh.
        recordings.Changed += OnRecordingChanged;
        _recordingState = RecordingControlState.Of(recordings, PrinterId);
    }

    private void DisposeRecording()
    {
        if (_recordings is not null)
        {
            _recordings.Changed -= OnRecordingChanged;
        }
    }

    /// <summary>The language changed: the tip is formatted again.</summary>
    private void RefreshRecordingTexts() => OnPropertyChanged(nameof(RecordingToolTip));

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

        RecordingControlState state = RecordingControlState.Of(recordings, PrinterId);
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
}
