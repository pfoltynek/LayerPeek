using Microsoft.Extensions.Logging;

using LayerPeek.Core.Monitoring;
using LayerPeek.Core.Printers;

namespace LayerPeek.Core.Cooldown;

/// <summary>
/// Cool-down state of every configured printer (T-079): runs the snapshots of <see cref="IPrinterMonitor"/> through one
/// <see cref="CooldownTracker"/> per printer, with the threshold of each printer
/// (<see cref="PrinterDefinition.CooldownThresholdCelsius"/>). Nothing is tracked until <see cref="Configure"/> is called.
/// <para>
/// Thread-safe: the monitor publishes printers concurrently, each printer's snapshots in order. Tracking is serialized
/// under a lock (cheap, no I/O, no allocation per snapshot); <see cref="Changed"/> and <see cref="CooledDown"/> are raised
/// outside of it on the publishing thread (or the one calling <see cref="Configure"/>). A failing subscriber is logged
/// and affects neither other subscribers nor monitoring.
/// </para>
/// </summary>
public sealed partial class CooldownMonitor : IDisposable
{
    private readonly IPrinterMonitor _monitor;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CooldownMonitor> _logger;
    private readonly CooldownTrackerOptions _options;
    private readonly Lock _gate = new();

    // Guarded by _gate.
    private Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public CooldownMonitor(IPrinterMonitor monitor, TimeProvider timeProvider, ILogger<CooldownMonitor> logger, CooldownTrackerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _monitor = monitor;
        _timeProvider = timeProvider;
        _logger = logger;
        _options = options ?? CooldownTrackerOptions.Default;
        _monitor.SnapshotUpdated += OnSnapshotUpdated;
    }

    /// <summary>
    /// The state of a printer changed (id as configured); read it with <see cref="GetState"/>. Raised for every change
    /// while cooling (temperatures, progress, estimate), when a cool-down starts or ends, when the threshold of a cooling
    /// printer changes and when a cooling printer is forgotten by <see cref="Configure"/>.
    /// </summary>
    public event EventHandler<string>? Changed;

    /// <summary>A printer cooled down after a print: at most once per print, never for a print that ended already cool.</summary>
    public event EventHandler<CooldownEvent>? CooledDown;

    /// <summary>
    /// Tracks <paramref name="printers"/> from now on, each with its own threshold. Call before every monitoring run and
    /// whenever the settings change. Printers that stay configured and point to the same machine (a rename keeps it) keep
    /// their state, and a changed threshold applies right away (see <see cref="CooldownTracker.SetThreshold"/>). Removed
    /// printers and printers whose address or connection type changed are forgotten.
    /// </summary>
    public void Configure(IReadOnlyList<PrinterDefinition> printers)
    {
        ArgumentNullException.ThrowIfNull(printers);
        foreach (PrinterDefinition? printer in printers)
        {
            if (printer is null)
            {
                throw new ArgumentException("Printer list must not contain null entries.", nameof(printers));
            }
        }

        List<string>? changed = null;
        List<CooldownEvent>? cooled = null;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            Dictionary<string, Entry> entries = new(printers.Count, StringComparer.OrdinalIgnoreCase);
            foreach (PrinterDefinition printer in printers)
            {
                if (entries.ContainsKey(printer.Id))
                {
                    continue;
                }

                if (_entries.TryGetValue(printer.Id, out Entry? entry) && IsSameMachine(entry.Printer, printer))
                {
                    CooldownState before = entry.Tracker.State;
                    CooldownUpdate update = entry.Tracker.SetThreshold(printer.CooldownThresholdCelsius);
                    entry.Printer = printer;
                    if (update.State != before)
                    {
                        (changed ??= []).Add(printer.Id);
                    }

                    if (update.Event is { } cooledDown)
                    {
                        (cooled ??= []).Add(cooledDown with { PrinterId = printer.Id });
                    }
                }
                else
                {
                    entry = new Entry(printer, new CooldownTracker(printer.CooldownThresholdCelsius, _options));
                }

                entries.Add(printer.Id, entry);
            }

            foreach (Entry previous in _entries.Values)
            {
                bool kept = entries.TryGetValue(previous.Printer.Id, out Entry? current) && ReferenceEquals(current, previous);
                if (!kept && previous.Tracker.State.Phase != CooldownPhase.None)
                {
                    (changed ??= []).Add(previous.Printer.Id);
                }
            }

            _entries = entries;
        }

        if (changed is not null)
        {
            foreach (string printerId in changed)
            {
                RaiseChanged(printerId);
            }
        }

        if (cooled is not null)
        {
            foreach (CooldownEvent cooledDown in cooled)
            {
                RaiseCooledDown(cooledDown);
            }
        }
    }

    /// <summary>
    /// The current state of <paramref name="printerId"/> (case-insensitive); <see cref="CooldownState.None"/> for printers
    /// that are not configured. The estimate counts down by the time elapsed since the snapshot it was made from
    /// (<see cref="TimeProvider"/>), down to zero.
    /// </summary>
    public CooldownState GetState(string printerId)
    {
        ArgumentNullException.ThrowIfNull(printerId);
        CooldownState state;
        lock (_gate)
        {
            if (_disposed || !_entries.TryGetValue(printerId, out Entry? entry))
            {
                return CooldownState.None;
            }

            state = entry.Tracker.State;
        }

        if (state is { EstimatedRemaining: { } remaining, UpdatedAt: { } updatedAt })
        {
            TimeSpan elapsed = _timeProvider.GetUtcNow() - updatedAt;
            if (elapsed > TimeSpan.Zero)
            {
                state = state with { EstimatedRemaining = elapsed >= remaining ? TimeSpan.Zero : remaining - elapsed };
            }
        }

        return state;
    }

    public void Dispose()
    {
        _monitor.SnapshotUpdated -= OnSnapshotUpdated;
        lock (_gate)
        {
            _disposed = true;
            _entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        }
    }

    internal void OnSnapshotUpdated(object? sender, PrinterSnapshot snapshot)
    {
        if (snapshot is null)
        {
            return;
        }

        string printerId;
        bool changed;
        CooldownEvent? cooled;
        lock (_gate)
        {
            // Snapshots of printers that are not (or no longer) configured are ignored.
            if (_disposed || !_entries.TryGetValue(snapshot.PrinterId, out Entry? entry))
            {
                return;
            }

            printerId = entry.Printer.Id;
            CooldownState before = entry.Tracker.State;
            try
            {
                CooldownUpdate update = entry.Tracker.Process(snapshot);
                changed = update.State != before;
                cooled = update.Event;
            }
            catch (Exception ex)
            {
                // Defensive: the tracker does not throw for any snapshot, but monitoring must never notice if it did.
                LogProcessFailed(ex, printerId);
                return;
            }
        }

        if (changed)
        {
            RaiseChanged(printerId);
        }

        if (cooled is { } cooledDown)
        {
            RaiseCooledDown(cooledDown with { PrinterId = printerId });
        }
    }

    /// <summary>
    /// The printer stays configured and still points to the same machine (a rename keeps it). A changed address or
    /// connection type may be another printer, whose state must start over.
    /// </summary>
    private static bool IsSameMachine(PrinterDefinition before, PrinterDefinition after) =>
        before.ConnectionType == after.ConnectionType && string.Equals(before.Address, after.Address, StringComparison.Ordinal);

    private void RaiseChanged(string printerId)
    {
        EventHandler<string>? handlers = Changed;
        if (handlers is null || IsDisposed())
        {
            return;
        }

        // One by one, without allocating, so that a failing subscriber does not starve the others.
        foreach (EventHandler<string> handler in Delegate.EnumerateInvocationList(handlers))
        {
            try
            {
                handler(this, printerId);
            }
            catch (Exception ex)
            {
                LogSubscriberFailed(ex, nameof(Changed), printerId);
            }
        }
    }

    private void RaiseCooledDown(CooldownEvent cooledDown)
    {
        EventHandler<CooldownEvent>? handlers = CooledDown;
        if (handlers is null || IsDisposed())
        {
            return;
        }

        foreach (EventHandler<CooldownEvent> handler in Delegate.EnumerateInvocationList(handlers))
        {
            try
            {
                handler(this, cooledDown);
            }
            catch (Exception ex)
            {
                LogSubscriberFailed(ex, nameof(CooledDown), cooledDown.PrinterId);
            }
        }
    }

    private bool IsDisposed()
    {
        lock (_gate)
        {
            return _disposed;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Cool-down tracking of printer '{PrinterId}' failed for a snapshot.")]
    private partial void LogProcessFailed(Exception exception, string printerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "A {EventName} subscriber of the cool-down monitor failed for printer '{PrinterId}'.")]
    private partial void LogSubscriberFailed(Exception exception, string eventName, string printerId);

    /// <summary>One configured printer; replaced when it points to another machine.</summary>
    private sealed class Entry(PrinterDefinition printer, CooldownTracker tracker)
    {
        public PrinterDefinition Printer { get; set; } = printer;

        public CooldownTracker Tracker { get; } = tracker;
    }
}
