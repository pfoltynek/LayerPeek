using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;

using LayerPeek.App.Cameras.Overlay;
using LayerPeek.App.Localization;
using LayerPeek.App.Resources;
using LayerPeek.App.Tray;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Overlay;
using LayerPeek.Core.Monitoring;
using LayerPeek.Core.Printers;

namespace LayerPeek.App.Cameras;

/// <summary>The printer whose camera a window shows (only printers with an enabled camera, see <see cref="From"/>).</summary>
internal sealed record CameraTarget
{
    private CameraTarget(PrinterDefinition printer, Uri streamUri)
    {
        Printer = printer;
        StreamUri = streamUri;
    }

    /// <summary>The printer as configured.</summary>
    public PrinterDefinition Printer { get; }

    public string PrinterId => Printer.Id;

    public string Name => Printer.Name;

    public Uri StreamUri { get; }

    /// <summary>How the window opens (T-035).</summary>
    public CameraWindowDefaults Window => Printer.Camera.Window;

    /// <summary>The widgets drawn over the image and where (T-077, T-081).</summary>
    public CameraOverlayLayout Overlay => Printer.Camera.Overlay;

    /// <summary>The printer with an enabled camera, or <see langword="null"/> when it has none.</summary>
    public static CameraTarget? From(PrinterDefinition printer)
    {
        ArgumentNullException.ThrowIfNull(printer);
        return printer is { Camera: { Enabled: true, StreamUri: { } uri } } ? new CameraTarget(printer, uri) : null;
    }
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
/// The widget overlay (<see cref="Overlay"/>, T-081) follows <see cref="IPrinterMonitor"/> snapshots of this printer
/// (compared cheaply on the monitor thread, applied on the UI thread only when something visible changed). When the
/// settings disable the camera or remove the printer, <see cref="CloseRequested"/> is raised; a changed address restarts the
/// stream, a new name the title; a new layout or card opacity changes the overlay in place.
/// </para>
/// <para>All public members are for the UI thread.</para>
/// </remarks>
public sealed partial class CameraViewModel : ObservableObject, IDisposable, ILanguageAware
{
    private readonly ReconnectingCameraFeed _feed;
    private readonly IPrinterMonitor _monitor;
    private readonly MonitoredPrinters _printers;
    private readonly IUiDispatcher _dispatcher;
    private readonly ICameraWindowStateStore _stateStore;
    private readonly ILogger<CameraViewModel> _logger;
    private readonly ICameraMonitorPathUpdater? _monitorPaths;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _retryDelay;
    private readonly Action _retry;
    private readonly CameraFrameExchange _exchange = new();
    private readonly Action _presentFrame;
    private readonly Action _applyPrinters;
    private readonly Lock _gate = new();

    // Guarded by _gate (read by monitor threads).
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

    // Where full screen returns to (user decision 2026-10-20): the mode it was entered from; the normal window when the
    // window opened in full screen.
    private CameraWindowMode _fullScreenOrigin = CameraWindowMode.Normal;
    private CameraWindowPlacement? _normalPlacement;
    private CameraWindowPlacement? _miniPlacement;
    private CameraWindowDefaults _defaults;

    // GetRemoveCount of the state store when the window opened: a different value at closing means the settings changed
    // the defaults meanwhile, so the state is not written back (T-035). "Apply" in the settings (T-040) places the window
    // by the new settings and takes the current count, so that the window is remembered again from then on.
    private int _stateRemoveCount;
    private CameraStatus _status = CameraStatus.Connecting;
    private bool _hasFrame;

    // Thread of the ending run (runs never overlap) and the UI thread (cleared by a shown frame): the last failure that
    // ended a run by itself, as "type: message", so that a repeated one is logged briefly.
    private string? _lastRunFailure;

    /// <param name="widgets">The overlay widgets the window can draw (T-081); <see langword="null"/> = none.</param>
    internal CameraViewModel(
        CameraTarget target,
        ReconnectingCameraFeed feed,
        IPrinterMonitor monitor,
        MonitoredPrinters printers,
        IUiDispatcher dispatcher,
        ICameraWindowStateStore stateStore,
        TimeProvider timeProvider,
        CameraFeedOptions options,
        ILogger<CameraViewModel> logger,
        ICameraMonitorPathUpdater? monitorPaths = null,
        OverlayWidgetRegistry? widgets = null)
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
        _monitorPaths = monitorPaths;
        _presentFrame = PresentFrame;
        _applyPrinters = ApplyPrinters;

        // T-035: the remembered state (when the defaults allow it) or the defaults; the window places itself from these.
        _defaults = target.Window;
        _stateRemoveCount = stateStore.GetRemoveCount(PrinterId);
        CameraWindowState state = stateStore.Get(PrinterId);
        CameraWindowOpenPlan plan = CameraWindowOpenPolicy.Decide(
            _defaults,
            Enum.IsDefined(state.Mode) ? state.Mode : null,
            hasRememberedNormal: state.Normal is not null,
            hasRememberedMini: state.Mini is not null);
        _mode = plan.Mode;
        _normalPlacement = plan.UseRememberedNormal ? state.Normal : null;
        _miniPlacement = plan.UseRememberedMini ? state.Mini : null;

        ToggleFullScreenCommand = new RelayCommand(ToggleFullScreen);
        ShowFullScreenCommand = new RelayCommand(() => Mode = CameraWindowMode.FullScreen, () => _mode != CameraWindowMode.FullScreen);
        ShowMiniCommand = new RelayCommand(() => Mode = CameraWindowMode.Mini, () => _mode != CameraWindowMode.Mini);
        ShowNormalCommand = new RelayCommand(() => Mode = CameraWindowMode.Normal, () => _mode != CameraWindowMode.Normal);
        EscapeCommand = new RelayCommand(Escape, () => _mode != CameraWindowMode.Normal);

        // T-081: the widgets of the printer's layout with the global card opacity.
        Overlay = new CameraOverlayViewModel(
            widgets ?? OverlayWidgetRegistry.Empty,
            new OverlayWidgetContext(PrinterId, timeProvider),
            dispatcher,
            logger,
            target.Printer,
            target.Overlay,
            printers.Current.CameraOverlayOpacityPercent,
            _mode,
            snapshot: null);

        // Subscribed before reading the current state: a change in between only causes one more (idempotent) update.
        _monitor.SnapshotUpdated += OnSnapshotUpdated;
        _printers.Changed += OnPrintersChanged;
        PrinterSnapshot? current = _monitor.TryGetSnapshot(PrinterId, out PrinterSnapshot? snapshot) ? snapshot : null;
        Overlay.ShowCurrent(current);
    }

    /// <summary>The cards over the image (T-081): normal window and full screen, none in the mini preview.</summary>
    public CameraOverlayViewModel Overlay { get; }

    /// <summary>The settings no longer have this camera (disabled, printer removed): the window should close.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// "Apply" in the settings (T-040, <see cref="ApplyWindowSettings"/>): the window places itself again in
    /// <see cref="Mode"/> by <see cref="Defaults"/> (monitor, size, corner, always on top), ignoring where it was.
    /// </summary>
    internal event EventHandler? PlacementResetRequested;

    /// <summary>
    /// While <see cref="ApplyWindowSettings"/> switches the mode: the window must not place the new mode itself (the
    /// placement follows in <see cref="PlacementResetRequested"/>).
    /// </summary>
    internal bool IsResettingPlacement { get; private set; }

    public string PrinterId { get; }

    public string Title => Format(Strings.CameraTitle, _name);

    /// <summary>
    /// UI thread: the language changed (T-029). The title, button tips, connection state and the overlay widgets are
    /// formatted again from what they show; the stream, the mode and the image do not change.
    /// </summary>
    public void RefreshTexts()
    {
        if (_disposed)
        {
            return;
        }

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(NormalToolTip));
        OnPropertyChanged(nameof(MiniToolTip));
        OnPropertyChanged(nameof(StatusText));
        Overlay.RefreshTexts();
    }

    public CameraWindowMode Mode
    {
        get => _mode;
        private set
        {
            CameraWindowMode previous = _mode;
            if (_disposed || !SetProperty(ref _mode, value))
            {
                return;
            }

            if (value == CameraWindowMode.FullScreen)
            {
                _fullScreenOrigin = previous == CameraWindowMode.Mini ? CameraWindowMode.Mini : CameraWindowMode.Normal;
            }

            OnPropertyChanged(nameof(IsNormal));
            OnPropertyChanged(nameof(IsFullScreen));
            OnPropertyChanged(nameof(IsMini));
            OnPropertyChanged(nameof(NormalToolTip));
            OnPropertyChanged(nameof(IsTopmost));
            // The mini preview draws no cards (T-081).
            Overlay.SetMode(value);
            ShowFullScreenCommand.NotifyCanExecuteChanged();
            ShowMiniCommand.NotifyCanExecuteChanged();
            ShowNormalCommand.NotifyCanExecuteChanged();
            EscapeCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsNormal => _mode == CameraWindowMode.Normal;

    public bool IsFullScreen => _mode == CameraWindowMode.FullScreen;

    public bool IsMini => _mode == CameraWindowMode.Mini;

    /// <summary>
    /// Whether the window stays above other windows in its current mode (T-037): the printer's option for the normal window
    /// or the mini preview, never in full screen. Changes with the mode and right after saved settings change the option.
    /// </summary>
    public bool IsTopmost => _defaults.IsTopmost(_mode);

    /// <summary>
    /// Tip (and accessible name) of the normal window button: mentions the double-click only where it leads to the normal
    /// window, i.e. not in full screen entered from the mini preview (the double-click returns there).
    /// </summary>
    public string NormalToolTip =>
        IsFullScreen && _fullScreenOrigin == CameraWindowMode.Mini ? Strings.CameraNormalPlain : Strings.CameraNormal;

    /// <summary>
    /// Tip (and accessible name) of the mini preview button: says "always on top" only while the printer's option for the
    /// mini preview is on (user decision 2026-10-20, T-037 Q1).
    /// </summary>
    public string MiniToolTip => _defaults.TopmostMini ? Strings.CameraMini : Strings.CameraMiniPlain;

    /// <summary>
    /// Where the normal window was (remembered, or earlier in this window); <see langword="null"/> = the window places it
    /// from <see cref="Defaults"/>.
    /// </summary>
    internal CameraWindowPlacement? NormalPlacement => _normalPlacement;

    /// <summary>Where the mini preview was; <see langword="null"/> = the window places it from <see cref="Defaults"/>.</summary>
    internal CameraWindowPlacement? MiniPlacement => _miniPlacement;

    /// <summary>How the window opens (T-035); follows saved settings, which apply to the next placement from defaults.</summary>
    internal CameraWindowDefaults Defaults => _defaults;

    /// <summary>
    /// The window read the monitors to open on (UI thread, once per opening): a saved monitor found unambiguously at another
    /// device path (another connector, older settings without a path) gets that path saved in the background (user decision
    /// 2026-10-23, <see cref="CameraWindowDefaultPlacement.Relocate"/>). The window is placed the same either way.
    /// </summary>
    internal void OnScreensRead(IReadOnlyList<CameraScreen> screens)
    {
        ArgumentNullException.ThrowIfNull(screens);
        if (_monitorPaths is { } monitorPaths
            && _defaults.Monitor is { } saved
            && CameraWindowDefaultPlacement.Relocate(saved, screens) is { } updated)
        {
            monitorPaths.Request(PrinterId, saved, updated);
        }
    }

    /// <summary>
    /// F11: into full screen from the normal window or the mini preview; out of full screen back to the mode it was entered
    /// from (the normal window when the window opened in full screen).
    /// </summary>
    public IRelayCommand ToggleFullScreenCommand { get; }

    /// <summary>Full screen (a window button; every mode shows the buttons of the other two).</summary>
    public IRelayCommand ShowFullScreenCommand { get; }

    public IRelayCommand ShowMiniCommand { get; }

    /// <summary>The normal window (from the mini preview or full screen); Esc is <see cref="EscapeCommand"/>.</summary>
    public IRelayCommand ShowNormalCommand { get; }

    /// <summary>Esc: leaves full screen to the mode it was entered from; from the mini preview back to the normal window.</summary>
    public IRelayCommand EscapeCommand { get; }

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
    /// Double-click on the image: normal window → full screen; full screen → the mode it was entered from; mini preview →
    /// normal window.
    /// </summary>
    public void OnImageDoubleClicked() => Mode = _mode switch
    {
        CameraWindowMode.Normal => CameraWindowMode.FullScreen,
        CameraWindowMode.FullScreen => _fullScreenOrigin,
        _ => CameraWindowMode.Normal,
    };

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
    /// "Apply" in the settings window (T-040; an exception to T-035 Q21, which Save keeps): the open window takes
    /// <paramref name="defaults"/> right away. The mode follows the start mode ("same as last time" keeps the current
    /// one); the placements remembered in this window are dropped, so the window is placed by the defaults on their
    /// monitor (<see cref="PlacementResetRequested"/>) and later mode switches use the defaults too. From now on the window
    /// is remembered again when it closes (the settings forgot the old state just before). Also called before a closed
    /// window is shown by Apply: the window then opens placed by the defaults.
    /// </summary>
    internal void ApplyWindowSettings(CameraWindowDefaults defaults)
    {
        ArgumentNullException.ThrowIfNull(defaults);
        if (_disposed)
        {
            return;
        }

        SetDefaults(defaults);
        _normalPlacement = null;
        _miniPlacement = null;
        _stateRemoveCount = _stateStore.GetRemoveCount(PrinterId);
        CameraWindowMode mode = defaults.StartMode switch
        {
            CameraWindowStartMode.Normal => CameraWindowMode.Normal,
            CameraWindowStartMode.Mini => CameraWindowMode.Mini,
            CameraWindowStartMode.FullScreen => CameraWindowMode.FullScreen,
            _ => _mode,
        };

        IsResettingPlacement = true;
        try
        {
            Mode = mode;
        }
        finally
        {
            IsResettingPlacement = false;
        }

        PlacementResetRequested?.Invoke(this, EventArgs.Empty);
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
            Mode = _fullScreenOrigin;
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
        Overlay.Dispose();
        StopStream();
        _sink = null;
        if (_stateStore.GetRemoveCount(PrinterId) != _stateRemoveCount)
        {
            return;
        }

        if (_defaults.RememberLastPlacement)
        {
            _stateStore.Set(PrinterId, new CameraWindowState(_mode, _normalPlacement, _miniPlacement));
        }
        else
        {
            // "Remember" is off (T-035, user decision 2026-10-19): only the mode is remembered (for "same as last time",
            // Q2); the placements remembered earlier stay as they were, for when "remember" is turned on again.
            CameraWindowState stored = _stateStore.Get(PrinterId);
            _stateStore.Set(PrinterId, stored with { Mode = _mode });
        }
    }

    private static string Format(string format, object? arg0) => string.Format(CultureInfo.CurrentCulture, format, arg0);

    private void ToggleFullScreen() => Mode = _mode == CameraWindowMode.FullScreen ? _fullScreenOrigin : CameraWindowMode.FullScreen;

    private void Escape() => Mode = _mode == CameraWindowMode.FullScreen ? _fullScreenOrigin : CameraWindowMode.Normal;

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
            LogRetryNotPosted(ex);
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
            LogRunFailure(ex);
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

    /// <summary>
    /// The full exception once, then a short line while runs keep ending with the same failure (the automatic retry would
    /// otherwise log the same stack trace every few seconds); a frame shown in between starts over. Like the repeated
    /// failures of <see cref="ReconnectingCameraFeed"/>. Runs never overlap, so only one thread at a time sets the key.
    /// </summary>
    private void LogRunFailure(Exception exception)
    {
        string key = string.Concat(exception.GetType().FullName, ": ", exception.Message);
        if (string.Equals(Interlocked.Exchange(ref _lastRunFailure, key), key, StringComparison.Ordinal))
        {
            LogStreamFailedAgain(exception.GetType().Name);
        }
        else
        {
            LogStreamFailed(exception);
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
            // A frame was shown: the next failure of a run is a new one (see LogRunFailure).
            Volatile.Write(ref _lastRunFailure, null);
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

        // The widgets compare their content on this thread and post only visible changes (T-081).
        Overlay.OnSnapshot(snapshot);
    }

    // Raised on the monitoring session's thread.
    private void OnPrintersChanged(object? sender, EventArgs e) => _dispatcher.Post(_applyPrinters);

    private void ApplyPrinters()
    {
        if (_disposed || _printers.Current is not { Printers: { } printers } state)
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

        // The open window does not move (T-035); the new defaults apply to the next placement from defaults. Only
        // "always on top" applies to the open window right away (T-037).
        SetDefaults(target.Window);

        // The layout, the printer (name…) and the global card opacity apply to the open window right away, in place (T-081).
        Overlay.ApplySettings(target.Printer, target.Overlay, state.CameraOverlayOpacityPercent);

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

    /// <summary>New window defaults; "always on top" and the mini preview tip are announced when they change.</summary>
    private void SetDefaults(CameraWindowDefaults defaults)
    {
        bool wasTopmost = IsTopmost;
        bool wasTopmostMini = _defaults.TopmostMini;
        _defaults = defaults;
        if (IsTopmost != wasTopmost)
        {
            OnPropertyChanged(nameof(IsTopmost));
        }

        if (_defaults.TopmostMini != wasTopmostMini)
        {
            OnPropertyChanged(nameof(MiniToolTip));
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The camera stream of the camera window failed unexpectedly.")]
    private partial void LogStreamFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The camera stream of the camera window failed again ({ExceptionType}).")]
    private partial void LogStreamFailedAgain(string exceptionType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The camera stream could not be restarted because the UI is shutting down.")]
    private partial void LogRetryNotPosted(Exception exception);

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
