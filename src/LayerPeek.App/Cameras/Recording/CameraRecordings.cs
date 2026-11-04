using System.Globalization;

using LayerPeek.App.Platform;
using LayerPeek.Core.Cameras.Recording;

namespace LayerPeek.App.Cameras.Recording;

/// <summary>
/// What the UI of the recordings (T-146) needs from <see cref="CameraRecordingService"/> (T-145): the state of a printer's
/// recording, manual start and stop, and opening the recordings in the system's apps. Thread-safe; <see cref="Changed"/>
/// comes on any thread (subscribers marshal to the UI thread themselves).
/// </summary>
internal interface ICameraRecordings
{
    /// <summary>The recording state of a printer changed (id as configured); read it with <see cref="GetStatus"/>.</summary>
    event EventHandler<string>? Changed;

    /// <summary>This build can record (T-144: the MP4 muxer is there); otherwise every recording option is disabled.</summary>
    bool IsAvailable { get; }

    /// <summary>The folder recordings go to while the settings name none (the system's video folder + <c>LayerPeek</c>).</summary>
    string DefaultFolder { get; }

    /// <summary>Below this free space recording does not start or stops (T-145: 5 GB).</summary>
    long MinFreeBytes { get; }

    /// <summary>How long a dashcam save goes on after the event (T-148: 30 s).</summary>
    TimeSpan DashcamPostEventDuration { get; }

    /// <inheritdoc cref="CameraRecordingService.GetStatus"/>
    CameraRecordingStatus GetStatus(string printerId);

    /// <inheritdoc cref="CameraRecordingService.Start"/>
    bool Start(string printerId);

    /// <inheritdoc cref="CameraRecordingService.Stop"/>
    void Stop(string printerId);

    /// <summary>"Save the last minutes" (T-148): the dashcam of <paramref name="printerId"/> saves now (manual trigger).</summary>
    /// <returns><see langword="false"/> when its dashcam does not buffer.</returns>
    bool SaveDashcam(string printerId);

    /// <summary>
    /// Shows <paramref name="folder"/> (default: the folder of the running settings) in Explorer / Finder, created when it
    /// does not exist yet. Never throws.
    /// </summary>
    Task<bool> OpenFolderAsync(string? folder = null);

    /// <summary>
    /// "Open recordings folder" of a printer's submenu (user decision 2026-11-04): the printer's own subfolder when it exists,
    /// otherwise the folder of the running settings like <see cref="OpenFolderAsync"/> (created when missing). Never throws.
    /// </summary>
    Task<bool> OpenPrinterFolderAsync(string printerId);

    /// <summary>Opens the newest completed recording of <paramref name="printerId"/> in the system's default app. Never throws.</summary>
    Task<bool> PlayLastAsync(string printerId);
}

/// <summary><see cref="ICameraRecordings"/> over the app's <see cref="CameraRecordingService"/> and the system launchers.</summary>
internal sealed class CameraRecordings : ICameraRecordings
{
    private readonly CameraRecordingService _service;
    private readonly IFolderLauncher _folders;
    private readonly IFileLauncher _files;

    /// <param name="options">The options of <paramref name="service"/>: its default root and the free space it needs.</param>
    public CameraRecordings(CameraRecordingService service, IFolderLauncher folders, IFileLauncher files, CameraRecordingServiceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(files);
        _service = service;
        _folders = folders;
        _files = files;
        DefaultFolder = options.DefaultRoot;
        MinFreeBytes = options.MinFreeBytes;
        DashcamPostEventDuration = options.DashcamPostEventDuration;
    }

    public event EventHandler<string>? Changed
    {
        add => _service.Changed += value;
        remove => _service.Changed -= value;
    }

    public bool IsAvailable => _service.Availability.IsAvailable;

    public string DefaultFolder { get; }

    public long MinFreeBytes { get; }

    public TimeSpan DashcamPostEventDuration { get; }

    public CameraRecordingStatus GetStatus(string printerId) => _service.GetStatus(printerId);

    public bool Start(string printerId) => _service.Start(printerId);

    public void Stop(string printerId) => _service.Stop(printerId);

    public bool SaveDashcam(string printerId) => _service.SaveDashcam(printerId);

    public Task<bool> OpenFolderAsync(string? folder = null)
    {
        string target = folder ?? _service.RecordingsFolder;
        return Path.IsPathFullyQualified(target) ? _folders.OpenAsync(target) : Task.FromResult(false);
    }

    public async Task<bool> OpenPrinterFolderAsync(string printerId)
    {
        // The service names the folder (no path rules of its own here); asking the disk can take a moment (a network share),
        // so never on the UI thread.
        if (_service.GetPrinterFolder(printerId) is { } folder && Path.IsPathFullyQualified(folder)
            && await Task.Run(() => Directory.Exists(folder)).ConfigureAwait(false))
        {
            return await _folders.OpenAsync(folder).ConfigureAwait(false);
        }

        return await OpenFolderAsync().ConfigureAwait(false);
    }

    public Task<bool> PlayLastAsync(string printerId) =>
        _service.GetStatus(printerId).LastFile is { } file ? _files.OpenAsync(file) : Task.FromResult(false);
}

/// <summary>
/// What the Start / Stop control of a printer's recording shows (tray menu item, camera window button): one place for the
/// rules, so that both say the same. A control acts on what it shows: Start while idle, Stop while recording, nothing while
/// the recording starts or stops (T-145 review: a Start during "stopping" would be lost silently).
/// </summary>
/// <remarks>Only printers with an enabled camera have the control, so it does not ask whether the printer can record.</remarks>
internal readonly record struct RecordingControlState(bool IsAvailable, CameraRecordingActivity Activity)
{
    public static RecordingControlState Of(ICameraRecordings recordings, string printerId)
    {
        ArgumentNullException.ThrowIfNull(recordings);
        return new(recordings.IsAvailable, recordings.GetStatus(printerId).Activity);
    }

    /// <summary>Starting, recording or stopping: the REC indicator shows, and the control reads "Stop recording".</summary>
    public bool IsActive => Activity != CameraRecordingActivity.Idle;

    /// <summary>The control can be used now (it starts while idle, stops while recording).</summary>
    public bool IsEnabled => Activity switch
    {
        CameraRecordingActivity.Idle => IsAvailable,
        CameraRecordingActivity.Recording => true,
        _ => false,
    };

    /// <summary>
    /// Why the control is disabled (its tooltip); <see langword="null"/> while it is enabled (the menu item needs no tip, the
    /// window button has <see cref="ButtonToolTip"/>).
    /// </summary>
    public string? DisabledReason => Activity switch
    {
        CameraRecordingActivity.Starting => Resources.Strings.RecordingStarting,
        CameraRecordingActivity.Stopping => Resources.Strings.RecordingStopping,
        CameraRecordingActivity.Idle when !IsAvailable => Resources.Strings.RecordingUnavailable,
        _ => null,
    };

    /// <summary>The tip (and accessible name) of the camera window button.</summary>
    public string ButtonToolTip => DisabledReason ?? (IsActive ? Resources.Strings.RecordingStop : Resources.Strings.RecordingStart);
}

/// <summary>
/// What "Save the last N min" of a printer shows (T-148; tray menu item, camera window context menu): one place for the
/// rules, so that both say the same. Shown only for printers with a dashcam; enabled while it buffers; otherwise disabled
/// with the reason (saving now, the whole print is recorded, or no print).
/// </summary>
internal readonly record struct DashcamControlState(CameraDashcamActivity Activity, bool WholePrintRecorded, int Minutes)
{
    public static DashcamControlState Of(ICameraRecordings recordings, string printerId)
    {
        ArgumentNullException.ThrowIfNull(recordings);
        CameraRecordingStatus status = recordings.GetStatus(printerId);
        return new(status.Dashcam, status.IsActive, status.DashcamMinutes);
    }

    /// <summary>The printer has a dashcam: the control is there.</summary>
    public bool IsShown => Activity != CameraDashcamActivity.Off;

    /// <summary>The last minutes are held: a click saves them.</summary>
    public bool IsEnabled => Activity == CameraDashcamActivity.Buffering;

    /// <summary>"Save the last 2 min", with its access key.</summary>
    public string Text => Format(Resources.Strings.MenuSaveDashcam, Minutes);

    /// <summary>Why the control is disabled (its tooltip); <see langword="null"/> while it is enabled or not shown.</summary>
    public string? DisabledReason => Activity switch
    {
        CameraDashcamActivity.Saving => Resources.Strings.DashcamSaving,
        CameraDashcamActivity.Waiting when WholePrintRecorded => Resources.Strings.DashcamWholePrintRecorded,
        CameraDashcamActivity.Waiting => Resources.Strings.DashcamNotPrinting,
        _ => null,
    };

    private static string Format(string format, int value) => string.Format(CultureInfo.CurrentCulture, format, value);
}
