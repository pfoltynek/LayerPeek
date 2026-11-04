using LayerPeek.App.Cameras.Recording;
using LayerPeek.App.Settings;
using LayerPeek.Core.Cameras.Recording;
using LayerPeek.Core.Cameras.Timelapse;

namespace LayerPeek.App.Tests.Cameras.Recording;

/// <summary>
/// The app's recordings without a recorder (T-146): statuses set by the test, commands recorded. <see cref="Changed"/> is
/// raised by <see cref="SetStatus"/> on the calling thread, like the service raises it on any thread.
/// </summary>
internal sealed class FakeCameraRecordings : ICameraRecordings
{
    /// <summary>A fixed default folder, so that snapshots do not show the user name of the machine.</summary>
    public static readonly string FixedDefaultFolder = OperatingSystem.IsWindows() ? @"C:\Users\Me\Videos\LayerPeek" : "/Users/me/Movies/LayerPeek";

    private readonly Dictionary<string, CameraRecordingStatus> _statuses = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();

    public event EventHandler<string>? Changed;

    public bool IsAvailable { get; set; } = true;

    /// <summary>This build can take timelapses (T-161).</summary>
    public bool IsTimelapseAvailable { get; set; } = true;

    /// <summary>The printers whose timelapse was started (T-161).</summary>
    public List<string> TimelapsesStarted { get; } = [];

    /// <summary>The printers whose timelapse was stopped (T-161).</summary>
    public List<string> TimelapsesStopped { get; } = [];

    /// <summary>The printers whose last timelapse was played (T-161).</summary>
    public List<string> TimelapsesPlayed { get; } = [];

    public string DefaultFolder { get; set; } = FixedDefaultFolder;

    public long MinFreeBytes { get; set; } = 5_000_000_000;

    public TimeSpan DashcamPostEventDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>The printers whose dashcam was asked to save (T-148).</summary>
    public List<string> DashcamSaves { get; } = [];

    public List<string> Started { get; } = [];

    public List<string> Stopped { get; } = [];

    public List<string?> OpenedFolders { get; } = [];

    public List<string> OpenedPrinterFolders { get; } = [];

    public List<string> Played { get; } = [];

    /// <summary>How many handlers listen to <see cref="Changed"/>.</summary>
    public int ChangedSubscribers => Changed?.GetInvocationList().Length ?? 0;

    /// <summary>How often <see cref="GetStatus"/> was asked.</summary>
    public int StatusReads { get; private set; }

    public void SetStatus(string printerId, CameraRecordingStatus status, bool raise = true)
    {
        lock (_gate)
        {
            _statuses[printerId] = status;
        }

        if (raise)
        {
            Changed?.Invoke(this, printerId);
        }
    }

    public void SetActivity(string printerId, CameraRecordingActivity activity, string? lastFile = null, bool raise = true) =>
        SetStatus(printerId, new CameraRecordingStatus
        {
            Activity = activity,
            Mode = activity == CameraRecordingActivity.Idle ? null : CameraRecordingMode.Manual,
            LastFile = lastFile,
            CanRecord = true,
        }, raise);

    public void Raise(string printerId) => Changed?.Invoke(this, printerId);

    public CameraRecordingStatus GetStatus(string printerId)
    {
        lock (_gate)
        {
            StatusReads++;
            return _statuses.GetValueOrDefault(printerId) ?? CameraRecordingStatus.None;
        }
    }

    public bool Start(string printerId)
    {
        Started.Add(printerId);
        return true;
    }

    public void Stop(string printerId) => Stopped.Add(printerId);

    public bool SaveDashcam(string printerId)
    {
        DashcamSaves.Add(printerId);
        return GetStatus(printerId).Dashcam == CameraDashcamActivity.Buffering;
    }

    /// <summary>The dashcam of <paramref name="printerId"/> (T-148) does <paramref name="activity"/>; the recording stays as it was.</summary>
    public void SetDashcam(string printerId, CameraDashcamActivity activity, int minutes = 2, bool raise = true)
    {
        CameraRecordingStatus status;
        lock (_gate)
        {
            status = (_statuses.GetValueOrDefault(printerId) ?? new CameraRecordingStatus { CanRecord = true }) with { Dashcam = activity, DashcamMinutes = minutes };
        }

        SetStatus(printerId, status, raise);
    }

    /// <summary>
    /// The timelapse of <paramref name="printerId"/> (T-161) does <paramref name="activity"/> (capturing unless
    /// <paramref name="capturing"/> says otherwise); the recording and the dashcam stay as they were.
    /// </summary>
    public void SetTimelapse(string printerId, CameraRecordingActivity activity, bool capturing = true, string? lastFile = null, bool raise = true)
    {
        CameraRecordingStatus status;
        lock (_gate)
        {
            status = _statuses.GetValueOrDefault(printerId) ?? new CameraRecordingStatus { CanRecord = true };
            status = status with
            {
                Timelapse = status.Timelapse with
                {
                    Activity = activity,
                    Mode = activity == CameraRecordingActivity.Idle ? null : CameraRecordingMode.Manual,
                    IsCapturing = activity != CameraRecordingActivity.Idle && capturing,
                    LastFile = lastFile,
                },
            };
        }

        SetStatus(printerId, status, raise);
    }

    public bool StartTimelapse(string printerId)
    {
        TimelapsesStarted.Add(printerId);
        return true;
    }

    public void StopTimelapse(string printerId) => TimelapsesStopped.Add(printerId);

    public Task<bool> PlayLastTimelapseAsync(string printerId)
    {
        TimelapsesPlayed.Add(printerId);
        return Task.FromResult(true);
    }

    public Task<bool> OpenFolderAsync(string? folder = null)
    {
        OpenedFolders.Add(folder);
        return Task.FromResult(true);
    }

    public Task<bool> OpenPrinterFolderAsync(string printerId)
    {
        OpenedPrinterFolders.Add(printerId);
        return Task.FromResult(true);
    }

    public Task<bool> PlayLastAsync(string printerId)
    {
        Played.Add(printerId);
        return Task.FromResult(true);
    }

    public event EventHandler? TimelapseExportChanged;

    /// <summary>This build can export timelapses as JPEG (T-165).</summary>
    public bool IsTimelapseExportAvailable { get; set; } = true;

    public CameraTimelapseExportStatus TimelapseExport { get; private set; } = CameraTimelapseExportStatus.Idle;

    /// <summary>The printers whose menu asked for the JPEG export (T-165).</summary>
    public List<string> TimelapseExportsRequested { get; } = [];

    public int TimelapseExportCancels { get; private set; }

    /// <summary>The JPEG export (T-165) runs with <paramref name="status"/>; <see cref="TimelapseExportChanged"/> is raised on the calling thread.</summary>
    public void SetTimelapseExport(CameraTimelapseExportStatus status)
    {
        TimelapseExport = status;
        TimelapseExportChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task ExportTimelapseToJpegAsync(string printerId)
    {
        TimelapseExportsRequested.Add(printerId);
        return Task.CompletedTask;
    }

    public void CancelTimelapseExport() => TimelapseExportCancels++;
}

/// <summary>The folder dialog with a scripted answer (T-146).</summary>
internal sealed class FakeRecordingFolderPicker : IRecordingFolderPicker
{
    /// <summary>What the next dialog returns; <see langword="null"/>: the user cancels.</summary>
    public PickedFolder? Answer { get; set; }

    public List<string?> StartDirectories { get; } = [];

    public Task<PickedFolder?> PickAsync(string? startDirectory)
    {
        StartDirectories.Add(startDirectory);
        return Task.FromResult(Answer);
    }
}

/// <summary>The open dialog for a recording file (T-165) with a scripted answer; can hold the answer back.</summary>
internal sealed class FakeRecordingFilePicker : IRecordingFilePicker
{
    /// <summary>What the next dialog returns; <see langword="null"/>: the user cancels.</summary>
    public string? Answer { get; set; }

    /// <summary>Awaited before the answer (a dialog that stays open); <see langword="null"/>: answers at once.</summary>
    public Task? OpenUntil { get; set; }

    public List<RecordingFilePickRequest> Requests { get; } = [];

    public async Task<string?> PickAsync(RecordingFilePickRequest request)
    {
        Requests.Add(request);
        if (OpenUntil is { } open)
        {
            await open.ConfigureAwait(true);
        }

        return Answer;
    }
}
