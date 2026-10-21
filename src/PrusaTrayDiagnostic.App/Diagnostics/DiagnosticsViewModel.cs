using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using PrusaTrayDiagnostic.App.Localization;
using PrusaTrayDiagnostic.App.Printers;
using PrusaTrayDiagnostic.App.Resources;
using PrusaTrayDiagnostic.App.Tray;
using PrusaTrayDiagnostic.Core.History;
using PrusaTrayDiagnostic.Core.Monitoring;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.PrusaLink;

namespace PrusaTrayDiagnostic.App.Diagnostics;

/// <summary>A printer in the selector of the diagnostics window.</summary>
public sealed record PrinterChoice(string Id, string Name);

/// <summary>A time range of the temperature chart.</summary>
public sealed class ChartWindowOption(TimeSpan window, Func<string> text) : LocalizedOption(text)
{
    public TimeSpan Window { get; } = window;
}

/// <summary>
/// Content of the diagnostics window for the selected printer: state, temperatures, fans, motion, last update, printer
/// and firmware information, and what the temperature chart shows.
/// </summary>
/// <remarks>
/// <para>
/// Lives only while the window is open (one instance per window) and is disposed when it closes, which unsubscribes
/// from the monitor and cancels a pending information request: a closed window costs nothing.
/// </para>
/// <para>
/// Snapshots arrive on monitor threads. Snapshots of other printers are dropped right away; for the selected printer
/// only a cheap <see cref="DiagnosticsContent"/> (rounded like the display) is compared under the lock. Only a visible
/// change posts work to the UI thread, and a burst of changes is applied by one UI update, which raises property changes
/// only for texts that really differ. Configuration changes (printer added, removed, renamed) and monitoring failures
/// arrive the same way through <see cref="MonitoredPrinters"/>.
/// </para>
/// <para>All public members are for the UI thread.</para>
/// </remarks>
public sealed class DiagnosticsViewModel : ObservableObject, IDisposable, ILanguageAware
{
    /// <summary>Time range of the chart when the window opens.</summary>
    public static readonly TimeSpan DefaultTimeWindow = TimeSpan.FromHours(2);

    private readonly IPrinterMonitor _monitor;
    private readonly MonitoredPrinters _monitoredPrinters;
    private readonly IUiDispatcher _dispatcher;
    private readonly IPrinterInfoSource _infoSource;
    private readonly Action _apply;
    private readonly Lock _gate = new();
    private readonly ObservableCollection<PrinterChoice> _printers = [];
    private readonly CustomPrinterImages? _images;

    // Guarded by _gate. _selectedId is also read without it (volatile) to drop other printers' snapshots cheaply.
    private string? _selectedId;
    private DiagnosticsContent? _content;
    private bool _contentDirty;
    private bool _printersDirty;
    private bool _applyPending;
    private bool _disposed;

    // UI thread only.
    private Dictionary<string, PrinterDefinition> _definitions = new(StringComparer.OrdinalIgnoreCase);

    // Printer to show as soon as the configured printers are known (opened from a printer submenu while monitoring was
    // starting, or a request racing a configuration change); cleared by the next printer list.
    private string? _preferredId;
    private bool _syncingPrinters;
    private CancellationTokenSource? _infoCancellation;
    private int _infoRequest;
    private InfoStatus _infoStatus;
    // A failed information request was already retried while the printer stayed reachable; reset by an unreachable
    // snapshot and by a new request for another printer or address.
    private bool _infoRetryUsed;

    private PrinterChoice? _selectedPrinter;
    private PrinterPicture? _picture;
    private bool _hasPrinterSelector;
    private bool _hasPrinter;
    private string? _notice;
    private string _stateText = string.Empty;
    private string? _detailText;
    private string _lastUpdateText = string.Empty;
    private string _nozzleActualText = string.Empty;
    private string _nozzleTargetText = string.Empty;
    private string _bedActualText = string.Empty;
    private string _bedTargetText = string.Empty;
    private string _hotendFanText = string.Empty;
    private string _printFanText = string.Empty;
    private string _speedText = string.Empty;
    private string _flowText = string.Empty;
    private string _axisZText = string.Empty;
    private string? _infoStatusText;
    private string _firmwareText = string.Empty;
    private string _modelText = string.Empty;
    private string _hostnameText = string.Empty;
    private string _nozzleDiameterText = string.Empty;
    private string _mmuText = string.Empty;
    private string? _chartPrinterId;
    private ChartWindowOption _selectedTimeWindow;

    // What the texts were formatted from, so that a language switch formats them again.
    private Func<string>? _noticeSource;
    private Func<string>? _infoStatusSource;
    private DiagnosticsContent? _shownContent;
    private PrusaLinkPrinterInfo? _shownInfo;

    internal DiagnosticsViewModel(
        IPrinterMonitor monitor,
        MonitoredPrinters printers,
        TelemetryHistory history,
        IUiDispatcher dispatcher,
        IPrinterInfoSource infoSource,
        TimeProvider clock,
        string? initialPrinterId = null,
        CustomPrinterImages? images = null)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(printers);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(infoSource);
        ArgumentNullException.ThrowIfNull(clock);
        _monitor = monitor;
        _monitoredPrinters = printers;
        _dispatcher = dispatcher;
        _infoSource = infoSource;
        _apply = Apply;
        History = history;
        Clock = clock;
        Printers = new ReadOnlyObservableCollection<PrinterChoice>(_printers);
        TimeWindows =
        [
            new ChartWindowOption(TimeSpan.FromMinutes(30), static () => Strings.DiagnosticsChartWindow30Minutes),
            new ChartWindowOption(DefaultTimeWindow, static () => Strings.DiagnosticsChartWindow2Hours),
            new ChartWindowOption(TimeSpan.FromHours(24), static () => Strings.DiagnosticsChartWindow24Hours),
        ];
        _selectedTimeWindow = TimeWindows[1];
        ShowContent(null);
        ShowInfo(null);
        _preferredId = initialPrinterId;

        // Subscribed before reading the current state: a change in between only causes one more (idempotent) update.
        _monitor.SnapshotUpdated += OnSnapshotUpdated;
        _monitoredPrinters.Changed += OnPrintersChanged;
        _images = images;
        if (_images is not null)
        {
            _images.Changed += OnImagesChanged;
        }

        SyncPrinters(_monitoredPrinters.Current);
    }

    private enum InfoStatus
    {
        None,
        Loading,
        Loaded,
        NotSupported,
        Failed,
    }

    public ReadOnlyObservableCollection<PrinterChoice> Printers { get; }

    /// <summary>
    /// The printer shown. A selector that briefly reports no selection (e.g. while its items are replaced) does not
    /// clear it while printers exist.
    /// </summary>
    public PrinterChoice? SelectedPrinter
    {
        get => _selectedPrinter;
        set
        {
            if (_syncingPrinters || IsDisposed())
            {
                return;
            }

            if (value is null || !_definitions.ContainsKey(value.Id))
            {
                // Tell the selector to show the current printer again.
                OnPropertyChanged();
                return;
            }

            // The user's choice wins over a printer requested from the menu that is still waiting for the printer list.
            _preferredId = null;
            if (string.Equals(value.Id, _selectedPrinter?.Id, StringComparison.OrdinalIgnoreCase))
            {
                SetProperty(ref _selectedPrinter, value);
                return;
            }

            Select(value);
        }
    }

    /// <summary>The picture of the selected printer (T-039, 48 DIP in the header); <see langword="null"/> without a printer.</summary>
    public PrinterPicture? Picture
    {
        get => _picture;
        private set => SetProperty(ref _picture, value);
    }

    /// <summary>More than one printer is configured.</summary>
    public bool HasPrinterSelector
    {
        get => _hasPrinterSelector;
        private set => SetProperty(ref _hasPrinterSelector, value);
    }

    /// <summary>A printer is selected; its values are shown.</summary>
    public bool HasPrinter
    {
        get => _hasPrinter;
        private set
        {
            if (SetProperty(ref _hasPrinter, value))
            {
                OnPropertyChanged(nameof(HasInfoValues));
            }
        }
    }

    /// <summary>"Starting…", "No printer configured" or "Monitoring stopped due to an error"; <see langword="null"/> otherwise.</summary>
    public string? Notice
    {
        get => _notice;
        private set
        {
            if (SetProperty(ref _notice, value))
            {
                OnPropertyChanged(nameof(HasNotice));
            }
        }
    }

    public bool HasNotice => _notice is not null;

    public string StateText
    {
        get => _stateText;
        private set => SetProperty(ref _stateText, value);
    }

    /// <summary>Detail reported with the state (e.g. why the printer is unreachable); technical English text from the client.</summary>
    public string? DetailText
    {
        get => _detailText;
        private set
        {
            if (SetProperty(ref _detailText, value))
            {
                OnPropertyChanged(nameof(HasDetail));
            }
        }
    }

    public bool HasDetail => _detailText is not null;

    public string LastUpdateText
    {
        get => _lastUpdateText;
        private set => SetProperty(ref _lastUpdateText, value);
    }

    public string NozzleActualText
    {
        get => _nozzleActualText;
        private set => SetProperty(ref _nozzleActualText, value);
    }

    public string NozzleTargetText
    {
        get => _nozzleTargetText;
        private set => SetProperty(ref _nozzleTargetText, value);
    }

    public string BedActualText
    {
        get => _bedActualText;
        private set => SetProperty(ref _bedActualText, value);
    }

    public string BedTargetText
    {
        get => _bedTargetText;
        private set => SetProperty(ref _bedTargetText, value);
    }

    public string HotendFanText
    {
        get => _hotendFanText;
        private set => SetProperty(ref _hotendFanText, value);
    }

    public string PrintFanText
    {
        get => _printFanText;
        private set => SetProperty(ref _printFanText, value);
    }

    public string SpeedText
    {
        get => _speedText;
        private set => SetProperty(ref _speedText, value);
    }

    public string FlowText
    {
        get => _flowText;
        private set => SetProperty(ref _flowText, value);
    }

    public string AxisZText
    {
        get => _axisZText;
        private set => SetProperty(ref _axisZText, value);
    }

    /// <summary>"Loading…", "Could not be read: …" or "Not available for Prusa Connect"; <see langword="null"/> once loaded.</summary>
    public string? InfoStatusText
    {
        get => _infoStatusText;
        private set
        {
            if (SetProperty(ref _infoStatusText, value))
            {
                OnPropertyChanged(nameof(HasInfoStatus));
                OnPropertyChanged(nameof(HasInfoValues));
            }
        }
    }

    public bool HasInfoStatus => _infoStatusText is not null;

    /// <summary>
    /// The information of the selected printer is loaded: its label/value pairs are shown instead of the status text
    /// (T-041, footer of the window).
    /// </summary>
    public bool HasInfoValues => _hasPrinter && _infoStatusText is null;

    public string FirmwareText
    {
        get => _firmwareText;
        private set => SetProperty(ref _firmwareText, value);
    }

    /// <summary>Model code reported by the firmware ("type.version.subversion", e.g. 7.0.0), shown as is.</summary>
    public string ModelText
    {
        get => _modelText;
        private set => SetProperty(ref _modelText, value);
    }

    public string HostnameText
    {
        get => _hostnameText;
        private set => SetProperty(ref _hostnameText, value);
    }

    public string NozzleDiameterText
    {
        get => _nozzleDiameterText;
        private set => SetProperty(ref _nozzleDiameterText, value);
    }

    public string MmuText
    {
        get => _mmuText;
        private set => SetProperty(ref _mmuText, value);
    }

    /// <summary>Source of the temperature chart.</summary>
    public TelemetryHistory History { get; }

    /// <summary>Clock of the chart's time axis.</summary>
    public TimeProvider Clock { get; }

    /// <summary>Printer whose history the chart shows; <see langword="null"/> = none.</summary>
    public string? ChartPrinterId
    {
        get => _chartPrinterId;
        private set => SetProperty(ref _chartPrinterId, value);
    }

    public IReadOnlyList<ChartWindowOption> TimeWindows { get; }

    /// <summary>Time range of the chart; a selector reporting no selection does not change it.</summary>
    public ChartWindowOption SelectedTimeWindow
    {
        get => _selectedTimeWindow;
        set
        {
            if (value is null)
            {
                OnPropertyChanged();
                return;
            }

            SetProperty(ref _selectedTimeWindow, value);
        }
    }

    /// <summary>
    /// UI thread: shows <paramref name="printerId"/> (opened from the printer's submenu while the window is open). An id
    /// that is not configured yet is remembered until the next printer list arrives; the current printer stays otherwise.
    /// </summary>
    public void ShowPrinter(string printerId)
    {
        ArgumentNullException.ThrowIfNull(printerId);
        if (IsDisposed())
        {
            return;
        }

        PrinterChoice? choice = FindChoice(_printers, printerId);
        if (choice is null)
        {
            _preferredId = printerId;
            return;
        }

        _preferredId = null;
        if (!string.Equals(choice.Id, _selectedPrinter?.Id, StringComparison.OrdinalIgnoreCase))
        {
            Select(choice);
        }
    }

    /// <summary>UI thread: the language changed; every text is formatted again from what is shown (no new request).</summary>
    public void RefreshTexts()
    {
        if (IsDisposed())
        {
            return;
        }

        foreach (ChartWindowOption option in TimeWindows)
        {
            option.RefreshText();
        }

        Notice = _noticeSource?.Invoke();
        InfoStatusText = _infoStatusSource?.Invoke();
        FormatContent(_shownContent);
        ShowInfo(_shownInfo);
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
        _monitoredPrinters.Changed -= OnPrintersChanged;
        if (_images is not null)
        {
            _images.Changed -= OnImagesChanged;
        }

        _infoCancellation?.Cancel();
        _infoCancellation = null;
    }

    private static PrinterChoice? FindChoice(IReadOnlyList<PrinterChoice> choices, string id)
    {
        foreach (PrinterChoice choice in choices)
        {
            if (string.Equals(choice.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return choice;
            }
        }

        return null;
    }

    private static bool IsSameMachine(PrinterDefinition a, PrinterDefinition b) =>
        a.ConnectionType == b.ConnectionType && string.Equals(a.Address, b.Address, StringComparison.OrdinalIgnoreCase);

    private bool IsDisposed()
    {
        lock (_gate)
        {
            return _disposed;
        }
    }

    private void OnSnapshotUpdated(object? sender, PrinterSnapshot snapshot)
    {
        if (snapshot is null || !string.Equals(snapshot.PrinterId, Volatile.Read(ref _selectedId), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        DiagnosticsContent content = DiagnosticsContent.From(snapshot);
        lock (_gate)
        {
            // The selection may have changed since the check above.
            if (_disposed || !string.Equals(snapshot.PrinterId, _selectedId, StringComparison.OrdinalIgnoreCase) || _content == content)
            {
                return;
            }

            _content = content;
            _contentDirty = true;
            ScheduleApplyLocked();
        }
    }

    private void OnPrintersChanged(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _printersDirty = true;
            ScheduleApplyLocked();
        }
    }

    /// <summary>Any thread: a custom image was saved or removed in the settings window.</summary>
    private void OnImagesChanged(object? sender, EventArgs e) => _dispatcher.Post(() =>
    {
        if (!IsDisposed())
        {
            UpdatePicture();
        }
    });

    /// <summary>UI thread: the picture of the selected printer from its definition and its stored custom image.</summary>
    private void UpdatePicture() =>
        Picture = _selectedPrinter is { } selected && _definitions.TryGetValue(selected.Id, out PrinterDefinition? definition)
            ? _images?.PictureOf(definition) ?? PrinterPicture.For(definition, customPng: null)
            : null;

    private void ScheduleApplyLocked()
    {
        if (!_applyPending)
        {
            _applyPending = true;
            _dispatcher.Post(_apply);
        }
    }

    /// <summary>UI thread: applies everything that changed since the last update.</summary>
    private void Apply()
    {
        bool printersDirty;
        lock (_gate)
        {
            _applyPending = false;
            if (_disposed)
            {
                return;
            }

            printersDirty = _printersDirty;
            _printersDirty = false;
        }

        if (printersDirty)
        {
            // May select another printer, which shows that printer's content right away.
            SyncPrinters(_monitoredPrinters.Current);
        }

        DiagnosticsContent? content;
        lock (_gate)
        {
            if (!_contentDirty)
            {
                return;
            }

            _contentDirty = false;
            content = _content;
        }

        ShowContent(content);
    }

    /// <summary>UI thread: mirrors the configured printers with the fewest collection changes and keeps the selection when possible.</summary>
    private void SyncPrinters(MonitoredPrintersState state)
    {
        _noticeSource = state.MonitoringFailed ? static () => Strings.ToolTipMonitoringFailed
            : state.Printers is null ? static () => Strings.ToolTipStarting
            : state.Printers.Count == 0 ? static () => Strings.ToolTipNoPrinter
            : null;
        Notice = _noticeSource?.Invoke();

        IReadOnlyList<PrinterDefinition> printers = state.Printers ?? [];
        Dictionary<string, PrinterDefinition> definitions = new(printers.Count, StringComparer.OrdinalIgnoreCase);
        List<PrinterChoice> choices = new(printers.Count);
        foreach (PrinterDefinition printer in printers)
        {
            // Ids are unique (the monitor rejects duplicates); a duplicate would only be shown once.
            if (definitions.TryAdd(printer.Id, printer))
            {
                choices.Add(new PrinterChoice(printer.Id, printer.Name));
            }
        }

        PrinterDefinition? previous = _selectedPrinter is { } selected ? _definitions.GetValueOrDefault(selected.Id) : null;
        _definitions = definitions;
        bool collectionChanged;
        _syncingPrinters = true;
        try
        {
            collectionChanged = Synchronize(choices);
        }
        finally
        {
            _syncingPrinters = false;
        }

        HasPrinterSelector = _printers.Count > 1;

        PrinterChoice? kept = _selectedPrinter is { } current ? FindChoice(choices, current.Id) : null;
        PrinterChoice? preferred = _preferredId is { } preferredId ? FindChoice(choices, preferredId) : null;
        if (state.Printers is not null)
        {
            // A printer list is known: the preference is either applied now or it names a printer that is not configured.
            _preferredId = null;
        }

        if (preferred is not null && (kept is null || !string.Equals(preferred.Id, kept.Id, StringComparison.OrdinalIgnoreCase)))
        {
            Select(preferred);
            return;
        }

        if (kept is null)
        {
            Select(choices.Count > 0 ? choices[0] : null);
            return;
        }

        // Same printer: the name may have changed; raised also when the selector lost its item while items were replaced.
        if (!SetProperty(ref _selectedPrinter, kept, nameof(SelectedPrinter)) && collectionChanged)
        {
            OnPropertyChanged(nameof(SelectedPrinter));
        }

        PrinterDefinition definition = definitions[kept.Id];
        if (previous is null || !IsSameMachine(previous, definition))
        {
            // Another machine behind the same id: its information may differ.
            LoadInfo(definition);
        }

        // A detected model or a new image (T-039) changes the picture without restarting anything.
        UpdatePicture();
    }

    /// <summary>Replaces differing items, then adds or removes at the end; returns whether anything changed.</summary>
    private bool Synchronize(List<PrinterChoice> choices)
    {
        bool changed = false;
        int common = Math.Min(choices.Count, _printers.Count);
        for (int i = 0; i < common; i++)
        {
            if (!_printers[i].Equals(choices[i]))
            {
                _printers[i] = choices[i];
                changed = true;
            }
        }

        for (int i = common; i < choices.Count; i++)
        {
            _printers.Add(choices[i]);
            changed = true;
        }

        while (_printers.Count > choices.Count)
        {
            _printers.RemoveAt(_printers.Count - 1);
            changed = true;
        }

        return changed;
    }

    /// <summary>UI thread: shows another printer (or none) with its latest snapshot, chart and information.</summary>
    private void Select(PrinterChoice? choice)
    {
        _selectedPrinter = choice;
        OnPropertyChanged(nameof(SelectedPrinter));
        HasPrinter = choice is not null;
        ChartPrinterId = choice?.Id;

        lock (_gate)
        {
            Volatile.Write(ref _selectedId, choice?.Id);
            _content = null;
            _contentDirty = false;
        }

        // The latest snapshot, unless a newer one arrived for the new printer in the meantime.
        DiagnosticsContent? initial = choice is not null && _monitor.TryGetSnapshot(choice.Id, out PrinterSnapshot? snapshot)
            ? DiagnosticsContent.From(snapshot)
            : null;
        lock (_gate)
        {
            _content ??= initial;
            _contentDirty = false;
            initial = _content;
        }

        LoadInfo(choice is not null ? _definitions.GetValueOrDefault(choice.Id) : null, knownState: initial?.State);
        UpdatePicture();
        ShowContent(initial);
    }

    private void ShowContent(DiagnosticsContent? value)
    {
        FormatContent(value);
        if (value is not { } content)
        {
            return;
        }

        // Failed information is read again once, on the next reachable snapshot: after an outage (e.g. the window
        // opened while the printer was off) and also when it failed while the printer answered polls (request in
        // flight during start, transient error). When that retry fails too, polls of the still reachable printer ask
        // no more; the next outage allows one more retry. So there is never a request per poll.
        if (!content.State.IsReachable())
        {
            _infoRetryUsed = false;
        }
        else if (_infoStatus == InfoStatus.Failed && !_infoRetryUsed && _selectedPrinter is { } selected)
        {
            LoadInfo(_definitions.GetValueOrDefault(selected.Id), isRetry: true);
        }
    }

    /// <summary>Only the texts of <paramref name="value"/>; also used again after a language switch.</summary>
    private void FormatContent(DiagnosticsContent? value)
    {
        _shownContent = value;
        if (value is not { } content)
        {
            StateText = TrayText.StateName(null);
            DetailText = null;
            LastUpdateText = DiagnosticsFormatter.Missing;
            NozzleActualText = NozzleTargetText = BedActualText = BedTargetText = DiagnosticsFormatter.Missing;
            HotendFanText = PrintFanText = SpeedText = FlowText = AxisZText = DiagnosticsFormatter.Missing;
            return;
        }

        StateText = TrayText.StateName(content.State);
        DetailText = DiagnosticsFormatter.Problem(content.Problem) ?? content.Message;
        LastUpdateText = DiagnosticsFormatter.Timestamp(content.Timestamp, Clock.GetUtcNow(), Clock.LocalTimeZone);
        NozzleActualText = DiagnosticsFormatter.Temperature(content.NozzleActual);
        NozzleTargetText = DiagnosticsFormatter.TargetTemperature(content.NozzleTarget);
        BedActualText = DiagnosticsFormatter.Temperature(content.BedActual);
        BedTargetText = DiagnosticsFormatter.TargetTemperature(content.BedTarget);
        HotendFanText = DiagnosticsFormatter.Rpm(content.HotendFanRpm);
        PrintFanText = DiagnosticsFormatter.Rpm(content.PrintFanRpm);
        SpeedText = DiagnosticsFormatter.Percent(content.SpeedPercent);
        FlowText = DiagnosticsFormatter.Percent(content.FlowPercent);
        AxisZText = DiagnosticsFormatter.Millimetres(content.AxisZ, DiagnosticsFormatter.AxisZDecimals);
    }

    /// <summary>UI thread: cancels a pending request and asks for the information of <paramref name="printer"/>.</summary>
    /// <param name="isRetry">Another attempt for the same printer; otherwise the retry allowance starts anew.</param>
    /// <param name="knownState">
    /// The state of the latest snapshot: an offline (not reachable) printer is not asked at all (T-044: a refused connection
    /// can take seconds on Windows); the failure is shown at once and the information is read on the next reachable snapshot.
    /// </param>
    private void LoadInfo(PrinterDefinition? printer, bool isRetry = false, PrinterState? knownState = null)
    {
        // Cancelled, not disposed: the request may still observe the token while it unwinds.
        _infoCancellation?.Cancel();
        _infoCancellation = null;
        int request = ++_infoRequest;
        _infoRetryUsed = isRetry;
        ShowInfo(null);
        if (printer is null)
        {
            _infoStatus = InfoStatus.None;
            SetInfoStatus(null);
            return;
        }

        if (knownState == PrinterState.Offline)
        {
            _infoStatus = InfoStatus.Failed;
            SetInfoStatus(static () => DiagnosticsFormatter.InfoUnavailable(PrinterState.Offline));
            return;
        }

        _infoStatus = InfoStatus.Loading;
        SetInfoStatus(static () => Strings.DiagnosticsInfoLoading);
        CancellationTokenSource cancellation = new();
        _infoCancellation = cancellation;
        _ = FetchInfoAsync(printer, request, cancellation.Token);
    }

    /// <summary>Never throws; the result is applied on the UI thread unless it was superseded or the window closed.</summary>
    private async Task FetchInfoAsync(PrinterDefinition printer, int request, CancellationToken cancellationToken)
    {
        PrusaLinkInfoResult? result;
        try
        {
            result = await _infoSource.GetInfoAsync(printer, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception)
        {
            // Expected failures come back in the result; anything else is shown as an unknown failure.
            result = new PrusaLinkInfoResult(PrinterState.Unknown, null, null);
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            _dispatcher.Post(() => ApplyInfo(request, result));
        }
    }

    private void ApplyInfo(int request, PrusaLinkInfoResult? result)
    {
        if (request != _infoRequest || IsDisposed())
        {
            return;
        }

        if (result is null)
        {
            _infoStatus = InfoStatus.NotSupported;
            SetInfoStatus(static () => Strings.DiagnosticsInfoNotSupported);
        }
        else if (result.Info is { } info)
        {
            _infoStatus = InfoStatus.Loaded;
            SetInfoStatus(null);
            ShowInfo(info);
        }
        else
        {
            _infoStatus = InfoStatus.Failed;
            PrinterState state = result.State;
            SetInfoStatus(() => DiagnosticsFormatter.InfoUnavailable(state));
        }
    }

    private void SetInfoStatus(Func<string>? source)
    {
        _infoStatusSource = source;
        InfoStatusText = source?.Invoke();
    }

    private void ShowInfo(PrusaLinkPrinterInfo? info)
    {
        _shownInfo = info;
        FirmwareText = DiagnosticsFormatter.Text(info?.Firmware);
        ModelText = DiagnosticsFormatter.Text(info?.PrinterModel);
        HostnameText = DiagnosticsFormatter.Text(info?.Hostname);
        NozzleDiameterText = DiagnosticsFormatter.Millimetres(info?.NozzleDiameter, DiagnosticsFormatter.NozzleDiameterDecimals);
        MmuText = info is null ? DiagnosticsFormatter.Missing : DiagnosticsFormatter.YesNo(info.HasMmu);
    }
}
