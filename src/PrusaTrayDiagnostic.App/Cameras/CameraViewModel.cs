using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;

using PrusaTrayDiagnostic.App.Resources;
using PrusaTrayDiagnostic.App.Tray;
using PrusaTrayDiagnostic.Core.Cameras;
using PrusaTrayDiagnostic.Core.Monitoring;
using PrusaTrayDiagnostic.Core.Printers;

namespace PrusaTrayDiagnostic.App.Cameras;

/// <summary>The printer whose camera a window shows.</summary>
internal sealed record CameraTarget(string PrinterId, string Name, Uri StreamUri)
{
    /// <summary>The printer with an enabled camera, or <see langword="null"/> when it has none.</summary>
    public static CameraTarget? From(PrinterDefinition printer) =>
        printer is { Camera: { Enabled: true, StreamUri: { } uri } } ? new CameraTarget(printer.Id, printer.Name, uri) : null;
}

/// <summary>Draws camera frames (the window's <c>WriteableBitmap</c>). UI thread only.</summary>
internal interface ICameraFrameSink
{
    /// <summary>Shows <paramref name="frame"/>; it is valid only during the call (copy it).</summary>
    void Present(CameraFrameBuffer frame);

    /// <summary>Removes the image (the camera address changed).</summary>
    void Clear();
}

/// <summary>
/// The camera window of one printer (T-014): live image, connection state, window mode and the print overlay shown in
/// every mode.
/// </summary>
/// <remarks>
/// <para>
/// Streaming runs only while the window is active (shown and not minimized, <see cref="SetActive"/>), on the thread pool
/// through <see cref="ReconnectingCameraFeed"/>, which reconnects by itself. Each borrowed frame is copied once into a
/// reused buffer of a <see cref="CameraFrameExchange"/>; normally at most one UI callback is queued, and it hands only the newest
/// frame to the <see cref="ICameraFrameSink"/>, so a busy UI drops frames instead of queueing them. Pausing, closing and
/// disposing cancel the stream, which closes the connection; a new run starts only after the previous one ended, so
/// there is never more than one connection per window.
/// </para>
/// <para>
/// The print overlay follows <see cref="IPrinterMonitor"/> snapshots of this printer (compared cheaply on the monitor
/// thread, applied on the UI thread only when something visible changed). When the settings disable the camera or remove
/// the printer, <see cref="CloseRequested"/> is raised; a changed address restarts the stream, a new name the title.
/// </para>
/// <para>All public members are for the UI thread.</para>
/// </remarks>
public sealed partial class CameraViewModel : ObservableObject, IDisposable
{
    private readonly ReconnectingCameraFeed _feed;
    private readonly IPrinterMonitor _monitor;
    private readonly MonitoredPrinters _printers;
    private readonly IUiDispatcher _dispatcher;
    private readonly ICameraWindowStateStore _stateStore;
    private readonly ILogger<CameraViewModel> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _retryDelay;
    private readonly Action _retry;
    private readonly CameraFrameExchange _exchange = new();
    private readonly Action _presentFrame;
    private readonly Action _applyPrintContent;
    private readonly Action _applyPrinters;
    private readonly Lock _gate = new();

    // Guarded by _gate (written by monitor threads).
    private CameraPrintContent _printContent;
    private bool _printContentPending;
    private bool _disposed;

    // UI thread only.
    private Uri _streamUri;
    private string _name;
    private ICameraFrameSink? _sink;
    private CancellationTokenSource? _run;
    private ITimer? _retryTimer;
    private int _runId;
    private Task _streaming = Task.CompletedTask;
    private bool _active;
    private CameraWindowMode _mode;
    private CameraWindowPlacement? _normalPlacement;
    private CameraWindowPlacement? _miniPlacement;
    private CameraStatus _status = CameraStatus.Connecting;
    private bool _hasFrame;
    private CameraPrintContent _shownPrintContent;
    private string? _printStateText;
    private string? _remainingText;

    internal CameraViewModel(
        CameraTarget target,
        ReconnectingCameraFeed feed,
        IPrinterMonitor monitor,
        MonitoredPrinters printers,
        IUiDispatcher dispatcher,
        ICameraWindowStateStore stateStore,
        TimeProvider timeProvider,
        CameraFeedOptions options,
        ILogger<CameraViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(printers);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(stateStore);
        ArgumentNullException.ThrowIfNull(logger);
        PrinterId = target.PrinterId;
        _name = target.Name;
        _streamUri = target.StreamUri;
        _feed = feed;
        _monitor = monitor;
        _printers = printers;
        _dispatcher = dispatcher;
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(options);
        _stateStore = stateStore;
        _timeProvider = timeProvider;
        _retryDelay = options.ReconnectDelay;
        _retry = Retry;
        _logger = logger;
        _presentFrame = PresentFrame;
        _applyPrintContent = ApplyPrintContent;
        _applyPrinters = ApplyPrinters;

        CameraWindowState state = stateStore.Get(PrinterId);
        _mode = Enum.IsDefined(state.Mode) ? state.Mode : CameraWindowMode.Normal;
        _normalPlacement = state.Normal;
        _miniPlacement = state.Mini;

        ToggleFullScreenCommand = new RelayCommand(ToggleFullScreen);
        ShowMiniCommand = new RelayCommand(() => Mode = CameraWindowMode.Mini, () => _mode != CameraWindowMode.Mini);
        ShowNormalCommand = new RelayCommand(() => Mode = CameraWindowMode.Normal, () => _mode != CameraWindowMode.Normal);

        // Subscribed before reading the current state: a change in between only causes one more (idempotent) update.
        _monitor.SnapshotUpdated += OnSnapshotUpdated;
        _printers.Changed += OnPrintersChanged;
        _printContent = CameraPrintContent.From(_monitor.TryGetSnapshot(PrinterId, out PrinterSnapshot? snapshot) ? snapshot : null);
        ApplyPrintContent();
    }

    /// <summary>The settings no longer have this camera (disabled, printer removed): the window should close.</summary>
    public event EventHandler? CloseRequested;

    public string PrinterId { get; }

    public string Title => Format(Strings.CameraTitle, _name);

    public CameraWindowMode Mode
    {
        get => _mode;
        private set
        {
            if (_disposed || !SetProperty(ref _mode, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsNormal));
            OnPropertyChanged(nameof(IsFullScreen));
            OnPropertyChanged(nameof(IsMini));
            OnPropertyChanged(nameof(FullScreenToolTip));
            ShowMiniCommand.NotifyCanExecuteChanged();
            ShowNormalCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsNormal => _mode == CameraWindowMode.Normal;

    public bool IsFullScreen => _mode == CameraWindowMode.FullScreen;

    public bool IsMini => _mode == CameraWindowMode.Mini;

    public string FullScreenToolTip => IsFullScreen ? Strings.CameraExitFullScreen : Strings.CameraFullScreen;

    /// <summary>Where the normal window was; <see langword="null"/> = never placed (the window centres itself).</summary>
    internal CameraWindowPlacement? NormalPlacement => _normalPlacement;

    /// <summary>Where the mini preview was; <see langword="null"/> = never shown.</summary>
    internal CameraWindowPlacement? MiniPlacement => _miniPlacement;

    /// <summary>Normal / full screen (F11, double-click); from the mini preview it goes full screen.</summary>
    public IRelayCommand ToggleFullScreenCommand { get; }

    public IRelayCommand ShowMiniCommand { get; }

    /// <summary>Back to the normal window (Esc, the button of the mini preview).</summary>
    public IRelayCommand ShowNormalCommand { get; }

    /// <summary>Connecting, no signal or an error: the overlay over the (last) image is shown.</summary>
    public bool IsStatusVisible => _status.State != CameraConnectionState.Streaming;

    public string StatusText => _status.State switch
    {
        CameraConnectionState.NoSignal => Strings.CameraNoSignal,
        CameraConnectionState.Error => Strings.CameraError,
        _ => Strings.CameraConnecting,
    };

    /// <summary>Why there is no image (technical English text without the address); <see langword="null"/> when none.</summary>
    public string? StatusDetail => IsStatusVisible ? _status.Reason : null;

    public bool HasStatusDetail => StatusDetail is not null;

    /// <summary>At least one image was shown (it stays visible while reconnecting).</summary>
    public bool HasFrame
    {
        get => _hasFrame;
        private set => SetProperty(ref _hasFrame, value);
    }

    /// <summary>"Printing – 42%"; <see langword="null"/> before the printer was polled.</summary>
    public string? PrintStateText
    {
        get => _printStateText;
        private set
        {
            if (SetProperty(ref _printStateText, value))
            {
                OnPropertyChanged(nameof(IsPrintOverlayVisible));
            }
        }
    }

    /// <summary>"1 h 5 min remaining"; <see langword="null"/> when not printing or unknown.</summary>
    public string? RemainingText
    {
        get => _remainingText;
        private set
        {
            if (SetProperty(ref _remainingText, value))
            {
                OnPropertyChanged(nameof(HasRemaining));
            }
        }
    }

    public bool HasRemaining => _remainingText is not null;

    /// <summary>The print state is shown over the image in every mode once the printer was polled (user decision 2026-10-18).</summary>
    public bool IsPrintOverlayVisible => _printStateText is not null;

    /// <summary>Completes when the current (or last) stream run ended and its connection was closed.</summary>
    internal Task StreamCompletion => _streaming;

    /// <summary>Whether a stream run is active (not paused, not closed).</summary>
    internal bool IsStreaming => _run is not null;

    /// <summary>
    /// The window is shown and not minimized (<see langword="true"/>): the stream runs; minimized (<see langword="false"/>):
    /// it stops until the window is restored.
    /// </summary>
    public void SetActive(bool active)
    {
        if (_disposed || _active == active)
        {
            return;
        }

        _active = active;
        if (active)
        {
            StartStream();
        }
        else
        {
            StopStream();
        }
    }

    /// <summary>The window's drawing surface; <see langword="null"/> detaches it (frames are then dropped).</summary>
    internal void AttachFrameSink(ICameraFrameSink? sink) => _sink = sink;

    /// <summary>
    /// Double-click on the image: normal ↔ full screen; from the mini preview back to the normal window.
    /// </summary>
    public void OnImageDoubleClicked() =>
        Mode = _mode == CameraWindowMode.Normal ? CameraWindowMode.FullScreen : CameraWindowMode.Normal;

    /// <summary>
    /// Where the window was while it showed <paramref name="mode"/> (the window reports it before leaving a mode and
    /// when closing); full screen has no placement of its own and is ignored.
    /// </summary>
    internal void RememberPlacement(CameraWindowMode mode, CameraWindowPlacement placement)
    {
        if (_disposed || !placement.IsValid)
        {
            return;
        }

        if (mode == CameraWindowMode.Normal)
        {
            _normalPlacement = placement;
        }
        else if (mode == CameraWindowMode.Mini)
        {
            _miniPlacement = placement;
        }
    }

    /// <summary>
    /// The OS switched the window into or out of full screen by itself (e.g. the green button on macOS): the mode follows.
    /// </summary>
    internal void OnWindowFullScreenChanged(bool isFullScreen)
    {
        if (isFullScreen && _mode != CameraWindowMode.FullScreen)
        {
            Mode = CameraWindowMode.FullScreen;
        }
        else if (!isFullScreen && _mode == CameraWindowMode.FullScreen)
        {
            Mode = CameraWindowMode.Normal;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _monitor.SnapshotUpdated -= OnSnapshotUpdated;
        _printers.Changed -= OnPrintersChanged;
        StopStream();
        _sink = null;
        _stateStore.Set(PrinterId, new CameraWindowState(_mode, _normalPlacement, _miniPlacement));
    }

    private static string Format(string format, object? arg0) => string.Format(CultureInfo.CurrentCulture, format, arg0);

    private void ToggleFullScreen() => Mode = _mode == CameraWindowMode.FullScreen ? CameraWindowMode.Normal : CameraWindowMode.FullScreen;

    private void StartStream()
    {
        if (_run is not null)
        {
            return;
        }

        // Owned by the UI thread: cancelled by StopStream, disposed by OnRunEnded once the run has ended.
        CancellationTokenSource run = new();
        _run = run;
        int runId = ++_runId;
        Uri uri = _streamUri;
        Task previous = _streaming;
        SetStatus(CameraStatus.Connecting);
        StatusReporter status = new(this, runId);
        // Task.Run: a decoder may block while connecting; nothing of the stream runs on the UI thread.
        _streaming = Task.Run(() => RunStreamAsync(previous, uri, status, run, runId), CancellationToken.None);
    }

    private void StopStream()
    {
        // Minimized, closed or another address: an automatic retry of an ended run is no longer wanted.
        CancelRetry();
        if (_run is not { } run)
        {
            return;
        }

        _run = null;
        // Statuses and frames of the stopped run are ignored from now on.
        _runId++;
        // Never disposed yet: only OnRunEnded disposes it, after clearing _run on this thread.
        run.Cancel();
    }

    /// <summary>
    /// UI thread, after a run ended (stopped, or by itself after an unexpected failure): a run that ended by itself is
    /// no longer active, so the next activation or address change starts a new one. Disposes the run's token source.
    /// </summary>
    private void OnRunEnded(CancellationTokenSource run)
    {
        if (ReferenceEquals(_run, run))
        {
            _run = null;
            _runId++;
            if (_active && !_disposed)
            {
                // User decision (T-014): an unexpected internal error is retried after the reconnect delay.
                ScheduleRetry();
            }
        }

        run.Dispose();
    }

    private void ScheduleRetry()
    {
        CancelRetry();
        _retryTimer = _timeProvider.CreateTimer(static state => ((CameraViewModel)state!).OnRetryDue(), this, _retryDelay, Timeout.InfiniteTimeSpan);
    }

    private void CancelRetry()
    {
        _retryTimer?.Dispose();
        _retryTimer = null;
    }

    // Timer thread.
    private void OnRetryDue()
    {
        try
        {
            _dispatcher.Post(_retry);
        }
        catch (Exception ex)
        {
            // The UI is gone (quitting): nothing to retry.
            LogStreamFailed(ex);
        }
    }

    // UI thread. A retry that was cancelled after it had been posted finds the window paused, closed or streaming again.
    private void Retry()
    {
        CancelRetry();
        if (_active && !_disposed && _run is null)
        {
            StartStream();
        }
    }

    private async Task RunStreamAsync(Task previous, Uri uri, StatusReporter status, CancellationTokenSource run, int runId)
    {
        CancellationToken token = run.Token;
        try
        {
            // One connection per window: the previous run must have closed its connection first.
            await previous.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            // Only this run produces frames now; a notification lost by an earlier run must not block it.
            _exchange.ResetNotification();
            await foreach (CameraFrame frame in _feed.ReadFramesAsync(uri, status, token).ConfigureAwait(false))
            {
                if (token.IsCancellationRequested)
                {
                    break;
                }

                if (_exchange.Publish(frame, runId))
                {
                    _dispatcher.Post(_presentFrame);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !token.IsCancellationRequested)
        {
            // The feed reports stream failures as statuses; this is only a safety net (e.g. a broken frame).
            LogStreamFailed(ex);
            TryReport(status, new CameraStatus(CameraConnectionState.Error, $"The camera stream failed ({ex.GetType().Name})."));
        }
        catch (OperationCanceledException)
        {
            // Paused or closed.
        }
        finally
        {
            try
            {
                _dispatcher.Post(() => OnRunEnded(run));
            }
            catch (Exception ex)
            {
                // The UI is gone (quitting); the token source holds no unmanaged resources.
                LogStreamFailed(ex);
            }
        }
    }

    private void TryReport(StatusReporter status, CameraStatus value)
    {
        try
        {
            status.Report(value);
        }
        catch (Exception ex)
        {
            LogStreamFailed(ex);
        }
    }

    private void PresentFrame()
    {
        CameraFrameBuffer? frame = _exchange.TakeLatest();
        // Frames of a stopped run (paused, or the previous camera address) are not drawn any more.
        if (frame is null || _disposed || _sink is not { } sink || frame.Generation != _runId)
        {
            return;
        }

        try
        {
            sink.Present(frame);
            HasFrame = true;
        }
        catch (Exception ex)
        {
            // A dispatcher callback must never throw; the next frame tries again.
            LogPresentFailed(ex);
        }
    }

    private void SetStatus(CameraStatus status)
    {
        if (_status == status)
        {
            return;
        }

        bool wasVisible = IsStatusVisible;
        string? previousDetail = StatusDetail;
        _status = status;
        OnPropertyChanged(nameof(StatusText));
        if (wasVisible != IsStatusVisible)
        {
            OnPropertyChanged(nameof(IsStatusVisible));
        }

        if (previousDetail != StatusDetail)
        {
            OnPropertyChanged(nameof(StatusDetail));
            OnPropertyChanged(nameof(HasStatusDetail));
        }
    }

    private void OnSnapshotUpdated(object? sender, PrinterSnapshot snapshot)
    {
        if (snapshot is null || !string.Equals(snapshot.PrinterId, PrinterId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        CameraPrintContent content = CameraPrintContent.From(snapshot);
        lock (_gate)
        {
            if (_disposed || _printContent == content)
            {
                return;
            }

            _printContent = content;
            if (_printContentPending)
            {
                return;
            }

            _printContentPending = true;
        }

        _dispatcher.Post(_applyPrintContent);
    }

    private void ApplyPrintContent()
    {
        CameraPrintContent content;
        lock (_gate)
        {
            _printContentPending = false;
            if (_disposed)
            {
                return;
            }

            content = _printContent;
        }

        if (content == _shownPrintContent && _printStateText is not null)
        {
            return;
        }

        _shownPrintContent = content;
        PrintStateText = content.StateText();
        RemainingText = content.RemainingText();
    }

    // Raised on the monitoring session's thread.
    private void OnPrintersChanged(object? sender, EventArgs e) => _dispatcher.Post(_applyPrinters);

    private void ApplyPrinters()
    {
        if (_disposed || _printers.Current is not { Printers: { } printers })
        {
            return;
        }

        PrinterDefinition? printer = null;
        foreach (PrinterDefinition candidate in printers)
        {
            if (string.Equals(candidate.Id, PrinterId, StringComparison.OrdinalIgnoreCase))
            {
                printer = candidate;
                break;
            }
        }

        if (printer is null || CameraTarget.From(printer) is not { } target)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (!string.Equals(_name, target.Name, StringComparison.Ordinal))
        {
            _name = target.Name;
            OnPropertyChanged(nameof(Title));
        }

        if (!string.Equals(_streamUri.OriginalString, target.StreamUri.OriginalString, StringComparison.Ordinal))
        {
            _streamUri = target.StreamUri;
            // Another camera: its predecessor's image must not stay on screen.
            _sink?.Clear();
            HasFrame = false;
            StopStream();
            SetStatus(CameraStatus.Connecting);
            if (_active)
            {
                StartStream();
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The camera stream of the camera window failed unexpectedly.")]
    private partial void LogStreamFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Showing a camera image failed.")]
    private partial void LogPresentFailed(Exception exception);

    /// <summary>
    /// Receives statuses on the streaming thread and applies them on the UI thread, unless the run was stopped meanwhile
    /// (a paused or closed window shows nothing of the old connection). Statuses change rarely; posting each is fine.
    /// </summary>
    private sealed class StatusReporter(CameraViewModel owner, int runId) : IProgress<CameraStatus>
    {
        public void Report(CameraStatus value) => owner._dispatcher.Post(() =>
        {
            if (!owner._disposed && owner._runId == runId)
            {
                owner.SetStatus(value);
            }
        });
    }
}
