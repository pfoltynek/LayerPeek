using CommunityToolkit.Mvvm.Input;

using LayerPeek.App.Cameras.Recording;
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
    }

    /// <summary>The recording settings as edited; <see langword="null"/> while the limit is invalid.</summary>
    private CameraRecordingSettings? BuildRecording() =>
        _recordingLimitGigabytes is { } limit && _recordingLimitError is null
            ? new CameraRecordingSettings { Folder = _recordingFolder, SizeLimitGigabytes = (int)limit }
            : null;

    private void ValidateRecording() =>
        RecordingLimitError = _recordingLimitGigabytes is { } limit
            && limit >= CameraRecordingSettings.MinSizeLimitGigabytes
            && limit <= CameraRecordingSettings.MaxSizeLimitGigabytes
            && limit == decimal.Truncate(limit)
            ? null
            : Format(Strings.SettingsErrorRecordingLimit, CameraRecordingSettings.MinSizeLimitGigabytes, CameraRecordingSettings.MaxSizeLimitGigabytes);

    /// <summary>The language changed: texts with a format and the error are formatted again.</summary>
    private void RefreshRecordingTexts()
    {
        OnPropertyChanged(nameof(RecordingLimitHint));
        OnPropertyChanged(nameof(RecordingLimitHelpText));
        OnPropertyChanged(nameof(RecordingFolderError));
        OnPropertyChanged(nameof(RecordingFolderHelpText));
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
