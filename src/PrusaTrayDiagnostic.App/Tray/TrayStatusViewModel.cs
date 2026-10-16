using CommunityToolkit.Mvvm.ComponentModel;

using PrusaTrayDiagnostic.Core.Monitoring;
using PrusaTrayDiagnostic.Core.Printers;

namespace PrusaTrayDiagnostic.App.Tray;

/// <summary>
/// State of the tray icon (which icon, which tooltip), fed by <see cref="IPrinterMonitor"/>.
/// Snapshots arrive on background threads. Each one only updates a cheap <see cref="TrayPresentationKey"/> under the
/// lock; the tooltip text is formatted (outside the lock) only when that key changed, and handed to the UI thread.
/// Bursts are coalesced into one UI update that applies the latest presentation.
/// Properties change and <see cref="ObservableObject.PropertyChanged"/> is raised on the UI thread only.
/// </summary>
internal sealed class TrayStatusViewModel : ObservableObject, ITrayStatusSource, IMonitoringObserver, IDisposable
{
    private readonly IPrinterMonitor _monitor;
    private readonly IUiDispatcher _dispatcher;
    private readonly Action _apply;
    private readonly Lock _gate = new();

    // Guarded by _gate.
    private TrayPrinter[]? _printers;
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

    public TrayStatusViewModel(IPrinterMonitor monitor, IUiDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(dispatcher);
        _monitor = monitor;
        _dispatcher = dispatcher;
        _apply = Apply;
        _latestKey = TrayPresentationKey.Create(printers: null, monitoringFailed: false);
        _latest = TrayPresentation.Format(_latestKey);
        _iconKey = _latest.IconKey;
        _toolTipText = _latest.ToolTipText;
        _monitor.SnapshotUpdated += OnSnapshotUpdated;
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

    /// <summary>
    /// Starts showing <paramref name="printers"/> (configuration order), all waiting for their first snapshot.
    /// Clears a previous <see cref="ReportMonitoringFailed"/>. Thread-safe.
    /// </summary>
    public void SetPrinters(IReadOnlyList<PrinterDefinition> printers)
    {
        ArgumentNullException.ThrowIfNull(printers);
        TrayPrinter[] entries = new TrayPrinter[printers.Count];
        Dictionary<string, int> slotById = new(printers.Count, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < entries.Length; i++)
        {
            PrinterDefinition printer = printers[i] ?? throw new ArgumentException("Printer list must not contain null entries.", nameof(printers));
            entries[i] = new TrayPrinter(printer, Snapshot: null);
            slotById.TryAdd(printer.Id, i);
        }

        TrayPresentationKey key;
        long version;
        lock (_gate)
        {
            _printers = entries;
            _slotById = slotById;
            _monitoringFailed = false;
            if (!TryAdvanceLocked(out key, out version))
            {
                return;
            }
        }

        Publish(key, version);
    }

    /// <summary>Shows that monitoring stopped unexpectedly, until the next <see cref="SetPrinters"/>. Thread-safe.</summary>
    public void ReportMonitoringFailed()
    {
        TrayPresentationKey key;
        long version;
        lock (_gate)
        {
            _monitoringFailed = true;
            if (!TryAdvanceLocked(out key, out version))
            {
                return;
            }
        }

        Publish(key, version);
    }

    public void Dispose()
    {
        _monitor.SnapshotUpdated -= OnSnapshotUpdated;
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
        lock (_gate)
        {
            // Snapshots of printers that are not (or no longer) configured are ignored.
            if (_printers is null || !_slotById.TryGetValue(snapshot.PrinterId, out int slot))
            {
                return;
            }

            _printers[slot] = _printers[slot] with { Snapshot = snapshot };
            if (!TryAdvanceLocked(out key, out version))
            {
                return;
            }
        }

        Publish(key, version);
    }

    /// <summary>Builds the key of the current state; <see langword="false"/> when nothing visible changed.</summary>
    private bool TryAdvanceLocked(out TrayPresentationKey key, out long version)
    {
        key = TrayPresentationKey.Create(_printers, _monitoringFailed);
        version = 0;
        if (_disposed || key == _latestKey)
        {
            return false;
        }

        _latestKey = key;
        version = ++_keyVersion;
        return true;
    }

    /// <summary>Formats outside the lock; only the newest key is published, an outdated result is dropped.</summary>
    private void Publish(TrayPresentationKey key, long version)
    {
        TrayPresentation presentation;
        try
        {
            presentation = TrayPresentation.Format(key);
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
