using LayerPeek.App.Cameras.Recording;
using LayerPeek.App.Settings;
using LayerPeek.Core.Cameras.Recording;

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

    public string DefaultFolder { get; set; } = FixedDefaultFolder;

    public long MinFreeBytes { get; set; } = 5_000_000_000;

    public List<string> Started { get; } = [];

    public List<string> Stopped { get; } = [];

    public List<string?> OpenedFolders { get; } = [];

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

    public Task<bool> OpenFolderAsync(string? folder = null)
    {
        OpenedFolders.Add(folder);
        return Task.FromResult(true);
    }

    public Task<bool> PlayLastAsync(string printerId)
    {
        Played.Add(printerId);
        return Task.FromResult(true);
    }
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
