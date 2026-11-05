using CommunityToolkit.Mvvm.Input;

using LayerPeek.App.Cameras.Recording;
using LayerPeek.App.Localization;
using LayerPeek.App.Resources;
using LayerPeek.Core.Cameras.Recording;

namespace LayerPeek.App.Settings;

/// <summary>
/// The "Recording" section (T-146, user decisions 2026-11-03): the folder of all recordings (the system's video folder by
/// default; every printer gets a subfolder in it) chosen in the system's folder dialog, the total size limit in whole GB, and
/// "Open recordings folder". "Record prints automatically" is per printer on its Camera sub-tab. When this build cannot
/// record, the options are disabled with the reason (the folder can still be opened: older recordings stay there).
/// A chosen folder is checked right away (on this computer, an absolute path the settings accept, writable); a folder that
/// fails is never taken and the reason is shown in place of the hint. Saved and applied by Save like the other settings.
/// <para>
/// T-148 "Last minutes of a print (dashcam)": the length the dashcam holds (whole minutes, 1–10) and which events save it
/// automatically (each on by default); whether a printer has a dashcam is per printer on its Camera sub-tab. Disabled with
/// the reason like the folder when this build cannot record.
/// </para>
/// <para>
/// T-161 "Timelapse" (experimental, user decision 2026-11-04: the badge is next to the heading): the time between two frames,
/// global for all printers (5–600 whole seconds, default 30; the video is always 30 fps, no option); whether a printer takes
/// a timelapse of its prints is on its Camera sub-tab. A free whole number like the other numbers of the tab (presets could
/// not offer every value the service accepts), validated like them (T-112). Disabled with the reason without the recorder.
/// </para>
/// <para>
/// T-159 what takes the frames (experimental like the whole section): a frame every interval (the default) or every N-th
/// layer change estimated from the Z axis (user decision 2026-11-04: N configurable, 1 by default, 1–100), for all printers.
/// The combo box replaces the label of the interval; next to it the number of the chosen trigger (the other one keeps its
/// value and is neither shown nor validated: an invalid hidden value falls back to the one loaded). The explanation that the
/// layer is an estimate is the tip and help text of both (no room for a hint line, T-033).
/// </para>
/// </summary>
public sealed partial class SettingsViewModel
{
    private string? _recordingFolder;
    private decimal? _recordingLimitGigabytes = CameraRecordingSettings.DefaultSizeLimitGigabytes;
    private string? _recordingLimitError;
    private Func<string>? _recordingFolderErrorSource;
    private bool _isChoosingRecordingFolder;
    private IAsyncRelayCommand? _chooseRecordingFolderCommand;
    private IRelayCommand? _resetRecordingFolderCommand;
    private IRelayCommand? _openRecordingsFolderCommand;
    private decimal? _dashcamMinutes = CameraRecordingSettings.DefaultDashcamMinutes;
    private string? _dashcamMinutesError;
    private bool _dashcamOnError = true;
    private bool _dashcamOnAttention = true;
    private bool _dashcamOnStopped = true;
    private bool _dashcamOnFailureDetection = true;

    private decimal? _timelapseIntervalSeconds = CameraRecordingSettings.DefaultTimelapseIntervalSeconds;
    private string? _timelapseIntervalError;
    private TimelapseTriggerOption? _selectedTimelapseTrigger;
    private decimal? _timelapseEveryNthLayer = CameraRecordingSettings.DefaultTimelapseEveryNthLayer;
    private string? _timelapseEveryNthLayerError;

    // The values loaded: what a hidden invalid value of the other trigger falls back to when saving.
    private int _loadedTimelapseIntervalSeconds = CameraRecordingSettings.DefaultTimelapseIntervalSeconds;
    private int _loadedTimelapseEveryNthLayer = CameraRecordingSettings.DefaultTimelapseEveryNthLayer;

    /// <summary>The fixed frame rate of every timelapse video (T-158), told by the hint of the interval.</summary>
    private readonly int _timelapseFramesPerSecond = LayerPeek.Core.Cameras.Timelapse.ICameraTimelapseRecorder.FramesPerSecond;

    /// <summary>The app's recordings (state, default folder, opening the folder); <see langword="null"/>: none (tests).</summary>
    internal ICameraRecordings? Recordings { get; init; }

    /// <summary>The folder dialog, set by the window; <see langword="null"/>: no dialog ("Change…" is disabled).</summary>
    internal IRecordingFolderPicker? RecordingFolderPicker
    {
        get;
        set
        {
            field = value;
            ChooseRecordingFolderCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>Checks that a chosen folder can be written to (off the UI thread); tests replace it.</summary>
    internal Func<string, bool> CanWriteFolder { get; init; } = CanWriteTo;

    public static decimal MinRecordingLimitGigabytes => CameraRecordingSettings.MinSizeLimitGigabytes;

    public static decimal MaxRecordingLimitGigabytes => CameraRecordingSettings.MaxSizeLimitGigabytes;

    /// <summary>This build can record (T-144); without the app's recordings (tests) the options are editable.</summary>
    public bool IsRecordingAvailable => Recordings?.IsAvailable ?? true;

    /// <summary>The folder as shown: the chosen one, or the default (the system's video folder + LayerPeek).</summary>
    public string RecordingFolderText => _recordingFolder ?? DefaultRecordingFolder;

    /// <summary>A folder of the user's own is chosen: "Default" brings back the system's video folder.</summary>
    public bool HasCustomRecordingFolder => _recordingFolder is not null;

    /// <summary>The chosen folder could not be taken (why); <see langword="null"/> while the hint is shown.</summary>
    public string? RecordingFolderError => _recordingFolderErrorSource?.Invoke();

    /// <summary>The hint of the folder: no error, and this build can record (otherwise the reason is shown in its place).</summary>
    public bool ShowsRecordingFolderHint => _recordingFolderErrorSource is null && IsRecordingAvailable;

    /// <summary>For screen readers: the error of the folder, else why recording is not available, else its hint.</summary>
    public string RecordingFolderHelpText => RecordingFolderError ?? (IsRecordingAvailable ? Strings.SettingsRecordingFolderHint : Strings.RecordingUnavailable);

    /// <summary>Total size of all recordings in whole GB; the oldest recordings are deleted above it (T-145).</summary>
    public decimal? RecordingLimitGigabytes
    {
        get => _recordingLimitGigabytes;
        set => SetGeneral(ref _recordingLimitGigabytes, value);
    }

    public string? RecordingLimitError
    {
        get => _recordingLimitError;
        private set
        {
            if (SetProperty(ref _recordingLimitError, value))
            {
                OnPropertyChanged(nameof(HasRecordingLimitError));
                OnPropertyChanged(nameof(ShowsRecordingLimitHint));
                OnPropertyChanged(nameof(RecordingLimitHelpText));
            }
        }
    }

    public bool HasRecordingLimitError => _recordingLimitError is not null;

    public bool ShowsRecordingLimitHint => _recordingLimitError is null;

    /// <summary>The hint of the limit with the free space the recordings need (T-145: 5 GB).</summary>
    public string RecordingLimitHint => Format(Strings.SettingsRecordingLimitHint, MinFreeGigabytes);

    /// <summary>For screen readers: the error of the limit, else its hint.</summary>
    public string RecordingLimitHelpText => _recordingLimitError ?? RecordingLimitHint;

    public static decimal MinDashcamMinutes => CameraRecordingSettings.MinDashcamMinutes;

    public static decimal MaxDashcamMinutes => CameraRecordingSettings.MaxDashcamMinutes;

    /// <summary>Minutes the dashcam holds in memory during a print (T-148).</summary>
    public decimal? DashcamMinutes
    {
        get => _dashcamMinutes;
        set => SetGeneral(ref _dashcamMinutes, value);
    }

    public string? DashcamMinutesError
    {
        get => _dashcamMinutesError;
        private set
        {
            if (SetProperty(ref _dashcamMinutesError, value))
            {
                OnPropertyChanged(nameof(HasDashcamMinutesError));
                OnPropertyChanged(nameof(ShowsDashcamMinutesHint));
                OnPropertyChanged(nameof(DashcamMinutesHelpText));
            }
        }
    }

    public bool HasDashcamMinutesError => _dashcamMinutesError is not null;

    public bool ShowsDashcamMinutesHint => _dashcamMinutesError is null;

    /// <summary>The hint of the length: the memory it takes and the time saved after the event.</summary>
    public string DashcamMinutesHint => Format(Strings.SettingsDashcamMinutesHint, (int)(Recordings?.DashcamPostEventDuration ?? CameraRecordingServiceOptions.Default.DashcamPostEventDuration).TotalSeconds);

    /// <summary>For screen readers: the error of the length, else its hint.</summary>
    public string DashcamMinutesHelpText => _dashcamMinutesError ?? DashcamMinutesHint;

    /// <summary>The dashcam saves on a print error (T-148).</summary>
    public bool DashcamOnError
    {
        get => _dashcamOnError;
        set => SetGeneral(ref _dashcamOnError, value);
    }

    /// <summary>The dashcam saves when the printer needs attention.</summary>
    public bool DashcamOnAttention
    {
        get => _dashcamOnAttention;
        set => SetGeneral(ref _dashcamOnAttention, value);
    }

    /// <summary>The dashcam saves when the print is stopped.</summary>
    public bool DashcamOnStopped
    {
        get => _dashcamOnStopped;
        set => SetGeneral(ref _dashcamOnStopped, value);
    }

    /// <summary>The dashcam saves when the print failure detection of the camera reports a failure (T-155).</summary>
    public bool DashcamOnFailureDetection
    {
        get => _dashcamOnFailureDetection;
        set => SetGeneral(ref _dashcamOnFailureDetection, value);
    }

    public static decimal MinTimelapseIntervalSeconds => CameraRecordingSettings.MinTimelapseIntervalSeconds;

    public static decimal MaxTimelapseIntervalSeconds => CameraRecordingSettings.MaxTimelapseIntervalSeconds;

    /// <summary>This build can take timelapses (T-160); without the app's recordings (tests) the options are editable.</summary>
    public bool IsTimelapseAvailable => Recordings?.IsTimelapseAvailable ?? true;

    /// <summary>The time between two timelapse frames in whole seconds (T-161), for all printers.</summary>
    public decimal? TimelapseIntervalSeconds
    {
        get => _timelapseIntervalSeconds;
        set => SetGeneral(ref _timelapseIntervalSeconds, value);
    }

    public string? TimelapseIntervalError
    {
        get => _timelapseIntervalError;
        private set
        {
            if (SetProperty(ref _timelapseIntervalError, value))
            {
                OnPropertyChanged(nameof(HasTimelapseIntervalError));
                OnPropertyChanged(nameof(ShowsTimelapseUnavailable));
                OnPropertyChanged(nameof(TimelapseIntervalHelpText));
            }
        }
    }

    public bool HasTimelapseIntervalError => _timelapseIntervalError is not null;

    /// <summary>Without the recorder the reason takes the place of the hint (unless an error does).</summary>
    public bool ShowsTimelapseUnavailable => _timelapseIntervalError is null && _timelapseEveryNthLayerError is null && !IsTimelapseAvailable;

    /// <summary>The hint of the interval: no frames during a pause, the fixed frame rate of the video.</summary>
    public string TimelapseIntervalHint => Format(Strings.SettingsTimelapseIntervalHint, _timelapseFramesPerSecond);

    /// <summary>For screen readers: the error of the interval, else why timelapses are not available, else its hint.</summary>
    public string TimelapseIntervalHelpText => _timelapseIntervalError ?? (IsTimelapseAvailable ? TimelapseIntervalHint : Strings.TimelapseUnavailable);

    /// <summary>What takes the timelapse frames (T-159): by time or by layer, in this order.</summary>
    public IReadOnlyList<TimelapseTriggerOption> TimelapseTriggerOptions => field ??=
    [
        new TimelapseTriggerOption(CameraTimelapseTriggerKind.Time, static () => Strings.SettingsTimelapseTriggerTime),
        new TimelapseTriggerOption(CameraTimelapseTriggerKind.Layer, static () => Strings.SettingsTimelapseTriggerLayer),
    ];

    public TimelapseTriggerOption SelectedTimelapseTrigger
    {
        get => _selectedTimelapseTrigger ?? TimelapseTriggerOptions[0];
        set
        {
            // A combo box may write null while its items change.
            if (value is null || ReferenceEquals(value, SelectedTimelapseTrigger))
            {
                return;
            }

            SetGeneral(ref _selectedTimelapseTrigger, value);
            OnPropertyChanged(nameof(IsTimelapseByTime));
            OnPropertyChanged(nameof(IsTimelapseByLayer));
            OnPropertyChanged(nameof(TimelapseTriggerHelpText));
        }
    }

    /// <summary>A frame every interval: the interval shows next to the trigger.</summary>
    public bool IsTimelapseByTime => SelectedTimelapseTrigger.Value == CameraTimelapseTriggerKind.Time;

    /// <summary>A frame every N-th layer: N shows next to the trigger.</summary>
    public bool IsTimelapseByLayer => SelectedTimelapseTrigger.Value == CameraTimelapseTriggerKind.Layer;

    public static decimal MinTimelapseEveryNthLayer => CameraRecordingSettings.MinTimelapseEveryNthLayer;

    public static decimal MaxTimelapseEveryNthLayer => CameraRecordingSettings.MaxTimelapseEveryNthLayer;

    /// <summary>The layer trigger takes a frame every this many layer changes (T-159), for all printers.</summary>
    public decimal? TimelapseEveryNthLayer
    {
        get => _timelapseEveryNthLayer;
        set => SetGeneral(ref _timelapseEveryNthLayer, value);
    }

    public string? TimelapseEveryNthLayerError
    {
        get => _timelapseEveryNthLayerError;
        private set
        {
            if (SetProperty(ref _timelapseEveryNthLayerError, value))
            {
                OnPropertyChanged(nameof(HasTimelapseEveryNthLayerError));
                OnPropertyChanged(nameof(ShowsTimelapseUnavailable));
                OnPropertyChanged(nameof(TimelapseEveryNthLayerHelpText));
            }
        }
    }

    public bool HasTimelapseEveryNthLayerError => _timelapseEveryNthLayerError is not null;

    /// <summary>The hint of N: what the layer trigger takes, that the layer is an estimate from the Z axis, the frame rate.</summary>
    public string TimelapseEveryNthLayerHint => Format(Strings.SettingsTimelapseEveryNthLayerHint, _timelapseFramesPerSecond);

    /// <summary>For screen readers: the error of N, else why timelapses are not available, else its hint.</summary>
    public string TimelapseEveryNthLayerHelpText => _timelapseEveryNthLayerError ?? (IsTimelapseAvailable ? TimelapseEveryNthLayerHint : Strings.TimelapseUnavailable);

    /// <summary>The tip and help text of the trigger: both triggers, the layer being an estimate.</summary>
    public string TimelapseTriggerHelpText => IsTimelapseAvailable ? Strings.SettingsTimelapseTriggerHint : Strings.TimelapseUnavailable;

    /// <summary>A field of the Recording tab is invalid: a failed save shows that tab.</summary>
    internal bool HasRecordingTabError =>
        _recordingLimitError is not null || _dashcamMinutesError is not null || _timelapseIntervalError is not null || _timelapseEveryNthLayerError is not null
        || _detectionIntervalError is not null;

    /// <summary>Opens the folder dialog; the chosen folder is checked before it is taken.</summary>
    public IAsyncRelayCommand ChooseRecordingFolderCommand => _chooseRecordingFolderCommand ??= new AsyncRelayCommand(
        ChooseRecordingFolderAsync,
        () => IsEditable && IsRecordingAvailable && RecordingFolderPicker is not null && !_isChoosingRecordingFolder);

    /// <summary>Back to the system's video folder.</summary>
    public IRelayCommand ResetRecordingFolderCommand => _resetRecordingFolderCommand ??= new RelayCommand(
        ResetRecordingFolder,
        () => IsEditable && IsRecordingAvailable && _recordingFolder is not null);

    /// <summary>Shows the folder in the field (also an unsaved choice) in Explorer / Finder, created when missing.</summary>
    public IRelayCommand OpenRecordingsFolderCommand => _openRecordingsFolderCommand ??= new RelayCommand(
        () => _ = Recordings?.OpenFolderAsync(RecordingFolderText),
        () => Recordings is not null);

    private string DefaultRecordingFolder => Recordings?.DefaultFolder ?? CameraRecordingFolders.DefaultRoot();

    /// <summary>Whole GB (10^9 bytes, like the limit), rounded up so that the hint never promises less than is needed.</summary>
    private long MinFreeGigabytes => ((Recordings?.MinFreeBytes ?? CameraRecordingServiceOptions.Default.MinFreeBytes) + 999_999_999) / 1_000_000_000;

    /// <summary>Loading: the saved folder and limit; no error.</summary>
    private void PopulateRecording(CameraRecordingSettings recording)
    {
        _recordingFolder = recording.Folder;
        _recordingLimitGigabytes = recording.SizeLimitGigabytes;
        _recordingFolderErrorSource = null;
        _dashcamMinutes = recording.DashcamMinutes;
        _dashcamOnError = recording.DashcamOnError;
        _dashcamOnAttention = recording.DashcamOnAttention;
        _dashcamOnStopped = recording.DashcamOnStopped;
        _dashcamOnFailureDetection = recording.DashcamOnFailureDetection;
        _timelapseIntervalSeconds = recording.TimelapseIntervalSeconds;
        _loadedTimelapseIntervalSeconds = recording.TimelapseIntervalSeconds;
        _selectedTimelapseTrigger = FindTimelapseTrigger(recording.TimelapseTrigger);
        _timelapseEveryNthLayer = recording.TimelapseEveryNthLayer;
        _loadedTimelapseEveryNthLayer = recording.TimelapseEveryNthLayer;
    }

    private TimelapseTriggerOption FindTimelapseTrigger(CameraTimelapseTriggerKind trigger)
    {
        foreach (TimelapseTriggerOption option in TimelapseTriggerOptions)
        {
            if (option.Value == trigger)
            {
                return option;
            }
        }

        return TimelapseTriggerOptions[0];
    }

    /// <summary>
    /// The recording settings as edited; <see langword="null"/> while the limit, the dashcam length or the number of the
    /// chosen timelapse trigger is invalid (the hidden one of the other trigger falls back to its loaded value).
    /// </summary>
    private CameraRecordingSettings? BuildRecording() =>
        _recordingLimitGigabytes is { } limit && _recordingLimitError is null && _dashcamMinutes is { } minutes && _dashcamMinutesError is null
            && _timelapseIntervalError is null && _timelapseEveryNthLayerError is null
            ? new CameraRecordingSettings
            {
                Folder = _recordingFolder,
                SizeLimitGigabytes = (int)limit,
                DashcamMinutes = (int)minutes,
                DashcamOnError = _dashcamOnError,
                DashcamOnAttention = _dashcamOnAttention,
                DashcamOnStopped = _dashcamOnStopped,
                DashcamOnFailureDetection = _dashcamOnFailureDetection,
                TimelapseIntervalSeconds = WholeOr(_timelapseIntervalSeconds, CameraRecordingSettings.MinTimelapseIntervalSeconds, CameraRecordingSettings.MaxTimelapseIntervalSeconds, _loadedTimelapseIntervalSeconds),
                TimelapseTrigger = SelectedTimelapseTrigger.Value,
                TimelapseEveryNthLayer = WholeOr(_timelapseEveryNthLayer, CameraRecordingSettings.MinTimelapseEveryNthLayer, CameraRecordingSettings.MaxTimelapseEveryNthLayer, _loadedTimelapseEveryNthLayer),
            }
            : null;

    private static int WholeOr(decimal? value, int min, int max, int fallback) => IsWholeNumber(value, min, max) ? (int)value!.Value : fallback;

    private void ValidateRecording()
    {
        RecordingLimitError = IsWholeNumber(_recordingLimitGigabytes, CameraRecordingSettings.MinSizeLimitGigabytes, CameraRecordingSettings.MaxSizeLimitGigabytes)
            ? null
            : Format(Strings.SettingsErrorRecordingLimit, CameraRecordingSettings.MinSizeLimitGigabytes, CameraRecordingSettings.MaxSizeLimitGigabytes);
        // The same text as the limit: "Enter a whole number from {0} to {1}."
        DashcamMinutesError = IsWholeNumber(_dashcamMinutes, CameraRecordingSettings.MinDashcamMinutes, CameraRecordingSettings.MaxDashcamMinutes)
            ? null
            : Format(Strings.SettingsErrorRecordingLimit, CameraRecordingSettings.MinDashcamMinutes, CameraRecordingSettings.MaxDashcamMinutes);
        // Only the number of the chosen trigger: the other one is hidden.
        TimelapseIntervalError = !IsTimelapseByTime || IsWholeNumber(_timelapseIntervalSeconds, CameraRecordingSettings.MinTimelapseIntervalSeconds, CameraRecordingSettings.MaxTimelapseIntervalSeconds)
            ? null
            : Format(Strings.SettingsErrorRecordingLimit, CameraRecordingSettings.MinTimelapseIntervalSeconds, CameraRecordingSettings.MaxTimelapseIntervalSeconds);
        TimelapseEveryNthLayerError = !IsTimelapseByLayer || IsWholeNumber(_timelapseEveryNthLayer, CameraRecordingSettings.MinTimelapseEveryNthLayer, CameraRecordingSettings.MaxTimelapseEveryNthLayer)
            ? null
            : Format(Strings.SettingsErrorRecordingLimit, CameraRecordingSettings.MinTimelapseEveryNthLayer, CameraRecordingSettings.MaxTimelapseEveryNthLayer);
    }

    private static bool IsWholeNumber(decimal? value, int min, int max) =>
        value is { } number && number >= min && number <= max && number == decimal.Truncate(number);

    /// <summary>The language changed: texts with a format and the error are formatted again.</summary>
    private void RefreshRecordingTexts()
    {
        OnPropertyChanged(nameof(RecordingLimitHint));
        OnPropertyChanged(nameof(RecordingLimitHelpText));
        OnPropertyChanged(nameof(RecordingFolderError));
        OnPropertyChanged(nameof(RecordingFolderHelpText));
        OnPropertyChanged(nameof(DashcamMinutesHint));
        OnPropertyChanged(nameof(DashcamMinutesHelpText));
        OnPropertyChanged(nameof(TimelapseIntervalHint));
        OnPropertyChanged(nameof(TimelapseIntervalHelpText));
        foreach (TimelapseTriggerOption option in TimelapseTriggerOptions)
        {
            option.RefreshText();
        }

        OnPropertyChanged(nameof(TimelapseEveryNthLayerHint));
        OnPropertyChanged(nameof(TimelapseEveryNthLayerHelpText));
        OnPropertyChanged(nameof(TimelapseTriggerHelpText));
    }

    /// <summary>Editing became possible or impossible (loading, saving): the buttons follow.</summary>
    private void OnRecordingEditableChanged()
    {
        ChooseRecordingFolderCommand.NotifyCanExecuteChanged();
        ResetRecordingFolderCommand.NotifyCanExecuteChanged();
    }

    private async Task ChooseRecordingFolderAsync()
    {
        if (RecordingFolderPicker is not { } picker || _disposed)
        {
            return;
        }

        _isChoosingRecordingFolder = true;
        ChooseRecordingFolderCommand.NotifyCanExecuteChanged();
        try
        {
            PickedFolder? picked = await picker.PickAsync(RecordingFolderText).ConfigureAwait(true);
            if (picked is not { } folder || _disposed)
            {
                return;
            }

            if (folder.LocalPath is not { } path)
            {
                SetRecordingFolderError(static () => Strings.SettingsRecordingFolderNotLocal);
                return;
            }

            if (!CameraRecordingSettings.IsValidFolder(path))
            {
                SetRecordingFolderError(static () => Format(Strings.SettingsRecordingFolderInvalid, CameraRecordingSettings.MaxFolderLength));
                return;
            }

            string trimmed = path.Trim();
            Func<string, bool> canWrite = CanWriteFolder;
            bool writable = await Task.Run(() => canWrite(trimmed)).ConfigureAwait(true);
            if (_disposed)
            {
                return;
            }

            if (!writable)
            {
                SetRecordingFolderError(static () => Strings.SettingsRecordingFolderNotWritable);
                return;
            }

            SetRecordingFolderError(null);
            // The default folder chosen in the dialog is the default: nothing of the user's own to save.
            SetRecordingFolder(string.Equals(Path.TrimEndingDirectorySeparator(trimmed), Path.TrimEndingDirectorySeparator(DefaultRecordingFolder), StringComparison.Ordinal) ? null : trimmed);
        }
        catch (Exception ex)
        {
            // The dialog of the OS failed (e.g. no portal on Linux): the folder stays as it was.
            SettingsLog.RecordingFolderPickerFailed(Logger, ex.GetType().Name);
            SetRecordingFolderError(static () => Strings.SettingsRecordingFolderNotWritable);
        }
        finally
        {
            _isChoosingRecordingFolder = false;
            ChooseRecordingFolderCommand.NotifyCanExecuteChanged();
        }
    }

    private void ResetRecordingFolder()
    {
        SetRecordingFolderError(null);
        SetRecordingFolder(null);
    }

    private void SetRecordingFolder(string? folder)
    {
        if (string.Equals(_recordingFolder, folder, StringComparison.Ordinal))
        {
            return;
        }

        _recordingFolder = folder;
        OnPropertyChanged(nameof(RecordingFolderText));
        OnPropertyChanged(nameof(HasCustomRecordingFolder));
        ResetRecordingFolderCommand.NotifyCanExecuteChanged();
        UpdateDirty();
    }

    private void SetRecordingFolderError(Func<string>? source)
    {
        _recordingFolderErrorSource = source;
        OnPropertyChanged(nameof(RecordingFolderError));
        OnPropertyChanged(nameof(ShowsRecordingFolderHint));
        OnPropertyChanged(nameof(RecordingFolderHelpText));
    }

    /// <summary>Whether a file can be created in <paramref name="folder"/>: a probe file deleted right away (nothing stays).</summary>
    internal static bool CanWriteTo(string folder)
    {
        try
        {
            if (!Directory.Exists(folder))
            {
                return false;
            }

            string probe = Path.Combine(folder, ".layerpeek-write-test-" + Guid.NewGuid().ToString("N"));
            using FileStream stream = new(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 1, FileOptions.DeleteOnClose);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }
}

/// <summary>An item of "Take a frame" of the timelapse (T-159): every interval or every N-th layer.</summary>
public sealed class TimelapseTriggerOption(CameraTimelapseTriggerKind value, Func<string> text) : LocalizedOption(text)
{
    public CameraTimelapseTriggerKind Value { get; } = value;
}
