using Microsoft.Extensions.Logging;

using LayerPeek.Core.Cameras;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.Cameras;

/// <summary>
/// Saves the device path a saved camera monitor was found at on another connector (user decision 2026-10-23, see
/// <see cref="CameraWindowDefaultPlacement.Relocate"/>).
/// </summary>
internal interface ICameraMonitorPathUpdater
{
    /// <summary>
    /// Saves <paramref name="updated"/> as the camera monitor of <paramref name="printerId"/> while the stored one is still
    /// <paramref name="saved"/>. Thread-safe, returns at once (the save runs in the background) and never throws for
    /// storage failures.
    /// </summary>
    void Request(string printerId, CameraMonitorId saved, CameraMonitorId updated);

    /// <summary>
    /// The device path this run saved for <paramref name="monitor"/> of <paramref name="printerId"/> (the same monitor,
    /// whatever its twins; several moves are followed), or <see langword="null"/>: a settings window opened before the save
    /// saves that path instead of the one it loaded (user decision 2026-10-23). Thread-safe.
    /// </summary>
    string? SavedDevicePath(string printerId, CameraMonitorId monitor);

    /// <summary>
    /// Waits at most <paramref name="timeout"/> for the requested saves (quitting, tests). <see langword="true"/> when none
    /// is running any more. Never throws.
    /// </summary>
    Task<bool> WaitForPendingAsync(TimeSpan timeout);
}

/// <summary>
/// <see cref="ICameraMonitorPathUpdater"/> through <see cref="ISettingsStore.SetCameraMonitorDevicePathAsync"/> (a byte for
/// byte, atomic change with the retries of a file held by another process): silently, once per printer and change, in the background
/// (one save after another), with one Information line when the file was written. A failure is logged as a warning and
/// does not affect the camera window; the next request of the same change (the next time the window opens) tries again.
/// Device paths are never logged (T-071 privacy decision).
/// </summary>
internal sealed partial class CameraMonitorPathUpdater : ICameraMonitorPathUpdater
{
    private readonly Func<ISettingsStore> _settingsStore;
    private readonly ILogger<CameraMonitorPathUpdater> _logger;
    private readonly Lock _gate = new();

    // Guarded by _gate: the changes requested and not failed (saved, saving or found unnecessary), and the last save.
    private readonly HashSet<Change> _requested = [];

    // Guarded by _gate: the changes written to the file, by printer id (trimmed, upper case), oldest first.
    private readonly Dictionary<string, List<Change>> _saved = [];
    private Task _pending = Task.CompletedTask;

    /// <param name="settingsStore">Resolved on each save; may throw (e.g. no per-user data folder).</param>
    public CameraMonitorPathUpdater(Func<ISettingsStore> settingsStore, ILogger<CameraMonitorPathUpdater> logger)
    {
        ArgumentNullException.ThrowIfNull(settingsStore);
        ArgumentNullException.ThrowIfNull(logger);
        _settingsStore = settingsStore;
        _logger = logger;
    }

    public string? SavedDevicePath(string printerId, CameraMonitorId monitor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerId);
        ArgumentNullException.ThrowIfNull(monitor);
        lock (_gate)
        {
            if (!_saved.TryGetValue(printerId.Trim().ToUpperInvariant(), out List<Change>? moves))
            {
                return null;
            }

            // Follows A -> B -> C (the latest move of each monitor); at most one step per saved move, so a cycle ends too.
            CameraMonitorId current = monitor;
            string? path = null;
            for (int step = 0; step < moves.Count; step++)
            {
                Change? next = null;
                foreach (Change move in moves)
                {
                    if (move.Saved.HasSameIdentity(current))
                    {
                        next = move;
                    }
                }

                if (next is not { } found)
                {
                    break;
                }

                path = found.Updated.DevicePath;
                current = found.Updated;
            }

            return path;
        }
    }

    public void Request(string printerId, CameraMonitorId saved, CameraMonitorId updated)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerId);
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(updated);
        Change change = new(printerId.Trim().ToUpperInvariant(), saved, updated);
        lock (_gate)
        {
            if (!_requested.Add(change))
            {
                return;
            }

            Task previous = _pending;
            _pending = Task.Run(() => SaveAsync(previous, printerId, change), CancellationToken.None);
        }
    }

    public async Task<bool> WaitForPendingAsync(TimeSpan timeout)
    {
        Task pending;
        lock (_gate)
        {
            pending = _pending;
        }

        // SaveAsync never faults; only the timeout matters here.
        await pending.WaitAsync(timeout).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        return pending.IsCompleted;
    }

    /// <summary>After the previous save; never faults.</summary>
    private async Task SaveAsync(Task previous, string printerId, Change change)
    {
        await previous.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        try
        {
            if (await _settingsStore().SetCameraMonitorDevicePathAsync(printerId, change.Saved, change.Updated.DevicePath!, CancellationToken.None).ConfigureAwait(false))
            {
                lock (_gate)
                {
                    if (!_saved.TryGetValue(change.PrinterId, out List<Change>? moves))
                    {
                        moves = [];
                        _saved.Add(change.PrinterId, moves);
                    }

                    moves.Add(change);
                }

                LogPathUpdated(printerId);
            }
            else
            {
                LogNothingToUpdate(printerId);
            }
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                _requested.Remove(change);
            }

            LogNotSaved(ex, printerId);
        }
    }

    /// <param name="PrinterId">Trimmed, upper case: ids are compared case-insensitively.</param>
    private readonly record struct Change(string PrinterId, CameraMonitorId Saved, CameraMonitorId Updated);

    [LoggerMessage(Level = LogLevel.Information, Message = "The camera monitor of printer '{PrinterId}' was found on another connector; its saved device path was updated.")]
    private partial void LogPathUpdated(string printerId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The camera monitor of printer '{PrinterId}' was found on another connector, but the settings file was not changed (the monitor changed meanwhile, or the file has problems the settings window shows).")]
    private partial void LogNothingToUpdate(string printerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The new device path of the camera monitor of printer '{PrinterId}' could not be saved; it is tried again the next time the camera window opens.")]
    private partial void LogNotSaved(Exception exception, string printerId);
}
