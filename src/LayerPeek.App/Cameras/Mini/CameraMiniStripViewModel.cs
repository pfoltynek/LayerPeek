using System.Globalization;

using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;

using LayerPeek.App.Cameras.Overlay;
using LayerPeek.App.Cameras.Overlay.Widgets;
using LayerPeek.App.Diagnostics.Chart;
using LayerPeek.App.Resources;
using LayerPeek.App.Tray;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cooldown;
using LayerPeek.Core.History;
using LayerPeek.Core.Printers;

namespace LayerPeek.App.Cameras.Mini;

/// <summary>What the summary row of the mini preview strip shows (T-089, design T-076 4).</summary>
internal enum MiniStripKind
{
    /// <summary>The state name only: no job (ready, finished, stopped…), offline, an error, before the first poll.</summary>
    State,

    /// <summary>An active job: the percent first, then the remaining time (printing) or the state (paused, attention…).</summary>
    Job,

    /// <summary>The print ended and the nozzle cools down to the threshold (T-079).</summary>
    Cooling,

    /// <summary>The nozzle is at or below the threshold after the print.</summary>
    Cooled,
}

/// <summary>
/// The content of the mini preview strip, rounded like the display (shared rules of the Status and Times widgets,
/// <see cref="OverlayValues"/>): two polls that look the same are equal, so nothing is posted for them.
/// </summary>
/// <param name="Kind">Which text the summary row shows.</param>
/// <param name="State">The printer state; <see langword="null"/> before the first poll.</param>
/// <param name="Percent">Whole percent of the active job.</param>
/// <param name="Minutes">Remaining minutes of the printing job, or of the cool-down estimate; <see langword="null"/> when unknown.</param>
/// <param name="NozzleCelsius">Whole degrees of the nozzle while cooling or cooled.</param>
/// <param name="Finish">
/// When the printing job finishes, as the Times widget shows it (T-103, <see cref="OverlayValues.Finish"/>); only while
/// printing (T-103 Q5: not while paused or waiting), <see langword="null"/> when unknown.
/// </param>
/// <param name="FinishDays">Local days from the snapshot to <paramref name="Finish"/> (0 = the same day).</param>
/// <param name="CoolingPercent">0–100 towards the cool-down threshold, rounded down like the Cool-down widget (100 once cooled down).</param>
internal readonly record struct MiniStripContent(
    MiniStripKind Kind,
    PrinterState? State,
    int Percent,
    long? Minutes,
    long NozzleCelsius,
    OverlayFinishTime? Finish = null,
    int FinishDays = 0,
    int CoolingPercent = 0)
{
    /// <summary>
    /// The content for <paramref name="snapshot"/> and the cool-down state of the printer: the active job first (the next
    /// print ends any cool-down), then the cool-down of a reachable printer, else the state name. The cool-down estimate
    /// follows the rules of the Cool-down widget (<see cref="CooldownEstimate.Minutes"/> at <paramref name="now"/>: never
    /// "0 min", none for a stale state), the finish time those of the Times widget in <paramref name="zone"/> (the local
    /// time zone of the window's clock). Allocates nothing.
    /// </summary>
    public static MiniStripContent Of(PrinterSnapshot? snapshot, in CooldownState cooldown, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        PrinterState? state = snapshot?.State;
        if (OverlayValues.ActiveJob(snapshot) is { } job)
        {
            // The remaining time and the finish time only while printing; otherwise the state stands next to the percent
            // (T-076 4, T-103 Q5).
            bool printing = state == PrinterState.Printing;
            long? remaining = printing ? OverlayValues.RemainingMinutes(job) : null;
            OverlayFinishTime? finish = printing ? OverlayValues.Finish(snapshot!, zone) : null;
            int days = finish is { IsOnAnotherDay: true } at
                ? (int)Math.Clamp((at.Local.Date - TimeZoneInfo.ConvertTime(snapshot!.Timestamp, zone).Date).TotalDays, int.MinValue, int.MaxValue)
                : 0;
            return new MiniStripContent(MiniStripKind.Job, state, OverlayValues.ProgressPercent(job), remaining, 0, finish, days);
        }

        if (snapshot is not null
            && snapshot.State.IsReachable()
            && cooldown.Phase != CooldownPhase.None
            && OverlayValues.Temperature(new Temperature(cooldown.NozzleCelsius ?? double.NaN, null)) is { } nozzle)
        {
            if (cooldown.Phase != CooldownPhase.Cooling)
            {
                return new MiniStripContent(MiniStripKind.Cooled, state, 0, null, nozzle.Actual, CoolingPercent: 100);
            }

            // T-103 Q3: the bar on the top edge shows the progress towards the threshold, as the Cool-down widget's bar.
            double progress = double.IsFinite(cooldown.Progress) ? Math.Clamp(cooldown.Progress, 0, 1) : 0;
            return new MiniStripContent(
                MiniStripKind.Cooling,
                state,
                0,
                CooldownEstimate.Minutes(cooldown, now),
                nozzle.Actual,
                CoolingPercent: (int)Math.Floor(progress * 100));
        }

        return new MiniStripContent(MiniStripKind.State, state, 0, null, 0);
    }
}

/// <summary>
/// One way to show the summary row (T-103 3): the summary text and the finish time after the flag; the strip shows the first
/// variant that fits whole (<see cref="CameraMiniSummaryPanel"/>).
/// </summary>
/// <param name="Summary">"19 % · 2 h 35 min", "Cooled down – safe to turn off", "87 °C · about 6 min"…</param>
/// <param name="Finish">"Done at 7:50 PM", "7:50 PM", "Fri 7:50 PM"; <see langword="null"/> = no flag group.</param>
public sealed record MiniStripVariant(string Summary, string? Finish = null)
{
    /// <summary>The flag group with the finish time is drawn.</summary>
    public bool HasFinish => Finish is not null;
}

/// <summary>
/// The texts of a <see cref="MiniStripContent"/> (T-103): up to three variants of the summary row, longest first (the last
/// one has no finish time and may be trimmed), and the whole content in words for the tooltip and screen readers.
/// </summary>
internal readonly record struct MiniStripTexts(MiniStripVariant First, MiniStripVariant? Second, MiniStripVariant? Third, string Accessible);

/// <summary>
/// The left column of the chart row of the mini preview strip (T-090, design T-076 4): the actual nozzle and bed
/// temperatures in whole degrees, rounded like the display; <see langword="null"/> = not reported.
/// </summary>
internal readonly record struct MiniChartValues(long? Nozzle, long? Bed)
{
    /// <summary>The values of <paramref name="snapshot"/> (none while it reports no temperatures). Allocates nothing.</summary>
    public static MiniChartValues Of(PrinterSnapshot? snapshot)
    {
        Telemetry? telemetry = snapshot?.Telemetry;
        return telemetry is null
            ? default
            : new MiniChartValues(OverlayValues.Temperature(telemetry.Nozzle)?.Actual, OverlayValues.Temperature(telemetry.Bed)?.Actual);
    }
}

/// <summary>
/// The info strip below the image of the mini preview (T-089, T-090, design T-076 4): a 3 DIP progress bar on its top
/// edge, the summary row "● 19 % · 2 h 35 min" with two toggle buttons on the right, and below it the collapsible print
/// name row (34) and temperature chart row (64). The summary row shows the number first; without a job the state, while
/// cooling down the nozzle temperature (T-079). While printing the finish time follows after a flag (T-103); the row
/// shows the first of up to three variants that fits whole (<see cref="CameraMiniSummaryPanel"/>). The full text is the
/// row's tooltip and accessible name.
/// </summary>
/// <remarks>
/// <para>
/// Snapshots and cool-down changes arrive on the monitor thread: the content is computed there under a lock with the shared
/// rules of the Status and Times widgets (<see cref="OverlayValues"/>, <see cref="OverlayFormat"/>), and one update is
/// posted to the UI thread only when it changed (a poll that changes nothing visible allocates nothing). Texts are
/// formatted on the UI thread, and again after a language switch. The cool-down estimate follows the rules of the
/// Cool-down widget (T-088, <see cref="CooldownEstimate"/>) and, while it is shown, counts down with the window clock
/// (<see cref="CooldownCountdown"/>) also without snapshots.
/// </para>
/// <para>
/// The name row is the compact template of the Print name widget over its own <see cref="PrintNameWidgetViewModel"/>
/// (<see cref="NameRow"/>, T-085); it disappears without a job (the toggle stays on). The chart row shows the current
/// temperatures and the compact <c>TemperatureChart</c> (T-084), which listens to the history only while it is shown; the
/// temperatures are computed only while the row is expanded, so a collapsed chart costs nothing per poll. Both rows add to
/// <see cref="StripHeight"/>, which the window follows (the image keeps the aspect ratio of the stream). Toggling a row
/// raises <see cref="RowsChanged"/> (the camera window view model remembers it per printer).
/// </para>
/// <para>Public members are for the UI thread unless stated otherwise.</para>
/// </remarks>
public sealed class CameraMiniStripViewModel : ObservableObject, IDisposable
{
    /// <summary>The progress bar on the top edge of the strip (DIP).</summary>
    public const double ProgressHeight = 3;

    /// <summary>The summary row (DIP).</summary>
    public const double SummaryHeight = 30;

    /// <summary>The collapsed strip: the progress bar and the summary row (DIP).</summary>
    public const double CollapsedHeight = ProgressHeight + SummaryHeight;

    /// <summary>The print name row (DIP).</summary>
    public const double NameRowHeight = 34;

    /// <summary>The temperature chart row (DIP).</summary>
    public const double ChartRowHeight = 64;

    private const string Separator = " · ";

    private readonly string _printerId;
    private readonly IUiDispatcher _dispatcher;
    private readonly CooldownMonitor? _cooldown;
    private readonly CooldownCountdown? _countdown;
    private readonly PrintNameWidgetViewModel _name = new();
    private readonly Action _apply;
    private readonly Lock _gate = new();

    // Guarded by _gate.
    private PrinterSnapshot? _snapshot;
    private MiniStripContent _computed;
    private bool _hasComputed;
    private bool _chartComputing;
    private MiniChartValues _chartComputed;
    private bool _applyPending;
    private bool _disposed;

    // UI thread.
    private MiniStripContent _shown;
    private bool _hasShown;
    private string _summary = string.Empty;
    private MiniStripVariant _firstVariant = new(string.Empty);
    private MiniStripVariant? _secondVariant;
    private MiniStripVariant? _thirdVariant;
    private string _accessibleText = string.Empty;
    private IBrush _dotBrush = OverlayStateBrushes.NoData;
    private double _progress;
    private bool _hasProgress;
    private MiniStripKind _kind;
    private bool _nameExpanded;
    private bool _chartExpanded;
    private bool _showsNameRow;
    private double _stripHeight = CollapsedHeight;
    private MiniChartValues _chartShown;
    private bool _hasChartShown;
    private string _nozzleText = string.Empty;
    private string _bedText = string.Empty;
    private bool _hasNozzle;
    private bool _hasBed;
    private string _chartAccessibleText = string.Empty;
    private string _nameToggleTip = string.Empty;
    private string _chartToggleTip = string.Empty;
    private string _nameToggleName = string.Empty;
    private string _chartToggleName = string.Empty;
    private string _chartRange = string.Empty;
    private string _chartNoData = string.Empty;
    private string _chartName = string.Empty;

    /// <param name="printerId">The printer of the window.</param>
    /// <param name="cooldown">The cool-down states (T-079); <see langword="null"/>: the strip never shows a cool-down.</param>
    /// <param name="history">The telemetry history of the chart row (T-090); <see langword="null"/>: the chart says "No data".</param>
    /// <param name="clock">The clock of the chart row; <see langword="null"/>: the system clock.</param>
    /// <param name="rows">The rows expanded when the window opens (remembered per printer; default both collapsed).</param>
    /// <remarks>Shows "Connecting…" until <see cref="ShowCurrent"/> or the first <see cref="OnSnapshot"/>.</remarks>
    internal CameraMiniStripViewModel(
        string printerId,
        IUiDispatcher dispatcher,
        CooldownMonitor? cooldown,
        TelemetryHistory? history = null,
        TimeProvider? clock = null,
        CameraMiniRows rows = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerId);
        ArgumentNullException.ThrowIfNull(dispatcher);
        _printerId = printerId;
        _dispatcher = dispatcher;
        _cooldown = cooldown;
        History = history;
        Clock = clock ?? TimeProvider.System;
        _apply = ApplyPosted;
        _nameExpanded = rows.Name;
        _chartExpanded = rows.Chart;
        _chartComputing = rows.Chart;
        if (_cooldown is not null)
        {
            // T-088: a shown estimate counts down with the window clock also while no snapshots arrive.
            _countdown = new CooldownCountdown(Clock, Recompute);
            _cooldown.Changed += OnCooldownChanged;
        }

        ShowCurrent(null);
        FormatRowTexts();
    }

    /// <summary>A row was expanded or collapsed by the user (not when the strip opens with remembered rows).</summary>
    public event EventHandler? RowsChanged;

    /// <summary>
    /// Height of the whole strip (DIP): <see cref="CollapsedHeight"/> plus the shown name row and the expanded chart row;
    /// the window follows it with its height.
    /// </summary>
    public double StripHeight
    {
        get => _stripHeight;
        private set => SetProperty(ref _stripHeight, value);
    }

    /// <summary>
    /// The summary of the longest variant (<see cref="FirstVariant"/>): "19 % · 2 h 35 min", "19 % · Paused", "Ready",
    /// "Cooling down… 87 °C · about 6 min", "Cooled down – safe to turn off" (number first).
    /// </summary>
    public string Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    /// <summary>
    /// The longest way to show the summary row (T-103 3: L1, C1, K1, or the only one); the row shows the first of
    /// <see cref="FirstVariant"/>, <see cref="SecondVariant"/>, <see cref="ThirdVariant"/> that fits whole.
    /// </summary>
    public MiniStripVariant FirstVariant
    {
        get => _firstVariant;
        private set => SetProperty(ref _firstVariant, value);
    }

    /// <summary>The shorter variant (L2 / L3, C2 / C3, K2); <see langword="null"/> = none.</summary>
    public MiniStripVariant? SecondVariant
    {
        get => _secondVariant;
        private set => SetProperty(ref _secondVariant, value);
    }

    /// <summary>The shortest variant (L3, C3); <see langword="null"/> = none.</summary>
    public MiniStripVariant? ThirdVariant
    {
        get => _thirdVariant;
        private set => SetProperty(ref _thirdVariant, value);
    }

    /// <summary>
    /// The whole content in words: tooltip and accessible name of the summary row, always the full wording whichever
    /// variant is drawn (T-103 6: with the finish time, "safe to turn off").
    /// </summary>
    public string AccessibleText
    {
        get => _accessibleText;
        private set => SetProperty(ref _accessibleText, value);
    }

    /// <summary>The state dot in the tray colour (<see cref="OverlayStateBrushes"/>); not drawn while cooling or cooled.</summary>
    public IBrush DotBrush
    {
        get => _dotBrush;
        private set => SetProperty(ref _dotBrush, value);
    }

    /// <summary>
    /// 0–100: the shown whole percent of the active job (the bar agrees with the number); while cooling the progress
    /// towards the threshold, once cooled down 100 (T-103 Q3).
    /// </summary>
    public double Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

    /// <summary>
    /// An active job, a cool-down or a cooled printer: the bar has its fill and its normal track; otherwise only a faint
    /// track (same height).
    /// </summary>
    public bool HasProgress
    {
        get => _hasProgress;
        private set => SetProperty(ref _hasProgress, value);
    }

    /// <summary>The state dot is drawn (not while cooling or cooled, which have their icon).</summary>
    public bool ShowsDot => _kind is MiniStripKind.State or MiniStripKind.Job;

    /// <summary>The snowflake icon of a cooling printer.</summary>
    public bool ShowsCoolingIcon => _kind == MiniStripKind.Cooling;

    /// <summary>The power icon of a printer that cooled down.</summary>
    public bool ShowsCooledIcon => _kind == MiniStripKind.Cooled;

    /// <summary>The print name row is on (its toggle button is checked); the row is shown only while there is a job.</summary>
    public bool IsNameRowExpanded
    {
        get => _nameExpanded;
        set
        {
            if (_disposed || _nameExpanded == value)
            {
                return;
            }

            _nameExpanded = value;
            OnPropertyChanged();
            UpdateRows();
            RowsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The temperature chart row is on (its toggle button is checked) and shown.</summary>
    public bool IsChartRowExpanded
    {
        get => _chartExpanded;
        set
        {
            if (_disposed || _chartExpanded == value)
            {
                return;
            }

            MiniChartValues values;
            lock (_gate)
            {
                // From now on (or no longer) the polls compute the temperatures; the current ones are shown right away.
                _chartComputing = value;
                _chartComputed = values = MiniChartValues.Of(_snapshot);
            }

            _chartExpanded = value;
            if (value)
            {
                FormatChart(values);
            }
            else
            {
                _hasChartShown = false;
            }

            OnPropertyChanged();
            UpdateRows();
            RowsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The name row is drawn: expanded and a job with a file name (T-090 Q4: it disappears without a job).</summary>
    public bool ShowsNameRow
    {
        get => _showsNameRow;
        private set => SetProperty(ref _showsNameRow, value);
    }

    /// <summary>The Print name widget whose compact template is the name row (T-085 <see cref="PrintNameCompactView"/>).</summary>
    public PrintNameWidgetViewModel NameRow => _name;

    /// <summary>Tooltip of the name toggle: "Show print name", "Hide print name", or without a job "The print name appears during a print".</summary>
    public string NameToggleTip
    {
        get => _nameToggleTip;
        private set => SetProperty(ref _nameToggleTip, value);
    }

    /// <summary>Tooltip of the chart toggle: "Show temperature chart" / "Hide temperature chart".</summary>
    public string ChartToggleTip
    {
        get => _chartToggleTip;
        private set => SetProperty(ref _chartToggleTip, value);
    }

    /// <summary>The steady accessible name of the name toggle ("Print name in the strip"); its state comes from the Toggle pattern.</summary>
    public string NameToggleName
    {
        get => _nameToggleName;
        private set => SetProperty(ref _nameToggleName, value);
    }

    /// <summary>The steady accessible name of the chart toggle ("Temperature chart in the strip").</summary>
    public string ChartToggleName
    {
        get => _chartToggleName;
        private set => SetProperty(ref _chartToggleName, value);
    }

    /// <summary>"60 min": the range of the compact chart.</summary>
    public string ChartRange
    {
        get => _chartRange;
        private set => SetProperty(ref _chartRange, value);
    }

    /// <summary>"No data" of the chart without history.</summary>
    public string ChartNoData
    {
        get => _chartNoData;
        private set => SetProperty(ref _chartNoData, value);
    }

    /// <summary>Accessible name of the chart ("chart of the last 60 minutes").</summary>
    public string ChartName
    {
        get => _chartName;
        private set => SetProperty(ref _chartName, value);
    }

    /// <summary>"215°": the actual nozzle temperature.</summary>
    public string NozzleText
    {
        get => _nozzleText;
        private set => SetProperty(ref _nozzleText, value);
    }

    /// <summary>"60°": the actual bed temperature.</summary>
    public string BedText
    {
        get => _bedText;
        private set => SetProperty(ref _bedText, value);
    }

    public bool HasNozzle
    {
        get => _hasNozzle;
        private set => SetProperty(ref _hasNozzle, value);
    }

    public bool HasBed
    {
        get => _hasBed;
        private set => SetProperty(ref _hasBed, value);
    }

    /// <summary>The chart row in words: "Nozzle 215 °C, Bed 60 °C" (without temperatures only the chart's name).</summary>
    public string ChartAccessibleText
    {
        get => _chartAccessibleText;
        private set => SetProperty(ref _chartAccessibleText, value);
    }

    /// <summary>The history the chart row draws.</summary>
    public TelemetryHistory? History { get; }

    /// <summary>The printer whose history the chart row draws.</summary>
    public string PrinterId => _printerId;

    /// <summary>The clock of the chart row.</summary>
    public TimeProvider Clock { get; }

    /// <summary>The expanded rows (remembered per printer).</summary>
    internal CameraMiniRows Rows => new(_nameExpanded, _chartExpanded);

    /// <summary>The content shown now (tests).</summary>
    internal MiniStripContent ShownContent => _shown;

    /// <summary>The clock that counts a shown cool-down estimate down runs (tests).</summary>
    internal bool IsTicking => _countdown?.IsTicking ?? false;

    /// <summary>
    /// UI thread, the window opens: the latest snapshot the monitor had (read after subscribing) is shown right away,
    /// unless a newer one arrived meanwhile through <see cref="OnSnapshot"/>.
    /// </summary>
    internal void ShowCurrent(PrinterSnapshot? current)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _snapshot ??= current;
            ComputeLocked();
        }

        ApplyPosted();
    }

    /// <summary>Any thread: a new snapshot of the printer; one update is posted only when the shown content changes.</summary>
    internal void OnSnapshot(PrinterSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        bool post;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _snapshot = snapshot;
            post = ComputeLocked() && !_applyPending;
            _applyPending |= post;
        }

        if (post)
        {
            _dispatcher.Post(_apply);
        }
    }

    /// <summary>
    /// "Apply" in the settings (T-040) forgot the remembered state: both rows collapse again (the default, T-090 Q1/Q3)
    /// without raising <see cref="RowsChanged"/>.
    /// </summary>
    internal void ResetRows()
    {
        EventHandler? rowsChanged = RowsChanged;
        RowsChanged = null;
        try
        {
            IsNameRowExpanded = false;
            IsChartRowExpanded = false;
        }
        finally
        {
            RowsChanged = rowsChanged;
        }
    }

    /// <summary>The language changed: the shown content is formatted again.</summary>
    public void RefreshTexts()
    {
        if (_disposed)
        {
            return;
        }

        if (_hasShown)
        {
            Format(_shown);
        }

        _name.RefreshTexts();
        if (_hasChartShown)
        {
            FormatChart(_chartShown);
        }

        FormatRowTexts();
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

        if (_cooldown is not null)
        {
            _cooldown.Changed -= OnCooldownChanged;
        }

        _countdown?.Dispose();

        _name.Dispose();
    }

    // Monitor thread (or whoever configures the cool-down monitor).
    private void OnCooldownChanged(object? sender, string printerId)
    {
        if (string.Equals(printerId, _printerId, StringComparison.OrdinalIgnoreCase))
        {
            Recompute();
        }
    }

    // Monitor thread or the countdown's timer: the content again; one update is posted only when it changed.
    private void Recompute()
    {
        bool post;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            post = ComputeLocked() && !_applyPending;
            _applyPending |= post;
        }

        if (post)
        {
            _dispatcher.Post(_apply);
        }
    }

    /// <summary>Under the lock: the content now (summary, name, and the temperatures while the chart is expanded); whether any changed.</summary>
    private bool ComputeLocked()
    {
        CooldownState cooldown = _cooldown?.GetState(_printerId) ?? CooldownState.None;
        MiniStripContent content = MiniStripContent.Of(_snapshot, cooldown, Clock.GetUtcNow(), Clock.LocalTimeZone);
        _countdown?.Set(content.Kind == MiniStripKind.Cooling && content.Minutes is not null);
        bool changed = false;
        if (!_hasComputed || content != _computed)
        {
            _computed = content;
            _hasComputed = true;
            changed = true;
        }

        // The name widget reads only the snapshot; in the mini preview it never asks for a thumbnail (T-087).
        OverlayWidgetInput input = default(OverlayWidgetInput) with { Snapshot = _snapshot, Mode = CameraWindowMode.Mini };
        changed |= _name.Compute(input);
        if (_chartComputing)
        {
            MiniChartValues values = MiniChartValues.Of(_snapshot);
            if (values != _chartComputed)
            {
                _chartComputed = values;
                changed = true;
            }
        }

        return changed;
    }

    // UI thread.
    private void ApplyPosted()
    {
        MiniStripContent content;
        MiniChartValues chart;
        bool chartComputing;
        lock (_gate)
        {
            _applyPending = false;
            if (_disposed || !_hasComputed)
            {
                return;
            }

            content = _computed;
            chart = _chartComputed;
            chartComputing = _chartComputing;
            _name.TakeComputed();
        }

        if (!_hasShown || content != _shown)
        {
            _shown = content;
            _hasShown = true;
            Format(content);
        }

        _name.ApplyTaken();
        if (chartComputing && _chartExpanded && (!_hasChartShown || chart != _chartShown))
        {
            FormatChart(chart);
        }

        UpdateRows();
    }

    /// <summary>Which rows are drawn, the strip height and the toggle tooltips that depend on them.</summary>
    private void UpdateRows()
    {
        ShowsNameRow = _nameExpanded && _name.IsShown;
        StripHeight = CollapsedHeight + (_showsNameRow ? NameRowHeight : 0) + (_chartExpanded ? ChartRowHeight : 0);
        NameToggleTip = !_nameExpanded ? Strings.CameraMiniShowName : _showsNameRow ? Strings.CameraMiniHideName : Strings.CameraMiniNameNoJob;
        ChartToggleTip = _chartExpanded ? Strings.CameraMiniHideChart : Strings.CameraMiniShowChart;
    }

    /// <summary>The texts of the rows that only follow the language.</summary>
    private void FormatRowTexts()
    {
        UpdateRows();
        NameToggleName = Strings.CameraMiniToggleName;
        ChartToggleName = Strings.CameraMiniToggleChart;
        ChartRange = Format(Strings.DurationMinutes, (long)TemperatureChart.CompactTimeWindow.TotalMinutes);
        ChartNoData = Strings.CameraOverlayChartNoData;
        ChartName = Strings.CameraOverlayChartName;
    }

    /// <summary>
    /// The texts of <paramref name="content"/> in the current language (T-076 4, T-103 3): the variants of the summary row
    /// (the number first, longest first) and the whole content in words for the tooltip and screen readers.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>Printing with a finish time: L1 "19 % · 2 h 35 min ⚑ Done at 7:50 PM" (the Times widget's wording), L2 with
    /// only the time ("7:50 PM", on another day within a week "Fri 7:50 PM"), L3 without the flag group (T-103 Q4: the
    /// remaining time stays, the finish time is in the tooltip).</item>
    /// <item>Cooling: C1 "Cooling down… 87 °C · about 6 min", C2 "87 °C · about 6 min" (the snowflake stands for the
    /// word), C3 "87 °C".</item>
    /// <item>Cooled: K1 "Cooled down – safe to turn off" (the widget's wording), K2 "Cooled down · 48 °C".</item>
    /// </list>
    /// </remarks>
    internal static MiniStripTexts Texts(in MiniStripContent content)
    {
        string state = OverlayFormat.StateName(content.State);
        switch (content.Kind)
        {
            case MiniStripKind.Job when content.State == PrinterState.Printing:
            {
                // "19 % · 2 h 35 min" / "Printing, 19 %, 2 h 35 min remaining".
                string percent = OverlayFormat.Percent(content.Percent);
                string summary = content.Minutes is { } minutes ? string.Concat(percent, Separator, OverlayFormat.Duration(minutes)) : percent;
                string accessible = content.Minutes is { } left ? string.Concat(state, ", ", percent, ", ", OverlayFormat.Remaining(left)) : string.Concat(state, ", ", percent);
                if (content.Finish is not { } finish)
                {
                    return new MiniStripTexts(new MiniStripVariant(summary), null, null, accessible);
                }

                string full = OverlayFormat.Finish(finish);
                accessible = string.Concat(accessible, ", ", full);
                // The day of the week only within the next 6 days (T-103 3.1); further away only the full date fits it.
                string? clock = !finish.IsOnAnotherDay ? OverlayFormat.FinishClock(finish)
                    : content.FinishDays is >= 1 and <= 6 ? OverlayFormat.FinishDayTime(finish)
                    : null;
                MiniStripVariant without = new(summary);
                return clock is null
                    ? new MiniStripTexts(new MiniStripVariant(summary, full), without, null, accessible)
                    : new MiniStripTexts(new MiniStripVariant(summary, full), new MiniStripVariant(summary, clock), without, accessible);
            }

            case MiniStripKind.Job:
            {
                // "19 % · Paused" (the number first, the word may be trimmed).
                string percent = OverlayFormat.Percent(content.Percent);
                return new MiniStripTexts(new MiniStripVariant(string.Concat(percent, Separator, state)), null, null, string.Concat(state, ", ", percent));
            }

            case MiniStripKind.Cooling:
            {
                string degrees = Degrees(content.NozzleCelsius);
                MiniStripVariant shortest = new(Format(Strings.DiagnosticsTemperatureValue, degrees));
                if (content.Minutes is not { } left)
                {
                    string noEstimate = Format(Strings.CameraMiniCoolingNoEstimate, degrees);
                    return new MiniStripTexts(new MiniStripVariant(noEstimate), shortest, null, noEstimate);
                }

                string duration = OverlayFormat.Duration(left);
                string full = Format(Strings.CameraMiniCooling, degrees, duration);
                return new MiniStripTexts(new MiniStripVariant(full), new MiniStripVariant(Format(Strings.CameraMiniCoolingShort, degrees, duration)), shortest, full);
            }

            case MiniStripKind.Cooled:
            {
                string degrees = Degrees(content.NozzleCelsius);
                return new MiniStripTexts(
                    new MiniStripVariant(Strings.CameraOverlayCooled),
                    new MiniStripVariant(Format(Strings.CameraMiniCooled, degrees)),
                    null,
                    Format(Strings.CameraMiniCooledAccessible, degrees));
            }

            default:
                return new MiniStripTexts(new MiniStripVariant(state), null, null, state);
        }
    }

    private void Format(in MiniStripContent content)
    {
        MiniStripTexts texts = Texts(content);
        bool kindChanged = _kind != content.Kind;
        _kind = content.Kind;
        FirstVariant = texts.First;
        SecondVariant = texts.Second;
        ThirdVariant = texts.Third;
        Summary = texts.First.Summary;
        AccessibleText = texts.Accessible;
        DotBrush = OverlayStateBrushes.For(content.State);
        Progress = content.Kind switch
        {
            MiniStripKind.Job => content.Percent,
            MiniStripKind.Cooling or MiniStripKind.Cooled => content.CoolingPercent,
            _ => 0,
        };
        HasProgress = content.Kind != MiniStripKind.State;
        if (kindChanged)
        {
            OnPropertyChanged(nameof(ShowsDot));
            OnPropertyChanged(nameof(ShowsCoolingIcon));
            OnPropertyChanged(nameof(ShowsCooledIcon));
        }
    }

    /// <summary>The left column of the chart row: "215°", "60°", and the row in words.</summary>
    private void FormatChart(in MiniChartValues values)
    {
        _chartShown = values;
        _hasChartShown = true;
        NozzleText = values.Nozzle is { } nozzle ? Degrees(nozzle) + "°" : string.Empty;
        BedText = values.Bed is { } bed ? Degrees(bed) + "°" : string.Empty;
        HasNozzle = values.Nozzle is not null;
        HasBed = values.Bed is not null;
        string text = Strings.CameraOverlayChartName;
        if (values.Nozzle is { } n)
        {
            text = string.Concat(text, ", ", Strings.DiagnosticsNozzle, " ", Format(Strings.DiagnosticsTemperatureValue, Degrees(n)));
        }

        if (values.Bed is { } b)
        {
            text = string.Concat(text, ", ", Strings.DiagnosticsBed, " ", Format(Strings.DiagnosticsTemperatureValue, Degrees(b)));
        }

        ChartAccessibleText = text;
    }

    private static string Degrees(long celsius) => celsius.ToString("N0", CultureInfo.CurrentCulture);

    private static string Format(string format, object? arg0) => string.Format(CultureInfo.CurrentCulture, format, arg0);

    private static string Format(string format, object? arg0, object? arg1) => string.Format(CultureInfo.CurrentCulture, format, arg0, arg1);
}
