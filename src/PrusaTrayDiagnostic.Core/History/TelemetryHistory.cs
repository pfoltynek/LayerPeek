using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using PrusaTrayDiagnostic.Core.Printers;

namespace PrusaTrayDiagnostic.Core.History;

/// <summary>
/// In-memory telemetry history of every configured printer (no persistence), e.g. for temperature charts.
/// Each printer has a ring of <see cref="TelemetrySample"/> covering <see cref="TelemetryHistoryOptions.Retention"/>
/// (one day by default); older samples are dropped even before the ring is full.
/// </summary>
/// <remarks>
/// <para>
/// Memory: the capacity is <see cref="CapacityFor"/> = ⌈retention / polling interval⌉ + 1 samples of 48 bytes. The
/// interval is bounded below by <see cref="MinSampleInterval"/> (1 s), so the worst case is 86 401 samples ≈ 4.0 MiB per
/// printer for one day; the array grows by doubling, so a printer monitored briefly holds much less.
/// </para>
/// <para>
/// Threading: <see cref="Record"/> is called by the monitor (one thread at a time per printer); the queries may be
/// called concurrently from any thread (e.g. the UI). Each printer has its own short lock: recording is O(1),
/// a query holds the lock only while it binary-searches and copies the requested range. Readers always see a
/// consistent, ordered copy. Use <see cref="GetVersion"/> or <see cref="Changed"/> to redraw only after new data arrived.
/// </para>
/// </remarks>
public sealed partial class TelemetryHistory
{
    /// <summary>Polling intervals below this are treated as this one when sizing the ring, which bounds the memory.</summary>
    public static readonly TimeSpan MinSampleInterval = TimeSpan.FromSeconds(1);

    /// <summary>Lower bound of <see cref="GapThreshold"/>, so that a slow poll (up to the poll timeout) does not split a line.</summary>
    public static readonly TimeSpan MinGapThreshold = TimeSpan.FromSeconds(30);

    /// <summary><see cref="GapThreshold"/> in polling intervals.</summary>
    public const int GapIntervals = 3;

    private readonly TimeSpan _retention;
    private readonly ILogger<TelemetryHistory> _logger;
    private readonly Lock _configureGate = new();
    private readonly Lock _handlersGate = new();

    // Copy-on-write: replaced as a whole by Configure, never mutated after publishing, so lookups need no lock.
    private Dictionary<string, PrinterHistory> _printers = new(StringComparer.OrdinalIgnoreCase);

    // Copy-on-write, so that raising Changed needs neither a lock nor an allocation (unlike GetInvocationList).
    private EventHandler<string>[] _changedHandlers = [];
    private long _lastVersion;

    /// <param name="options">Retention; <see langword="null"/> = <see cref="TelemetryHistoryOptions.Default"/>.</param>
    /// <param name="logger">Receives failures of <see cref="Changed"/> handlers; <see langword="null"/> = discarded.</param>
    public TelemetryHistory(TelemetryHistoryOptions? options = null, ILogger<TelemetryHistory>? logger = null)
    {
        _retention = (options ?? TelemetryHistoryOptions.Default).Retention;
        _logger = logger ?? NullLogger<TelemetryHistory>.Instance;
    }

    /// <summary>
    /// The data of a printer changed: a sample was recorded, the printer was added or removed, its history was replaced,
    /// or its capacity changed (re-check <see cref="GetCapacity"/>). <see cref="GetVersion"/> has increased before this
    /// is raised. Raised with the printer id on the thread that made the change (a monitor thread for new samples,
    /// the caller of <see cref="Configure"/>), outside of any lock. Handlers must be fast and thread-safe; marshal to the
    /// UI thread yourself.
    /// </summary>
    /// <remarks>
    /// Each handler is isolated: an exception is logged and the other handlers still run, so a faulty chart can break
    /// neither recording nor <see cref="Configure"/> (and through it the monitoring session).
    /// </remarks>
    public event EventHandler<string>? Changed
    {
        add
        {
            if (value is not null)
            {
                lock (_handlersGate)
                {
                    _changedHandlers = [.. _changedHandlers, value];
                }
            }
        }

        remove
        {
            if (value is not null)
            {
                lock (_handlersGate)
                {
                    int index = Array.LastIndexOf(_changedHandlers, value);
                    if (index >= 0)
                    {
                        _changedHandlers = [.. _changedHandlers.AsSpan(0, index), .. _changedHandlers.AsSpan(index + 1)];
                    }
                }
            }
        }
    }

    public TimeSpan Retention => _retention;

    /// <summary>
    /// The gap threshold of a polling interval: <see cref="GapIntervals"/> intervals, but at least <see cref="MinGapThreshold"/>.
    /// A sample recorded longer than this after the previous one has <see cref="TelemetrySample.StartsSegment"/>.
    /// </summary>
    public static TimeSpan GapThreshold(TimeSpan pollingInterval)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pollingInterval, TimeSpan.Zero);
        TimeSpan interval = pollingInterval < MinSampleInterval ? MinSampleInterval : pollingInterval;
        // The polling interval of the app is at most minutes; saturate instead of overflowing for absurd values.
        TimeSpan threshold = interval.Ticks <= TimeSpan.MaxValue.Ticks / GapIntervals ? interval * GapIntervals : TimeSpan.MaxValue;
        return threshold > MinGapThreshold ? threshold : MinGapThreshold;
    }

    /// <summary>Number of samples kept per printer: ⌈<paramref name="retention"/> / interval⌉ + 1, interval at least <see cref="MinSampleInterval"/>.</summary>
    public static int CapacityFor(TimeSpan retention, TimeSpan pollingInterval)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(retention, TelemetryHistoryOptions.MinRetention);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(retention, TelemetryHistoryOptions.MaxRetention);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pollingInterval, TimeSpan.Zero);
        long interval = Math.Max(pollingInterval.Ticks, MinSampleInterval.Ticks);
        // + 1: n intervals span n + 1 samples. Bounded by MaxRetention / MinSampleInterval + 1, far below int.MaxValue.
        return (int)((retention.Ticks + interval - 1) / interval) + 1;
    }

    /// <summary>
    /// Sets the printers and the polling interval of a monitoring run; call before every run. Printers that stay
    /// configured and point to the same machine (same address and connection type; a rename keeps the history) keep
    /// their samples, resized to the new capacity with the newest kept. Removed printers and printers pointing to
    /// another machine lose their history.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="printers"/> contains <see langword="null"/>.</exception>
    public void Configure(IReadOnlyList<PrinterDefinition> printers, TimeSpan pollingInterval)
    {
        ArgumentNullException.ThrowIfNull(printers);
        int capacity = CapacityFor(_retention, pollingInterval);
        long gapTicks = GapThreshold(pollingInterval).Ticks;
        foreach (PrinterDefinition? printer in printers)
        {
            if (printer is null)
            {
                throw new ArgumentException("Printer list must not contain null entries.", nameof(printers));
            }
        }

        List<string> changed = [];
        lock (_configureGate)
        {
            Dictionary<string, PrinterHistory> previous = _printers;
            Dictionary<string, PrinterHistory?> kept = new(printers.Count, StringComparer.OrdinalIgnoreCase);
            foreach (PrinterDefinition printer in printers)
            {
                if (!kept.ContainsKey(printer.Id))
                {
                    kept.Add(
                        printer.Id,
                        previous.TryGetValue(printer.Id, out PrinterHistory? history) && history.IsSameMachine(printer) ? history : null);
                }
            }

            // Retire replaced and removed histories before the new ones get their versions: a writer still holding the
            // previous dictionary can then no longer give an orphan a version newer than its replacement's.
            foreach ((string id, PrinterHistory history) in previous)
            {
                if (!kept.TryGetValue(id, out PrinterHistory? keptHistory) || keptHistory is null)
                {
                    history.Retire();
                    changed.Add(id);
                }
            }

            Dictionary<string, PrinterHistory> current = new(kept.Count, StringComparer.OrdinalIgnoreCase);
            foreach (PrinterDefinition printer in printers)
            {
                if (current.ContainsKey(printer.Id))
                {
                    continue;
                }

                PrinterHistory? history = kept[printer.Id];
                if (history is not null)
                {
                    history.Adopt(printer);
                    if (history.Reconfigure(capacity, gapTicks, NextVersion))
                    {
                        changed.Add(printer.Id);
                    }
                }
                else
                {
                    history = new PrinterHistory(printer, new TelemetryRing(capacity, _retention.Ticks, gapTicks), NextVersion());
                    if (!changed.Contains(printer.Id, StringComparer.OrdinalIgnoreCase))
                    {
                        changed.Add(printer.Id);
                    }
                }

                current.Add(printer.Id, history);
            }

            Volatile.Write(ref _printers, current);
        }

        foreach (string id in changed)
        {
            RaiseChanged(id);
        }
    }

    /// <summary>
    /// Records <paramref name="snapshot"/> as the newest sample of its printer; snapshots of printers that are not
    /// configured are ignored. Snapshots without telemetry are recorded as gap samples (all values NaN).
    /// O(1) and allocation-free once the printer's ring reached its capacity.
    /// </summary>
    public void Record(PrinterSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!Volatile.Read(ref _printers).TryGetValue(snapshot.PrinterId, out PrinterHistory? history))
        {
            return;
        }

        TelemetrySample sample = TelemetrySample.FromSnapshot(snapshot);
        lock (history.Gate)
        {
            // Configure replaced or removed the printer after the lookup above; the sample belongs to the old run.
            if (history.IsRetired)
            {
                return;
            }

            history.Ring.Add(sample);
            history.Version = NextVersion();
        }

        RaiseChanged(history.Id);
    }

    /// <summary>Maximum number of samples kept for the printer; 0 when it is not configured. Useful to size a reusable buffer.</summary>
    public int GetCapacity(string printerId)
    {
        ArgumentNullException.ThrowIfNull(printerId);
        if (!Volatile.Read(ref _printers).TryGetValue(printerId, out PrinterHistory? history))
        {
            return 0;
        }

        lock (history.Gate)
        {
            return history.Ring.Capacity;
        }
    }

    /// <summary>
    /// Increases with every change of the printer's samples (unique across printers, never reused, also after the
    /// printer was removed and added again); 0 when the printer is not configured. Compare with the version returned
    /// by <see cref="CopySamples"/> to find out whether a redraw is needed.
    /// </summary>
    public long GetVersion(string printerId)
    {
        ArgumentNullException.ThrowIfNull(printerId);
        return Volatile.Read(ref _printers).TryGetValue(printerId, out PrinterHistory? history) ? history.Version : 0;
    }

    /// <summary>Number of samples with a timestamp within <paramref name="from"/> – <paramref name="to"/> (both inclusive).</summary>
    public int CountSamples(string printerId, DateTimeOffset from, DateTimeOffset to)
    {
        ArgumentNullException.ThrowIfNull(printerId);
        if (!Volatile.Read(ref _printers).TryGetValue(printerId, out PrinterHistory? history))
        {
            return 0;
        }

        lock (history.Gate)
        {
            (_, int count) = Range(history.Ring, from, to);
            return count;
        }
    }

    /// <summary>
    /// Copies the samples with a timestamp within <paramref name="from"/> – <paramref name="to"/> (both inclusive), oldest
    /// first, into <paramref name="destination"/>. When it is too small, the newest samples of the range that fit are
    /// copied. Allocation-free; reuse a buffer of <see cref="GetCapacity"/> samples to always get the whole range.
    /// </summary>
    /// <param name="version">The <see cref="GetVersion"/> of the copied state; 0 when the printer is not configured.</param>
    /// <returns>Number of samples written to the start of <paramref name="destination"/>.</returns>
    public int CopySamples(string printerId, DateTimeOffset from, DateTimeOffset to, Span<TelemetrySample> destination, out long version)
    {
        ArgumentNullException.ThrowIfNull(printerId);
        if (!Volatile.Read(ref _printers).TryGetValue(printerId, out PrinterHistory? history))
        {
            version = 0;
            return 0;
        }

        lock (history.Gate)
        {
            version = history.Version;
            (int start, int count) = Range(history.Ring, from, to);
            if (count > destination.Length)
            {
                start += count - destination.Length;
                count = destination.Length;
            }

            history.Ring.CopyTo(start, count, destination);
            return count;
        }
    }

    private static (int Start, int Count) Range(TelemetryRing ring, DateTimeOffset from, DateTimeOffset to)
    {
        long fromTicks = from.UtcTicks;
        long toTicks = to.UtcTicks;
        if (fromTicks > toTicks)
        {
            return (0, 0);
        }

        int start = ring.LowerBound(fromTicks);
        int end = ring.UpperBound(toTicks);
        return (start, end - start);
    }

    private long NextVersion() => Interlocked.Increment(ref _lastVersion);

    private void RaiseChanged(string printerId)
    {
        foreach (EventHandler<string> handler in Volatile.Read(ref _changedHandlers))
        {
            try
            {
                handler(this, printerId);
            }
            catch (Exception ex)
            {
                LogChangedHandlerFailed(ex, printerId);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "A TelemetryHistory.Changed handler failed for printer '{PrinterId}'.")]
    private partial void LogChangedHandlerFailed(Exception exception, string printerId);

    /// <summary>History of one printer; <see cref="Ring"/> is guarded by <see cref="Gate"/>.</summary>
    private sealed class PrinterHistory(PrinterDefinition printer, TelemetryRing ring, long version)
    {
        private long _version = version;

        public Lock Gate { get; } = new();

        public TelemetryRing Ring { get; } = ring;

        /// <summary>Id as configured; snapshots may differ in case.</summary>
        public string Id { get; private set; } = printer.Id;

        // Only used by Configure (under the configure gate).
        private PrinterDefinition Printer { get; set; } = printer;

        /// <summary>Written under <see cref="Gate"/>, read without it.</summary>
        public long Version
        {
            get => Volatile.Read(ref _version);
            set => Volatile.Write(ref _version, value);
        }

        /// <summary>No longer configured: ignores new samples. Guarded by <see cref="Gate"/>.</summary>
        public bool IsRetired { get; private set; }

        public void Retire()
        {
            lock (Gate)
            {
                IsRetired = true;
            }
        }

        public bool IsSameMachine(PrinterDefinition printer) =>
            printer.ConnectionType == Printer.ConnectionType
            && string.Equals(printer.Address, Printer.Address, StringComparison.Ordinal);

        /// <summary>
        /// Applies a new capacity and gap threshold. When the capacity changed (samples may have been dropped, and a
        /// reader sizing its buffer must re-check it), bumps the version and returns <see langword="true"/>.
        /// </summary>
        public bool Reconfigure(int capacity, long gapTicks, Func<long> nextVersion)
        {
            lock (Gate)
            {
                Ring.GapTicks = gapTicks;
                if (!Ring.Resize(capacity))
                {
                    return false;
                }

                Version = nextVersion();
                return true;
            }
        }

        public void Adopt(PrinterDefinition printer)
        {
            Printer = printer;
            Id = printer.Id;
        }
    }
}
