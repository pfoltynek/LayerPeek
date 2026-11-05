using Microsoft.Extensions.Logging;

using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Recording;
using LayerPeek.Core.Monitoring;
using LayerPeek.Core.Notifications;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

namespace LayerPeek.Core.Detection;

/// <summary>
/// Watches printing printers through their cameras for print failures (T-153, user decisions 2026-11-04), with the
/// detectors registered by the composition root (<see cref="PrintFailureDetectorRegistration"/>, T-155):
/// <list type="bullet">
/// <item><b>When:</b> only while a printer is <see cref="PrinterState.Printing"/>, its camera is enabled, detection is on
/// globally and for the printer and at least one detector is on. Otherwise (idle, paused, waiting for the user, offline,
/// finished) it holds no camera lease, nothing is decoded or analysed for it and no timer or task exists.</item>
/// <item><b>Samples:</b> a lease of key frames only (<see cref="CameraFrameSubscriptionOptions.KeyFramesOnly"/>), one per
/// <see cref="PrintFailureDetectionSettings.SampleInterval"/>, downscaled into 8-bit grayscale of the detectors'
/// <see cref="DetectionFrameSpec"/> while the hub copies it. Without a camera window only those key frames are decoded.
/// The camera window does not have to be open.</item>
/// <item><b>Analysis:</b> on the thread pool, never on the decoder or UI thread; a slow detector only drops samples (the
/// hub keeps the newest). Every start, resume, camera reconnect and the light coming back reset the detectors; while a
/// detector is not warmed up, no alarm. A dark region (<see cref="PrintFailureDetectionOptions.DarkBrightness"/>, chamber
/// light off) pauses the analysis. A detector that throws is switched off for the rest of the print, the others go on.</item>
/// <item><b>Alarm:</b> a score at or above the threshold of the sensitivity (<see cref="DetectionThresholds"/>) in the
/// share <see cref="PrintFailureDetectionOptions.ConfirmationRatio"/> of the samples of the last
/// <see cref="PrintFailureDetectionOptions.ConfirmationWindow"/>; once per print and kind, or again every
/// <see cref="PrintFailureDetectionOptions.RepeatInterval"/> while it lasts (<see cref="PrintFailureAlarmRepeat"/>). It
/// asks the dashcam to save the minutes around it (<see cref="CameraDashcamTrigger.FailureDetected"/>, T-148 / T-155),
/// takes a full-size snapshot from the next key frame (a short lease of its own), encodes it as JPEG
/// (<see cref="ICameraImageEncoder"/>), saves it into the printer's recording folder when
/// <see cref="PrintFailureDetectionSettings.SaveSnapshots"/> (counted in the recordings' size limit), and then raises
/// <see cref="AlarmRaised"/> and shows a notification (<see cref="INotificationService.ShowPrintFailure"/>).</item>
/// <item><b>False alarm:</b> <see cref="ReportFalseAlarmAsync"/> keeps a local record (values and snapshot); the
/// sensitivity is not changed.</item>
/// </list>
/// Thread-safe: snapshots arrive concurrently from the monitor, settings from the monitoring session; the state is kept
/// under one lock that is never held while a detector runs or a file is written. Never throws for runtime conditions.
/// </summary>
public sealed partial class PrintFailureDetectionService : IAsyncDisposable, IDisposable
{
    /// <summary>Alarms kept for <see cref="TryGetAlarm"/> (the "False alarm" action of a notification, T-154).</summary>
    public const int RecentAlarmCount = 8;

    private readonly IPrinterMonitor _monitor;
    private readonly CameraStreamHub _hub;
    private readonly PrintFailureDetectorRegistration[] _registrations;
    private readonly CameraGrayscaleSize _sampleSize;
    private readonly ICameraRecordingsFolder _recordings;
    private readonly PrintFailureAlarmFiles _files;
    private readonly ICameraImageEncoder _encoder;
    private readonly INotificationService _notifications;
    private readonly TimeProvider _time;
    private readonly ILogger<PrintFailureDetectionService> _logger;
    private readonly PrintFailureDetectionOptions _options;
    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _gate = new();

    // Guarded by _gate.
    private Dictionary<string, Session> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Task> _work = [];
    private readonly Queue<PrintFailureAlarm> _recent = new(RecentAlarmCount);
    private PrintFailureDetectionSettings _settings = PrintFailureDetectionSettings.Default;
    private NotificationSettings _notificationSettings = new();
    private Task? _stopped;

    /// <param name="detectors">The detectors the app offers, in display order; unique ids, one common <see cref="DetectionFrameSpec"/>.</param>
    /// <exception cref="ArgumentException">Duplicate detector ids, or different frame specs.</exception>
    public PrintFailureDetectionService(
        IPrinterMonitor monitor,
        CameraStreamHub hub,
        IReadOnlyList<PrintFailureDetectorRegistration> detectors,
        ICameraRecordingsFolder recordings,
        PrintFailureAlarmFiles files,
        ICameraImageEncoder encoder,
        INotificationService notifications,
        TimeProvider timeProvider,
        ILogger<PrintFailureDetectionService> logger,
        PrintFailureDetectionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(detectors);
        ArgumentNullException.ThrowIfNull(recordings);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(encoder);
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _registrations = [.. detectors];
        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (PrintFailureDetectorRegistration registration in _registrations)
        {
            ArgumentNullException.ThrowIfNull(registration, nameof(detectors));
            if (!ids.Add(registration.Id))
            {
                throw new ArgumentException($"Detector id '{registration.Id}' is registered twice.", nameof(detectors));
            }

            if (registration.FrameSpec != _registrations[0].FrameSpec)
            {
                // One sample stream per printer; detectors of other sizes would need a lease each (not needed yet).
                throw new ArgumentException("All detectors must analyse the same frame spec.", nameof(detectors));
            }
        }

        _sampleSize = _registrations.Length > 0 ? _registrations[0].FrameSpec.Size : default;
        _monitor = monitor;
        _hub = hub;
        _recordings = recordings;
        _files = files;
        _encoder = encoder;
        _notifications = notifications;
        _time = timeProvider;
        _logger = logger;
        _options = options ?? PrintFailureDetectionOptions.Default;
        _monitor.SnapshotUpdated += OnSnapshotUpdated;
    }

    /// <summary>
    /// A print failure was confirmed (after its snapshot was saved or given up), on a thread-pool thread; also shown as a
    /// notification. A failing subscriber is logged.
    /// </summary>
    public event EventHandler<PrintFailureAlarm>? AlarmRaised;

    /// <summary>The detectors the app offers (for the settings, T-154).</summary>
    public IReadOnlyList<PrintFailureDetectorRegistration> Detectors => _registrations;

    /// <summary>
    /// Watches <paramref name="settings"/>' printers with its detection settings from now on. Call before every monitoring
    /// run and whenever the settings change. A printer whose camera, region, interval or detector settings changed starts
    /// over (its detectors are reset); a removed printer or a switched-off detection stops at once.
    /// </summary>
    public void Configure(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        IReadOnlyDictionary<string, string> folders = CameraRecordingFolders.PrinterFolderNames(settings.Printers);
        lock (_gate)
        {
            if (_stopped is not null)
            {
                return;
            }

            _settings = settings.Detection;
            _notificationSettings = settings.Notifications;
            Dictionary<string, Session> sessions = new(settings.Printers.Count, StringComparer.OrdinalIgnoreCase);
            foreach (PrinterDefinition printer in settings.Printers)
            {
                if (sessions.ContainsKey(printer.Id))
                {
                    continue;
                }

                if (!_sessions.Remove(printer.Id, out Session? session))
                {
                    session = new Session(printer.Id);
                    session.Snapshot = _monitor.TryGetSnapshot(printer.Id, out PrinterSnapshot? latest) ? latest : null;
                }

                session.Printer = printer;
                session.FolderName = folders[printer.Id];
                session.Config = DesiredConfig(printer);
                // A lost camera may be back with the new settings.
                session.CameraLost = false;
                sessions.Add(printer.Id, session);
            }

            foreach (Session removed in _sessions.Values)
            {
                StopRun(removed);
            }

            _sessions = sessions;
            foreach (Session session in sessions.Values)
            {
                Evaluate(session);
            }
        }
    }

    /// <summary>
    /// Monitoring stopped (it failed): whether the printers still print is unknown, so every watch stops until the next
    /// snapshot says otherwise. The alarms of the current prints are kept.
    /// </summary>
    public void ForgetSnapshots()
    {
        lock (_gate)
        {
            foreach (Session session in _sessions.Values)
            {
                session.Snapshot = null;
                StopRun(session);
            }
        }
    }

    /// <summary>Whether the printer is being watched right now (it holds a camera lease for samples).</summary>
    public bool IsWatching(string printerId)
    {
        ArgumentNullException.ThrowIfNull(printerId);
        lock (_gate)
        {
            return _sessions.TryGetValue(printerId, out Session? session) && session.Run is not null;
        }
    }

    /// <summary>
    /// One of the last <see cref="RecentAlarmCount"/> alarms of this run of the app by <see cref="PrintFailureAlarm.Id"/>,
    /// e.g. for the "False alarm" action of its notification (which carries only the id).
    /// </summary>
    public bool TryGetAlarm(string alarmId, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out PrintFailureAlarm? alarm)
    {
        ArgumentNullException.ThrowIfNull(alarmId);
        lock (_gate)
        {
            foreach (PrintFailureAlarm recent in _recent)
            {
                if (string.Equals(recent.Id, alarmId, StringComparison.Ordinal))
                {
                    alarm = recent;
                    return true;
                }
            }
        }

        alarm = null;
        return false;
    }

    /// <summary>
    /// "False alarm" (T-154 calls it): saves a local record of <paramref name="alarm"/> for tuning the detectors (its values
    /// and snapshot, <see cref="PrintFailureAlarmFiles.SaveFalseAlarm"/>). Nothing is sent anywhere and nothing changes.
    /// </summary>
    /// <returns>The path of the record; <see langword="null"/> when it could not be saved.</returns>
    public Task<string?> ReportFalseAlarmAsync(PrintFailureAlarm alarm, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(alarm);
        LogFalseAlarm(alarm.PrinterId, alarm.DetectorId, alarm.Score);
        return Task.Run(() => _files.SaveFalseAlarm(alarm), cancellationToken);
    }

    /// <summary>Stops watching (ends every lease) and waits until running alarms completed.</summary>
    public ValueTask DisposeAsync() => new(Stop());

    /// <summary>Stops watching without waiting for running alarms.</summary>
    public void Dispose() => _ = Stop();

    private Task Stop()
    {
        Task stopped;
        lock (_gate)
        {
            if (_stopped is { } running)
            {
                return running;
            }

            _monitor.SnapshotUpdated -= OnSnapshotUpdated;
            foreach (Session session in _sessions.Values)
            {
                StopRun(session);
            }

            _sessions.Clear();
            stopped = Task.WhenAll(_work);
            _stopped = stopped;
        }

        // Ends the snapshot waits of running alarms; the CTS stays (a late alarm task may still read its token).
        _stop.Cancel();
        return stopped;
    }

    // Monitor thread.
    private void OnSnapshotUpdated(object? sender, PrinterSnapshot snapshot)
    {
        lock (_gate)
        {
            if (_stopped is null && _sessions.TryGetValue(snapshot.PrinterId, out Session? session))
            {
                session.Snapshot = snapshot;
                Evaluate(session);
            }
        }
    }

    /// <summary>Under the lock: starts, keeps, restarts or stops the printer's run after its snapshot or settings changed.</summary>
    private void Evaluate(Session session)
    {
        PrinterSnapshot? snapshot = session.Snapshot;
        if (session.Run is { EndReason: { } ended })
        {
            // The hub ended the lease: a lost camera waits for new settings, a failed lease starts again below.
            StopRun(session);
            session.CameraLost |= ended is CameraStreamEndReason.CameraUnavailable or CameraStreamEndReason.HubStopped;
        }

        RunConfig? config = snapshot is { State: PrinterState.Printing } && !session.CameraLost ? session.Config : null;
        if (snapshot is null || config is null)
        {
            StopRun(session);
            if (snapshot?.State is PrinterState.Idle or PrinterState.Finished or PrinterState.Stopped)
            {
                // The print is over: its alarms are forgotten (a pause, an error or going offline keep them).
                session.ForgetAlarms();
            }

            return;
        }

        JobKey job = JobKey.Of(snapshot.Job);
        if (session.AlarmJob != job)
        {
            session.ForgetAlarms();
            session.AlarmJob = job;
        }

        if (session.Run is { } run)
        {
            if (run.Config == config && run.Job == job)
            {
                run.Snapshot = snapshot;
                return;
            }

            StopRun(session);
        }

        StartRun(session, config, snapshot, job);
    }

    /// <summary>
    /// Under the lock, once per settings change: what a run of <paramref name="printer"/> is made of, or
    /// <see langword="null"/> when it is not watched (so a snapshot costs no allocation).
    /// </summary>
    private RunConfig? DesiredConfig(PrinterDefinition printer)
    {
        if (!_settings.Enabled || !printer.Detection.Enabled || printer.Camera is not { Enabled: true, StreamUri: { } uri })
        {
            return null;
        }

        List<string>? enabled = null;
        foreach (PrintFailureDetectorRegistration registration in _registrations)
        {
            DetectorSettings settings = printer.Detection.For(registration.Id);
            if (settings.Enabled)
            {
                (enabled ??= []).Add($"{registration.Id}:{settings.Sensitivity}");
            }
        }

        return enabled is null ? null : new RunConfig(uri, printer.Detection.RegionFor(printer), _settings.SampleInterval, string.Join(';', enabled), printer.Detection.DarkFilament);
    }

    /// <summary>Under the lock (the hub never calls back into this lock).</summary>
    private void StartRun(Session session, RunConfig config, PrinterSnapshot snapshot, JobKey job)
    {
        PrinterDefinition printer = session.Printer!;
        List<ActiveDetector> detectors = [];
        foreach (PrintFailureDetectorRegistration registration in _registrations)
        {
            DetectorSettings settings = printer.Detection.For(registration.Id);
            if (!settings.Enabled)
            {
                continue;
            }

            try
            {
                IPrintFailureDetector detector = registration.Create() ?? throw new InvalidOperationException("The detector factory returned nothing.");
                ConfirmationWindow window = new(
                    (long)(_options.ConfirmationWindow.TotalSeconds * _time.TimestampFrequency),
                    _options.ConfirmationRatio,
                    _options.MinConfirmationSamples);
                detectors.Add(new ActiveDetector(registration, detector, settings.Sensitivity, registration.Thresholds.ThresholdFor(settings.Sensitivity), window));
            }
            catch (Exception ex)
            {
                LogDetectorFailed(ex, printer.Id, registration.Id);
            }
        }

        if (detectors.Count == 0)
        {
            return;
        }

        Run run = new(this, session, config, job, [.. detectors], snapshot, _time.GetTimestamp());
        CameraFrameSubscription subscription = _hub.SubscribeFrames(printer.Id, options: new CameraFrameSubscriptionOptions
        {
            KeyFramesOnly = true,
            MinFrameInterval = config.Interval,
            Grayscale = _sampleSize,
            FrameAvailable = run.Schedule,
            Status = run,
            Ended = run.OnEnded,
        });
        if (subscription.IsEnded)
        {
            // Refused at once: the hub has no camera for the printer (yet); the next settings try again.
            session.CameraLost = true;
            return;
        }

        run.Subscription = subscription;
        session.Run = run;
        // A sample may have been published before the run knew its lease: that work item found no subscription and left
        // the exchange's notification pending, so no later sample would ever notify. One check now takes it (or nothing).
        run.Schedule();
        LogWatching(printer.Id, detectors.Count, config.Interval.TotalSeconds);
    }

    /// <summary>Under the lock: ends the run's lease; a sample being analysed right now completes but raises nothing.</summary>
    private void StopRun(Session session)
    {
        if (session.Run is not { } run)
        {
            return;
        }

        session.Run = null;
        run.Stop();
        LogStopped(session.PrinterId);
    }

    /// <summary>Worker thread (holding the run's lock): a detector confirmed a failure; raises the alarm unless it is not due.</summary>
    private void OnConfirmed(Run run, ActiveDetector detector, float score, in PrintFailureContext context, PrinterSnapshot snapshot, long timestamp)
    {
        Session session = run.Session;
        PrintFailureAlarm alarm;
        string root;
        string folder;
        bool save;
        lock (_gate)
        {
            if (_stopped is not null || !ReferenceEquals(session.Run, run) || session.Printer is not { } printer)
            {
                return;
            }

            if (session.LastAlarm.TryGetValue(detector.Registration.Id, out long last)
                && (_settings.AlarmRepeat == PrintFailureAlarmRepeat.OncePerPrint || _time.GetElapsedTime(last, timestamp) < _options.RepeatInterval))
            {
                return;
            }

            session.LastAlarm[detector.Registration.Id] = timestamp;
            alarm = new PrintFailureAlarm
            {
                Id = Guid.NewGuid().ToString("N"),
                PrinterId = printer.Id,
                PrinterName = printer.Name,
                DetectorId = detector.Registration.Id,
                Timestamp = _time.GetLocalNow(),
                Score = score,
                Threshold = detector.Threshold,
                Sensitivity = detector.Sensitivity,
                Context = context,
                FileName = snapshot.Job?.FileName,
                RecentScores = detector.Window.RecentScores(timestamp),
            };
            root = _recordings.RecordingsFolder;
            folder = session.FolderName;
            save = _settings.SaveSnapshots;
            Track(Task.Run(() => RaiseAsync(alarm, root, folder, save), CancellationToken.None));
        }

        LogConfirmed(alarm.PrinterId, alarm.DetectorId, score, detector.Threshold);
    }

    /// <summary>
    /// T-148 / T-155: the dashcam keeps the minutes before the failure and the time after it (unless the user switched
    /// that trigger off or the printer has no dashcam). Off the service's lock: the recording service takes its own.
    /// Never throws.
    /// </summary>
    private void SaveDashcam(string printerId)
    {
        try
        {
            if (_recordings.SaveDashcam(printerId, CameraDashcamTrigger.FailureDetected))
            {
                LogDashcamSaving(printerId);
            }
        }
        catch (Exception ex)
        {
            // Defensive: the recordings promise not to throw, and a failed save must not cost the alarm.
            LogDashcamFailed(ex, printerId);
        }
    }

    /// <summary>Under the lock: keeps <paramref name="task"/> until it completed, for <see cref="DisposeAsync"/>.</summary>
    private void Track(Task task)
    {
        _work.Add(task);
        _ = task.ContinueWith(
            static (done, state) =>
            {
                PrintFailureDetectionService service = (PrintFailureDetectionService)state!;
                lock (service._gate)
                {
                    service._work.Remove(done);
                }
            },
            this,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>Thread pool: dashcam, snapshot, file, event and notification of one alarm; never throws.</summary>
    private async Task RaiseAsync(PrintFailureAlarm alarm, string root, string folder, bool save)
    {
        try
        {
            // First, so that the dashcam's save starts at once (the snapshot waits for a key frame).
            SaveDashcam(alarm.PrinterId);
            byte[] jpeg = await CaptureSnapshotAsync(alarm.PrinterId).ConfigureAwait(false);
            string? path = null;
            if (save && jpeg.Length > 0)
            {
                path = _files.SaveSnapshot(root, folder, alarm.Timestamp, alarm.DetectorId, jpeg);
                if (path is not null)
                {
                    // Counted in the size limit of the recordings at once.
                    _recordings.RequestRotation();
                }
            }

            alarm = alarm with { SnapshotPath = path, SnapshotJpeg = jpeg };
            NotificationSettings notifications;
            lock (_gate)
            {
                if (_stopped is not null)
                {
                    return;
                }

                notifications = _notificationSettings;
                if (_recent.Count == RecentAlarmCount)
                {
                    _recent.Dequeue();
                }

                _recent.Enqueue(alarm);
            }

            try
            {
                AlarmRaised?.Invoke(this, alarm);
            }
            catch (Exception ex)
            {
                LogSubscriberFailed(ex);
            }

            if (notifications.Enabled)
            {
                _notifications.ShowPrintFailure(new PrintFailureNotification(alarm, notifications.ShouldPlaySound(NotificationSeverity.ActionRequired)));
            }
        }
        catch (Exception ex)
        {
            // The notification service may throw; nothing must escape a background task.
            LogAlarmFailed(ex, alarm.PrinterId);
        }
    }

    /// <summary>
    /// A full-size snapshot of the printer's camera from its next key frame (a short lease of its own; the gray samples have
    /// too few pixels), as JPEG; empty when none came within <see cref="PrintFailureDetectionOptions.SnapshotTimeout"/>.
    /// </summary>
    private async Task<byte[]> CaptureSnapshotAsync(string printerId)
    {
        TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using CameraFrameSubscription lease = _hub.SubscribeFrames(printerId, options: new CameraFrameSubscriptionOptions
        {
            KeyFramesOnly = true,
            FrameAvailable = () => ready.TrySetResult(),
            Ended = _ => ready.TrySetResult(),
        });
        if (lease.IsEnded)
        {
            // No camera any more (switched off meanwhile) or the app quits.
            return [];
        }

        try
        {
            await ready.Task.WaitAsync(_options.SnapshotTimeout, _time, _stop.Token).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            LogNoSnapshot(printerId);
            return [];
        }
        catch (OperationCanceledException)
        {
            return [];
        }

        if (lease.TakeLatest() is not { } frame)
        {
            LogNoSnapshot(printerId);
            return [];
        }

        try
        {
            // Straight from the lease's buffer: it stays valid until the lease is disposed.
            return _encoder.EncodeJpeg(frame.Pixels, frame.Width, frame.Height, frame.Stride, _options.SnapshotQuality);
        }
        catch (Exception ex)
        {
            LogEncodeFailed(ex, printerId);
            return [];
        }
    }

    /// <summary>Mean luma of the region (0–255).</summary>
    private static int MeanBrightness(ReadOnlySpan<byte> pixels, int stride, DetectionPixelRegion region)
    {
        long sum = 0;
        for (int y = region.Y; y < region.Bottom; y++)
        {
            foreach (byte value in pixels.Slice((y * stride) + region.X, region.Width))
            {
                sum += value;
            }
        }

        return (int)(sum / region.Area);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Print failure detection watches printer '{PrinterId}' ({Detectors} detector(s), a sample every {IntervalSeconds} s).")]
    private partial void LogWatching(string printerId, int detectors, double intervalSeconds);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Print failure detection of printer '{PrinterId}' stopped.")]
    private partial void LogStopped(string printerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Print failure suspected at printer '{PrinterId}' by detector '{DetectorId}' (score {Score:0.0000}, threshold {Threshold:0.0000}).")]
    private partial void LogConfirmed(string printerId, string detectorId, float score, float threshold);

    [LoggerMessage(Level = LogLevel.Information, Message = "The dashcam of printer '{PrinterId}' saves the minutes around a print failure alarm.")]
    private partial void LogDashcamSaving(string printerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "The dashcam of printer '{PrinterId}' could not be saved for a print failure alarm.")]
    private partial void LogDashcamFailed(Exception exception, string printerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "An alarm of printer '{PrinterId}' by detector '{DetectorId}' (score {Score:0.0000}) was marked as a false alarm.")]
    private partial void LogFalseAlarm(string printerId, string detectorId, float score);

    [LoggerMessage(Level = LogLevel.Error, Message = "Detector '{DetectorId}' of printer '{PrinterId}' failed; it is off for the rest of this print.")]
    private partial void LogDetectorFailed(Exception exception, string printerId, string detectorId);

    [LoggerMessage(Level = LogLevel.Information, Message = "The chamber of printer '{PrinterId}' is dark (mean brightness {Brightness}); print failure detection pauses until it is lit.")]
    private partial void LogDark(string printerId, int brightness);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The camera of printer '{PrinterId}' gave no image for the alarm snapshot in time.")]
    private partial void LogNoSnapshot(string printerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "The alarm snapshot of printer '{PrinterId}' could not be encoded.")]
    private partial void LogEncodeFailed(Exception exception, string printerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "The print failure alarm of printer '{PrinterId}' failed.")]
    private partial void LogAlarmFailed(Exception exception, string printerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "A subscriber of print failure alarms failed.")]
    private partial void LogSubscriberFailed(Exception exception);

    /// <summary>What a run of a printer is made of; a change restarts it.</summary>
    private sealed record RunConfig(Uri StreamUri, DetectionRegion Region, TimeSpan Interval, string Detectors, bool DarkFilament);

    /// <summary>One print job of a printer (its id, or its file name when the source has no id).</summary>
    private readonly record struct JobKey(long? Id, string? FileName)
    {
        public static JobKey Of(PrintJob? job) => job is null ? default : new JobKey(job.Id, job.Id is null ? job.FileName : null);
    }

    /// <summary>A printer as configured; every field is guarded by the service's lock.</summary>
    private sealed class Session(string printerId)
    {
        public string PrinterId { get; } = printerId;

        public PrinterDefinition? Printer { get; set; }

        public string FolderName { get; set; } = "";

        public PrinterSnapshot? Snapshot { get; set; }

        public Run? Run { get; set; }

        /// <summary>What a run would be made of with the current settings; <see langword="null"/>: not watched.</summary>
        public RunConfig? Config { get; set; }

        /// <summary>The hub had no camera for the printer: no new lease before the next <see cref="Configure"/>.</summary>
        public bool CameraLost { get; set; }

        /// <summary>The job <see cref="LastAlarm"/> belongs to.</summary>
        public JobKey AlarmJob { get; set; }

        /// <summary>When each detector last raised an alarm in <see cref="AlarmJob"/> (timestamps).</summary>
        public Dictionary<string, long> LastAlarm { get; } = new(StringComparer.Ordinal);

        public void ForgetAlarms()
        {
            LastAlarm.Clear();
            AlarmJob = default;
        }
    }

    /// <summary>A detector of a run with its sensitivity, threshold and recent scores; used by the run's worker only.</summary>
    private sealed class ActiveDetector(PrintFailureDetectorRegistration registration, IPrintFailureDetector detector, int sensitivity, float threshold, ConfirmationWindow window)
    {
        public PrintFailureDetectorRegistration Registration { get; } = registration;

        public IPrintFailureDetector Detector { get; } = detector;

        public int Sensitivity { get; } = sensitivity;

        public float Threshold { get; } = threshold;

        public ConfirmationWindow Window { get; } = window;

        public bool Failed { get; set; }
    }

    /// <summary>
    /// One stretch of printing of one printer: its camera lease, fresh detectors and their state. The lease's frame
    /// callback queues the run itself as a thread-pool work item (no allocation); the work runs under the run's own lock,
    /// one sample at a time. Stopping never waits for it: a stopped run ignores its work and its result.
    /// </summary>
    private sealed class Run(
        PrintFailureDetectionService service,
        Session session,
        RunConfig config,
        JobKey job,
        ActiveDetector[] detectors,
        PrinterSnapshot snapshot,
        long started) : IThreadPoolWorkItem, IProgress<CameraStatus>
    {
        private readonly Lock _work = new();
        private volatile PrinterSnapshot _snapshot = snapshot;
        private volatile bool _stopped;
        private volatile bool _resetPending = true;
        private volatile bool _streamed;
        private int _endReason = -1;

        // Worker only (under _work).
        private bool _dark;
        private int _regionWidth;
        private int _regionHeight;
        private DetectionPixelRegion _region;

        public Session Session { get; } = session;

        public RunConfig Config { get; } = config;

        public JobKey Job { get; } = job;

        /// <summary>Set once under the service's lock right after the lease was taken; read by the worker.</summary>
        public CameraFrameSubscription? Subscription
        {
            get => Volatile.Read(ref field);
            set => Volatile.Write(ref field, value);
        }

        /// <summary>The latest snapshot, for the context of the next sample.</summary>
        public PrinterSnapshot Snapshot
        {
            get => _snapshot;
            set => _snapshot = value;
        }

        /// <summary>Why the hub ended the lease; <see langword="null"/> while it runs.</summary>
        public CameraStreamEndReason? EndReason
        {
            get
            {
                int reason = Volatile.Read(ref _endReason);
                return reason < 0 ? null : (CameraStreamEndReason)reason;
            }
        }

        public void Stop()
        {
            _stopped = true;
            Subscription?.Dispose();
        }

        // Streaming thread: a sample is ready (at most one call pending, see CameraFrameExchange).
        public void Schedule()
        {
            if (!_stopped)
            {
                ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: false);
            }
        }

        // Streaming thread or the hub's caller: never takes a lock.
        public void OnEnded(CameraStreamSubscription subscription) => Volatile.Write(ref _endReason, (int)(subscription.EndReason ?? CameraStreamEndReason.Failed));

        // Hub (under its lock): a reconnected camera may look different; the detectors start over.
        void IProgress<CameraStatus>.Report(CameraStatus value)
        {
            if (value.State == CameraConnectionState.Streaming)
            {
                _streamed = true;
            }
            else if (value.State == CameraConnectionState.Connecting && _streamed)
            {
                _streamed = false;
                _resetPending = true;
            }
        }

        // Thread pool.
        void IThreadPoolWorkItem.Execute()
        {
            try
            {
                lock (_work)
                {
                    if (!_stopped)
                    {
                        Analyze();
                    }
                }
            }
            catch (Exception ex)
            {
                // Defensive: detector failures are caught per detector; nothing may escape onto the thread pool.
                service.LogDetectorFailed(ex, Session.PrinterId, "*");
            }
        }

        private void Analyze()
        {
            if (Subscription?.TakeLatest() is not { Format: CameraPixelFormat.Gray8 } frame)
            {
                return;
            }

            long now = service._time.GetTimestamp();
            TimeSpan elapsed = service._time.GetElapsedTime(started, now);
            PrinterSnapshot snapshot = _snapshot;
            PrintFailureContext context = new(
                snapshot.Job?.Id,
                snapshot.Telemetry?.AxisZ,
                snapshot.Job?.ProgressPercent ?? 0,
                elapsed,
                Config.Region,
                Config.DarkFilament);
            if (frame.Width != _regionWidth || frame.Height != _regionHeight)
            {
                _region = Config.Region.ToPixels(frame.Width, frame.Height);
                _regionWidth = frame.Width;
                _regionHeight = frame.Height;
            }

            ReadOnlySpan<byte> pixels = frame.Pixels;
            int brightness = MeanBrightness(pixels, frame.Stride, _region);
            if (brightness < service._options.DarkBrightness)
            {
                if (!_dark)
                {
                    _dark = true;
                    service.LogDark(Session.PrinterId, brightness);
                }

                return;
            }

            if (_dark || _resetPending)
            {
                // Started, resumed, reconnected or lit again: what the detectors learned no longer fits.
                _dark = false;
                _resetPending = false;
                foreach (ActiveDetector detector in detectors)
                {
                    detector.Window.Clear();
                    if (detector.Failed)
                    {
                        continue;
                    }

                    try
                    {
                        detector.Detector.Reset(context);
                    }
                    catch (Exception ex)
                    {
                        detector.Failed = true;
                        service.LogDetectorFailed(ex, Session.PrinterId, detector.Registration.Id);
                    }
                }
            }

            DetectionSample sample = new(pixels, frame.Width, frame.Height, frame.Stride, elapsed, _region, context);
            foreach (ActiveDetector detector in detectors)
            {
                if (detector.Failed)
                {
                    continue;
                }

                DetectionResult result;
                try
                {
                    result = detector.Detector.Analyze(sample);
                }
                catch (Exception ex)
                {
                    detector.Failed = true;
                    service.LogDetectorFailed(ex, Session.PrinterId, detector.Registration.Id);
                    continue;
                }

                if (!result.IsWarmedUp)
                {
                    detector.Window.Clear();
                    continue;
                }

                float score = float.IsFinite(result.Score) ? Math.Clamp(result.Score, 0, 1) : 0;
                detector.Window.Add(now, score, score >= detector.Threshold);
                if (detector.Window.IsConfirmed(now))
                {
                    service.OnConfirmed(this, detector, score, context, snapshot, now);
                }
            }
        }
    }
}
