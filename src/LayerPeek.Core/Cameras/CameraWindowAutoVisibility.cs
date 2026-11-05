using LayerPeek.Core.Monitoring;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

namespace LayerPeek.Core.Cameras;

/// <summary>
/// Closes the camera window of a switched-off printer and opens it again once the printer is back (T-150, user decisions
/// 2026-11-03). A pure state machine without I/O, timers or UI: the app feeds it the monitoring snapshots and what happens
/// to the windows, executes the returned <see cref="CameraWindowAutoCommand"/>s and calls <see cref="Evaluate"/> at
/// <see cref="NextDueAt"/>. Rules, per printer:
/// <list type="bullet">
/// <item>Only <see cref="PrinterState.Offline"/> counts as switched off; every other state, also
/// <see cref="PrinterState.Unauthorized"/> and <see cref="PrinterState.Unknown"/>, counts as online.</item>
/// <item>An open window closes (<see cref="CameraWindowAutoAction.Close"/>, exactly once) after
/// <see cref="CameraWindowAutoVisibilityOptions.CloseAfterOffline"/> of offline snapshots only, measured from the first one;
/// the printer is then flagged as "closed automatically". A shorter outage changes nothing.</item>
/// <item>A flagged printer's window opens again (<see cref="CameraWindowAutoAction.Open"/>, exactly once) after
/// <see cref="CameraWindowAutoVisibilityOptions.OpenAfterOnline"/> of online snapshots only. The camera stream is not
/// awaited: the window connects by itself.</item>
/// <item>Opening or closing the window by the user forgets the flag (<see cref="CameraWindowAutoAction.ForgetAutoClosed"/>):
/// a window the user closed never opens by itself. A window the user opened while the printer was not known to be online
/// stays open until the printer has been online again (decision 7).</item>
/// <item>While closing is deferred (<see cref="SetCloseDeferred"/>: layout mode, open context menu, T-151) the window does
/// not close; when the deferral ends it closes right away if the printer is still offline for the full delay.</item>
/// <item>The flag survives a restart (pass it to the constructor): such a window opens once the printer is online for the
/// delay. Windows open when the app ended are not reopened by this class.</item>
/// <item>Nothing happens before the first <see cref="ApplySettings"/>, nor for printers whose option
/// (<see cref="AppSettings.CameraWindowAutoClose"/>, global) is off or whose camera is disabled. Turning the option off,
/// disabling the camera or removing the printer forgets its flag. Turning the option on (or enabling the camera) during an
/// outage counts the offline delay from then (decision 8).</item>
/// <item>A new monitoring run (<see cref="ApplySettings"/>) and <see cref="MonitoringStopped"/> forget what was observed
/// about the printers (the delays start again with the next snapshot); flags, open windows and deferrals stay.</item>
/// <item>Sleep, resume and clock jumps (decision 9): nothing is done on the state from before. A pending close or open is
/// dropped when the latest snapshot of the printer is older than <see cref="StaleAfter"/> (more than the longest regular
/// gap between snapshots, including the offline back-off) or lies in the future (the clock went back); a snapshot after
/// such a gap starts the delays again. <see cref="SystemResumed"/> does the same right away when the app learns about a
/// resume from the operating system.</item>
/// </list>
/// Time comes from the <see cref="TimeProvider"/> when a method is called, not from the snapshot timestamps. Printer ids are
/// case-insensitive; commands carry the id as configured. Not thread-safe: callers serialize all calls (e.g. on the UI
/// thread).
/// </summary>
public sealed class CameraWindowAutoVisibility
{
    private static readonly CameraWindowAutoCommand[] NoCommands = [];

    private readonly TimeProvider _timeProvider;

    // In configuration order (then the unconfigured ones), so that commands come in a stable order.
    private readonly List<Entry> _entries = [];
    private bool _settingsApplied;
    private bool _option;
    private TimeSpan _pollingInterval = AppSettings.DefaultPollingInterval;

    /// <param name="timeProvider">The clock of the delays.</param>
    /// <param name="options">Delays; <see cref="CameraWindowAutoVisibilityOptions.Default"/> when <see langword="null"/>.</param>
    /// <param name="autoClosedPrinterIds">
    /// Printers whose window was closed automatically in an earlier run (<c>camera-windows.json</c>). Blank ids are skipped;
    /// ids that are not configured at the first <see cref="ApplySettings"/> are forgotten then.
    /// </param>
    public CameraWindowAutoVisibility(TimeProvider timeProvider, CameraWindowAutoVisibilityOptions? options = null, IEnumerable<string>? autoClosedPrinterIds = null)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
        Options = options ?? CameraWindowAutoVisibilityOptions.Default;
        if (autoClosedPrinterIds is not null)
        {
            foreach (string? id in autoClosedPrinterIds)
            {
                if (!string.IsNullOrWhiteSpace(id))
                {
                    GetOrAdd(id).AutoClosed = true;
                }
            }
        }
    }

    private enum Connectivity
    {
        /// <summary>No snapshot in this monitoring run yet.</summary>
        None,

        Online,

        Offline,
    }

    public CameraWindowAutoVisibilityOptions Options { get; }

    /// <summary>
    /// How old the latest snapshot of a printer may be for a pending close or open to happen: twice the longest regular gap
    /// between two snapshots, i.e. the polling interval or the offline back-off of the monitor
    /// (<see cref="PrinterMonitorOptions.MaxBackoff"/>), whichever is longer, plus its
    /// <see cref="PrinterMonitorOptions.PollTimeout"/>. 140 s with the defaults. A longer gap means the computer slept (or
    /// monitoring stalled, or the clock jumped): what was observed before is not acted on.
    /// </summary>
    public TimeSpan StaleAfter
    {
        get
        {
            PrinterMonitorOptions monitor = Options.Monitor;
            TimeSpan gap = (_pollingInterval > monitor.MaxBackoff ? _pollingInterval : monitor.MaxBackoff) + monitor.PollTimeout;
            return gap * 2;
        }
    }

    /// <summary>Settings were applied and their option <see cref="AppSettings.CameraWindowAutoClose"/> is on.</summary>
    public bool Enabled => _settingsApplied && _option;

    /// <summary>
    /// The earliest time a command may become due without any further input, or <see langword="null"/> when nothing is
    /// pending; call <see cref="Evaluate"/> then. Changes after every call. Reading it drops state that is stale by then
    /// (<see cref="StaleAfter"/>), like <see cref="Evaluate"/>.
    /// </summary>
    public DateTimeOffset? NextDueAt
    {
        get
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();
            DateTimeOffset? next = null;
            foreach (Entry entry in _entries)
            {
                if (IsFresh(entry, now) && DueAt(entry) is { } due && (next is null || due < next))
                {
                    next = due;
                }
            }

            return next;
        }
    }

    /// <summary>The printers flagged as "closed automatically", in configuration order.</summary>
    public IReadOnlyList<string> AutoClosedPrinterIds => [.. _entries.Where(static entry => entry.AutoClosed).Select(static entry => entry.PrinterId)];

    /// <summary>Whether the window of <paramref name="printerId"/> (case-insensitive) is flagged as "closed automatically".</summary>
    public bool IsAutoClosed(string printerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerId);
        return Find(printerId)?.AutoClosed == true;
    }

    /// <summary>
    /// A monitoring run with <paramref name="settings"/> starts (<c>IMonitoringObserver.ApplySettings</c>): what was observed
    /// about the printers is forgotten, the option and the printers (with their cameras) apply.
    /// </summary>
    public IReadOnlyList<CameraWindowAutoCommand> ApplySettings(AppSettings settings) => Configure(settings, newRun: true);

    /// <summary>
    /// Saved settings while the monitoring run continues (<c>IMonitoringObserver.UpdateSettings</c>): like
    /// <see cref="ApplySettings"/>, but running delays continue.
    /// </summary>
    public IReadOnlyList<CameraWindowAutoCommand> UpdateSettings(AppSettings settings) => Configure(settings, newRun: false);

    /// <summary>Monitoring stopped (failed): no more snapshots, so pending delays are dropped until the next snapshot.</summary>
    public IReadOnlyList<CameraWindowAutoCommand> MonitoringStopped() => ForgetObservations();

    /// <summary>
    /// The computer woke up from sleep (power event of the operating system): pending delays are dropped and start again
    /// with the first snapshot after it (decision 9). Flags, open windows and deferrals stay. Optional:
    /// <see cref="StaleAfter"/> catches a resume without this call too, but only once the gap is long enough.
    /// </summary>
    public IReadOnlyList<CameraWindowAutoCommand> SystemResumed() => ForgetObservations();

    /// <summary>A snapshot of the monitor; snapshots of printers that are not configured are ignored.</summary>
    public IReadOnlyList<CameraWindowAutoCommand> Process(PrinterSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!_settingsApplied || Find(snapshot.PrinterId) is not { Configured: true } entry)
        {
            return NoCommands;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (!IsFresh(entry, now))
        {
            // After a sleep or a clock jump the state from before is not continued.
            entry.Connectivity = Connectivity.None;
        }

        entry.LastSnapshotAt = now;
        if (snapshot.State == PrinterState.Offline)
        {
            if (entry.Connectivity != Connectivity.Offline)
            {
                entry.Connectivity = Connectivity.Offline;
                entry.Since = now;
            }
        }
        else
        {
            if (entry.Connectivity != Connectivity.Online)
            {
                entry.Connectivity = Connectivity.Online;
                entry.Since = now;
            }

            entry.HoldUntilOnline = false;
        }

        return EvaluateAt(now);
    }

    /// <summary>
    /// The window of <paramref name="printerId"/> is open (opened by the user; reporting an automatic open too is harmless).
    /// Forgets the flag. Opened while the printer is not known to be online, it is not closed before the printer has been
    /// online again.
    /// </summary>
    public IReadOnlyList<CameraWindowAutoCommand> WindowOpened(string printerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerId);
        Entry entry = GetOrAdd(printerId);
        if (entry.WindowOpen)
        {
            return NoCommands;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        entry.WindowOpen = true;
        // A stale "online" (before a sleep) is not known to be online any more.
        entry.HoldUntilOnline = !IsFresh(entry, now) || entry.Connectivity != Connectivity.Online;
        List<CameraWindowAutoCommand>? commands = null;
        if (entry.AutoClosed)
        {
            entry.AutoClosed = false;
            (commands ??= []).Add(new CameraWindowAutoCommand(entry.PrinterId, CameraWindowAutoAction.ForgetAutoClosed));
        }

        return Append(commands, EvaluateAt(now));
    }

    /// <summary>
    /// The window of <paramref name="printerId"/> closed for any reason other than executing a
    /// <see cref="CameraWindowAutoAction.Close"/> command: call it for EVERY such close (closed or hidden by the user, the
    /// printer removed, its camera disabled, the app closing windows for any other reason), also when the camera is hidden
    /// while the window was closed automatically. The window then never opens by itself; forgets the flag and the deferral.
    /// Not for closes that execute <see cref="CameraWindowAutoAction.Close"/>.
    /// </summary>
    public IReadOnlyList<CameraWindowAutoCommand> WindowClosedByUser(string printerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerId);
        if (Find(printerId) is not { } entry)
        {
            return NoCommands;
        }

        entry.WindowOpen = false;
        entry.CloseDeferred = false;
        entry.HoldUntilOnline = false;
        CameraWindowAutoCommand[] commands = NoCommands;
        if (entry.AutoClosed)
        {
            entry.AutoClosed = false;
            commands = [new CameraWindowAutoCommand(entry.PrinterId, CameraWindowAutoAction.ForgetAutoClosed)];
        }

        DropIfUnused(entry);
        return commands;
    }

    /// <summary>
    /// Defers closing the window of <paramref name="printerId"/> (<see langword="true"/>: layout mode or context menu open,
    /// T-151) or ends the deferral; the window then closes right away when the printer is still offline for the full delay.
    /// </summary>
    public IReadOnlyList<CameraWindowAutoCommand> SetCloseDeferred(string printerId, bool deferred)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerId);
        Entry entry = GetOrAdd(printerId);
        if (entry.CloseDeferred == deferred)
        {
            return NoCommands;
        }

        entry.CloseDeferred = deferred;
        DropIfUnused(entry);
        return EvaluateAt(_timeProvider.GetUtcNow());
    }

    /// <summary>The commands due now; call at <see cref="NextDueAt"/> (calling earlier or more often is harmless).</summary>
    public IReadOnlyList<CameraWindowAutoCommand> Evaluate() => EvaluateAt(_timeProvider.GetUtcNow());

    private IReadOnlyList<CameraWindowAutoCommand> Configure(AppSettings settings, bool newRun)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settingsApplied = true;
        _option = settings.CameraWindowAutoClose;
        _pollingInterval = settings.PollingInterval;
        DateTimeOffset now = _timeProvider.GetUtcNow();
        List<CameraWindowAutoCommand>? commands = null;
        List<Entry> configured = new(settings.Printers.Count);
        foreach (PrinterDefinition printer in settings.Printers)
        {
            Entry entry = GetOrAdd(printer.Id);
            entry.PrinterId = printer.Id;
            entry.Configured = true;
            bool wasEligible = entry.Eligible;
            entry.Eligible = _option && printer.Camera.Enabled;
            if (newRun)
            {
                entry.Connectivity = Connectivity.None;
            }
            else if (entry.Eligible && !wasEligible && entry.Connectivity == Connectivity.Offline)
            {
                // Decision 8: an outage that began while the option (or the camera) was off counts from now.
                entry.Since = now;
            }

            configured.Add(entry);
        }

        List<Entry> others = [];
        foreach (Entry entry in _entries)
        {
            if (!configured.Contains(entry))
            {
                // Removed (or never configured): only a still open window is remembered, so that its close is known.
                entry.Configured = false;
                entry.Eligible = false;
                entry.Connectivity = Connectivity.None;
                entry.HoldUntilOnline = false;
                others.Add(entry);
            }
        }

        foreach (Entry entry in configured.Concat(others))
        {
            if (entry.AutoClosed && !entry.Eligible)
            {
                entry.AutoClosed = false;
                (commands ??= []).Add(new CameraWindowAutoCommand(entry.PrinterId, CameraWindowAutoAction.ForgetAutoClosed));
            }
        }

        _entries.Clear();
        _entries.AddRange(configured);
        _entries.AddRange(others.Where(static entry => entry.WindowOpen || entry.CloseDeferred));
        return Append(commands, EvaluateAt(now));
    }

    private CameraWindowAutoCommand[] ForgetObservations()
    {
        foreach (Entry entry in _entries)
        {
            entry.Connectivity = Connectivity.None;
        }

        return NoCommands;
    }

    /// <summary>
    /// Whether what was observed about <paramref name="entry"/> may still be acted on at <paramref name="now"/>; forgets it
    /// otherwise. Nothing observed counts as fresh (there is nothing to act on).
    /// </summary>
    private bool IsFresh(Entry entry, DateTimeOffset now)
    {
        if (entry.Connectivity == Connectivity.None)
        {
            return true;
        }

        TimeSpan age = now - entry.LastSnapshotAt;
        if (age >= TimeSpan.Zero && age <= StaleAfter)
        {
            return true;
        }

        entry.Connectivity = Connectivity.None;
        return false;
    }

    private IReadOnlyList<CameraWindowAutoCommand> EvaluateAt(DateTimeOffset now)
    {
        List<CameraWindowAutoCommand>? commands = null;
        foreach (Entry entry in _entries)
        {
            if (!IsFresh(entry, now) || DueAt(entry) is not { } due || due > now)
            {
                continue;
            }

            if (entry.WindowOpen)
            {
                entry.WindowOpen = false;
                entry.AutoClosed = true;
                (commands ??= []).Add(new CameraWindowAutoCommand(entry.PrinterId, CameraWindowAutoAction.Close));
            }
            else
            {
                entry.WindowOpen = true;
                entry.AutoClosed = false;
                (commands ??= []).Add(new CameraWindowAutoCommand(entry.PrinterId, CameraWindowAutoAction.Open));
            }
        }

        return commands ?? (IReadOnlyList<CameraWindowAutoCommand>)NoCommands;
    }

    /// <summary>When the pending close or open of <paramref name="entry"/> is due; <see langword="null"/> when none is pending.</summary>
    private DateTimeOffset? DueAt(Entry entry)
    {
        if (!Enabled || !entry.Eligible)
        {
            return null;
        }

        if (entry.WindowOpen)
        {
            return entry.Connectivity == Connectivity.Offline && !entry.CloseDeferred && !entry.HoldUntilOnline
                ? entry.Since + Options.CloseAfterOffline
                : null;
        }

        return entry.AutoClosed && entry.Connectivity == Connectivity.Online ? entry.Since + Options.OpenAfterOnline : null;
    }

    private static IReadOnlyList<CameraWindowAutoCommand> Append(List<CameraWindowAutoCommand>? first, IReadOnlyList<CameraWindowAutoCommand> second)
    {
        if (first is null)
        {
            return second;
        }

        first.AddRange(second);
        return first;
    }

    /// <summary>A printer that is not configured is remembered only while there is something to remember.</summary>
    private void DropIfUnused(Entry entry)
    {
        if (!entry.Configured && !entry.WindowOpen && !entry.CloseDeferred && !entry.AutoClosed)
        {
            _entries.Remove(entry);
        }
    }

    private Entry? Find(string printerId)
    {
        foreach (Entry entry in _entries)
        {
            if (string.Equals(entry.PrinterId, printerId, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        return null;
    }

    private Entry GetOrAdd(string printerId)
    {
        if (Find(printerId) is { } entry)
        {
            return entry;
        }

        entry = new Entry(printerId);
        _entries.Add(entry);
        return entry;
    }

    private sealed class Entry(string printerId)
    {
        public string PrinterId { get; set; } = printerId;

        /// <summary>Part of the applied settings.</summary>
        public bool Configured { get; set; }

        /// <summary>Configured with an enabled camera while the option is on.</summary>
        public bool Eligible { get; set; }

        public bool WindowOpen { get; set; }

        public bool AutoClosed { get; set; }

        public bool CloseDeferred { get; set; }

        /// <summary>Opened by the user while the printer was not known to be online: not closed before it is online again.</summary>
        public bool HoldUntilOnline { get; set; }

        public Connectivity Connectivity { get; set; }

        /// <summary>Start of the current <see cref="Connectivity"/> (online or offline).</summary>
        public DateTimeOffset Since { get; set; }

        /// <summary>When the latest snapshot arrived (clock of the <see cref="TimeProvider"/>).</summary>
        public DateTimeOffset LastSnapshotAt { get; set; }
    }
}
