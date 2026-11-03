using CommunityToolkit.Mvvm.ComponentModel;

using LayerPeek.App.Cameras.Recording;
using LayerPeek.App.Localization;
using LayerPeek.Core.Monitoring;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.Tray;

/// <summary>
/// State of the tray icon (which icon, which tooltip), fed by <see cref="IPrinterMonitor"/>.
/// Snapshots arrive on background threads. Each one only updates a cheap <see cref="TrayPresentationKey"/> under the
/// lock; the tooltip text is formatted (outside the lock) only when that key changed, and handed to the UI thread.
/// Bursts are coalesced into one UI update that applies the latest presentation.
/// With several printers the icon follows the most severe printer (<see cref="TrayStatusRules.SelectPrimary"/>, O(n)
/// without allocation per poll) and the tooltip has one line per printer; a poll compares only the content of its own
/// printer and bumps a version when it changed, so the n lines are copied and formatted only after a visible change.
/// T-146: a printer whose camera is being recorded says so in the tooltip ("Recording"); the icon does not change (user
/// decision 2026-11-03). A recording change (rare) is handled like a snapshot of that printer.
/// Properties change and <see cref="ObservableObject.PropertyChanged"/> is raised on the UI thread only.
/// </summary>
internal sealed class TrayStatusViewModel : ObservableObject, ITrayStatusSource, IMonitoringObserver, IDisposable, ILanguageAware
{
    private readonly IPrinterMonitor _monitor;
    private readonly IUiDispatcher _dispatcher;
    private readonly Action _apply;
    private readonly Lock _gate = new();
    private readonly ICameraRecordings? _recordings;

    // Guarded by _gate.
    private TrayPrinter[]? _printers;

    // What the tooltip shows for each printer (parallel to _printers); a change bumps _summaryVersion.
    private ToolTipContent[] _contents = [];
    private long _summaryVersion;
    private Dictionary<string, int> _slotById = new(StringComparer.OrdinalIgnoreCase);
    private bool _monitoringFailed;
    private TrayPresentationKey _latestKey;
    private long _keyVersion;
    private TrayPresentation _latest;
    private bool _applyPending;
    private bool _disposed;

    // UI thread only.
    private TrayIconKey _iconKey;
    private string _toolTipText;

    /// <param name="recordings">The camera recordings (T-146); <see langword="null"/>: the tooltip never says "Recording".</param>
    public TrayStatusViewModel(IPrinterMonitor monitor, IUiDispatcher dispatcher, ICameraRecordings? recordings = null)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(dispatcher);
        _monitor = monitor;
        _dispatcher = dispatcher;
        _recordings = recordings;
        _apply = Apply;
        _latestKey = TrayPresentationKey.Create(printers: null, monitoringFailed: false);
        _latest = TrayPresentation.Format(_latestKey);
        _iconKey = _latest.IconKey;
        _toolTipText = _latest.ToolTipText;
        _monitor.SnapshotUpdated += OnSnapshotUpdated;
        if (_recordings is not null)
        {
            _recordings.Changed += OnRecordingChanged;
        }
    }

    public TrayIconKey IconKey
    {
        get => _iconKey;
        private set => SetProperty(ref _iconKey, value);
    }

    public string ToolTipText
    {
        get => _toolTipText;
        private set => SetProperty(ref _toolTipText, value);
    }

    /// <inheritdoc/>
    void IMonitoringObserver.ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        SetPrinters(settings.Printers);
    }

    /// <summary>
    /// Starts showing <paramref name="printers"/> (configuration order), all waiting for their first snapshot.
    /// Clears a previous <see cref="ReportMonitoringFailed"/>. Thread-safe.
    /// </summary>
    public void SetPrinters(IReadOnlyList<PrinterDefinition> printers)
    {
        ArgumentNullException.ThrowIfNull(printers);
        TrayPrinter[] entries = new TrayPrinter[printers.Count];
        ToolTipContent[] contents = new ToolTipContent[printers.Count];
        Dictionary<string, int> slotById = new(printers.Count, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < entries.Length; i++)
        {
            PrinterDefinition printer = printers[i] ?? throw new ArgumentException("Printer list must not contain null entries.", nameof(printers));
            entries[i] = new TrayPrinter(printer, Snapshot: null);
            contents[i] = ToolTipContent.From(entries[i], otherPrinterCount: 0);
            slotById.TryAdd(printer.Id, i);
        }

        TrayPresentationKey key;
        long version;
        ToolTipContent[]? summary;
        lock (_gate)
        {
            // Recordings outlive a restart of monitoring (T-145): a running one shows right away. The service never calls
            // back while it holds its own lock, so asking it under this lock cannot deadlock (and orders it with OnRecordingChanged).
            for (int i = 0; _recordings is not null && i < entries.Length; i++)
            {
                if (IsRecordingLocked(entries[i].Definition.Id))
                {
                    entries[i] = entries[i] with { IsRecording = true };
                    contents[i] = ToolTipContent.From(entries[i], otherPrinterCount: 0);
                }
            }

            _printers = entries;
            _contents = contents;
            _summaryVersion++;
            _slotById = slotById;
            _monitoringFailed = false;
            if (!TryAdvanceLocked(out key, out version, out summary))
            {
                return;
            }
        }

        Publish(key, version, summary);
    }

    /// <summary>Shows that monitoring stopped unexpectedly, until the next <see cref="SetPrinters"/>. Thread-safe.</summary>
    public void ReportMonitoringFailed()
    {
        TrayPresentationKey key;
        long version;
        ToolTipContent[]? summary;
        lock (_gate)
        {
            _monitoringFailed = true;
            if (!TryAdvanceLocked(out key, out version, out summary))
            {
                return;
            }
        }

        Publish(key, version, summary);
    }

    /// <summary>
    /// The language changed: the tooltip of the current state is formatted again and applied through the usual UI update
    /// (a result formatted concurrently with the old language is outdated by the new version and dropped). Thread-safe.
    /// </summary>
    public void RefreshTexts()
    {
        TrayPresentationKey key;
        long version;
        ToolTipContent[]? summary = null;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            key = _latestKey;
            version = ++_keyVersion;
            if (key.Message == TrayMessage.Summary)
            {
                summary = [.. _contents];
            }
        }

        Publish(key, version, summary);
    }

    public void Dispose()
    {
        _monitor.SnapshotUpdated -= OnSnapshotUpdated;
        if (_recordings is not null)
        {
            _recordings.Changed -= OnRecordingChanged;
        }

        lock (_gate)
        {
            _disposed = true;
        }
    }

    private void OnSnapshotUpdated(object? sender, PrinterSnapshot snapshot)
    {
        if (snapshot is null)
        {
            return;
        }

        TrayPresentationKey key;
        long version;
        ToolTipContent[]? summary;
        lock (_gate)
        {
            // Snapshots of printers that are not (or no longer) configured are ignored.
            if (_printers is null || !_slotById.TryGetValue(snapshot.PrinterId, out int slot))
            {
                return;
            }

            TrayPrinter printer = _printers[slot] with { Snapshot = snapshot };
            _printers[slot] = printer;

            // O(1): only this printer's line can have changed (no allocation, nothing formatted).
            ToolTipContent content = ToolTipContent.From(printer, otherPrinterCount: 0);
            if (content != _contents[slot])
            {
                _contents[slot] = content;
                _summaryVersion++;
            }

            // O(n) without allocation: the icon follows the most severe printer.
            if (!TryAdvanceLocked(out key, out version, out summary))
            {
                return;
            }
        }

        Publish(key, version, summary);
    }

    /// <summary>Any thread (T-146): the recording of a printer started or ended; only its tooltip line can change.</summary>
    private void OnRecordingChanged(object? sender, string printerId)
    {
        if (printerId is null)
        {
            return;
        }

        TrayPresentationKey key;
        long version;
        ToolTipContent[]? summary;
        lock (_gate)
        {
            if (_disposed || _printers is null || !_slotById.TryGetValue(printerId, out int slot))
            {
                return;
            }

            bool recording = IsRecordingLocked(printerId);
            if (_printers[slot].IsRecording == recording)
            {
                return;
            }

            TrayPrinter printer = _printers[slot] with { IsRecording = recording };
            _printers[slot] = printer;
            _contents[slot] = ToolTipContent.From(printer, otherPrinterCount: 0);
            _summaryVersion++;
            if (!TryAdvanceLocked(out key, out version, out summary))
            {
                return;
            }
        }

        Publish(key, version, summary);
    }

    /// <summary>Starting, recording or stopping: "Recording" shows, like the REC dot of the camera window.</summary>
    private bool IsRecordingLocked(string printerId) => _recordings?.GetStatus(printerId).IsActive == true;

    /// <summary>
    /// Builds the key of the current state; <see langword="false"/> when nothing visible changed. For several printers
    /// <paramref name="summary"/> is a copy of every printer's content (allocated only when something changed).
    /// </summary>
    private bool TryAdvanceLocked(out TrayPresentationKey key, out long version, out ToolTipContent[]? summary)
    {
        key = TrayPresentationKey.Create(_printers, _monitoringFailed, _summaryVersion);
        version = 0;
        summary = null;
        if (_disposed || key == _latestKey)
        {
            return false;
        }

        _latestKey = key;
        version = ++_keyVersion;
        if (key.Message == TrayMessage.Summary)
        {
            summary = [.. _contents];
        }

        return true;
    }

    /// <summary>Formats outside the lock; only the newest key is published, an outdated result is dropped.</summary>
    private void Publish(TrayPresentationKey key, long version, ToolTipContent[]? summary)
    {
        TrayPresentation presentation;
        try
        {
            presentation = TrayPresentation.Format(key, summary);
        }
        catch
        {
            lock (_gate)
            {
                // Forget the key so that the next poll tries again instead of being treated as unchanged.
                if (version == _keyVersion)
                {
                    _latestKey = default;
                }
            }

            throw;
        }

        lock (_gate)
        {
            if (_disposed || version != _keyVersion)
            {
                return;
            }

            _latest = presentation;
            if (!_applyPending)
            {
                _applyPending = true;
                _dispatcher.Post(_apply);
            }
        }
    }

    private void Apply()
    {
        TrayPresentation presentation;
        lock (_gate)
        {
            _applyPending = false;
            if (_disposed)
            {
                return;
            }

            presentation = _latest;
        }

        // Outside the lock: PropertyChanged handlers may do UI work.
        IconKey = presentation.IconKey;
        ToolTipText = presentation.ToolTipText;
    }
}
