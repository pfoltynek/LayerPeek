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
internal readonly record struct MiniStripContent(MiniStripKind Kind, PrinterState? State, int Percent, long? Minutes, long NozzleCelsius)
{
    /// <summary>
    /// The content for <paramref name="snapshot"/> and the cool-down state of the printer: the active job first (the next
    /// print ends any cool-down), then the cool-down of a reachable printer, else the state name. Allocates nothing.
    /// </summary>
    public static MiniStripContent Of(PrinterSnapshot? snapshot, in CooldownState cooldown)
    {
        PrinterState? state = snapshot?.State;
        if (OverlayValues.ActiveJob(snapshot) is { } job)
        {
            // The remaining time only while printing; otherwise the state stands next to the percent (T-076 4).
            long? remaining = state == PrinterState.Printing ? OverlayValues.RemainingMinutes(job) : null;
            return new MiniStripContent(MiniStripKind.Job, state, OverlayValues.ProgressPercent(job), remaining, 0);
        }

        if (snapshot is not null
            && snapshot.State.IsReachable()
            && cooldown.Phase != CooldownPhase.None
            && OverlayValues.Temperature(new Temperature(cooldown.NozzleCelsius ?? double.NaN, null)) is { } nozzle)
        {
            return cooldown.Phase == CooldownPhase.Cooling
                ? new MiniStripContent(MiniStripKind.Cooling, state, 0, cooldown.EstimatedRemaining is { } left ? ToolTipContent.CeilingMinutes(left) : null, nozzle.Actual)
                : new MiniStripContent(MiniStripKind.Cooled, state, 0, null, nozzle.Actual);
        }

        return new MiniStripContent(MiniStripKind.State, state, 0, null, 0);
    }
}

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
/// cooling down the nozzle temperature (T-079). The full text is the row's tooltip and accessible name.
/// </summary>
/// <remarks>
/// <para>
/// Snapshots and cool-down changes arrive on the monitor thread: the content is computed there under a lock with the shared
/// rules of the Status and Times widgets (<see cref="OverlayValues"/>, <see cref="OverlayFormat"/>), and one update is
/// posted to the UI thread only when it changed (a poll that changes nothing visible allocates nothing). Texts are
/// formatted on the UI thread, and again after a language switch.
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

    /// <summary>"19 % · 2 h 35 min", "19 % · Paused", "Ready", "Cooling down… 87 °C · about 6 min" (number first).</summary>
    public string Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    /// <summary>The whole content in words: tooltip and accessible name of the summary row (the row trims its text).</summary>
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

    /// <summary>0–100: the shown whole percent of the active job (the bar agrees with the number).</summary>
    public double Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

    /// <summary>An active job: the bar has its fill and its normal track; without one only a faint track (same height).</summary>
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

        _name.Dispose();
    }

    // Monitor thread (or whoever configures the cool-down monitor).
    private void OnCooldownChanged(object? sender, string printerId)
    {
        if (!string.Equals(printerId, _printerId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

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
        MiniStripContent content = MiniStripContent.Of(_snapshot, cooldown);
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
    /// The texts of <paramref name="content"/> in the current language (T-076 4): the summary row (the number first) and
    /// the whole content in words for the tooltip and screen readers.
    /// </summary>
    internal static (string Summary, string Accessible) Texts(in MiniStripContent content)
    {
        string state = OverlayFormat.StateName(content.State);
        string percent = content.Kind == MiniStripKind.Job ? OverlayFormat.Percent(content.Percent) : string.Empty;
        return content.Kind switch
        {
            // "19 % · 2 h 35 min" / "Printing, 19 %, 2 h 35 min remaining".
            MiniStripKind.Job when content.Minutes is { } minutes && content.State == PrinterState.Printing =>
                (string.Concat(percent, Separator, OverlayFormat.Duration(minutes)), string.Concat(state, ", ", percent, ", ", OverlayFormat.Remaining(minutes))),
            MiniStripKind.Job when content.State == PrinterState.Printing => (percent, string.Concat(state, ", ", percent)),
            // "19 % · Paused" (the number first, the word may be trimmed).
            MiniStripKind.Job => (string.Concat(percent, Separator, state), string.Concat(state, ", ", percent)),
            MiniStripKind.Cooling => Same(content.Minutes is { } left
                ? Format(Strings.CameraMiniCooling, Degrees(content.NozzleCelsius), OverlayFormat.Duration(left))
                : Format(Strings.CameraMiniCoolingNoEstimate, Degrees(content.NozzleCelsius))),
            MiniStripKind.Cooled => Same(Format(Strings.CameraMiniCooled, Degrees(content.NozzleCelsius))),
            _ => Same(state),
        };

        static (string, string) Same(string text) => (text, text);
    }

    private void Format(in MiniStripContent content)
    {
        (string summary, string accessible) = Texts(content);
        bool kindChanged = _kind != content.Kind;
        _kind = content.Kind;
        Summary = summary;
        AccessibleText = accessible;
        DotBrush = OverlayStateBrushes.For(content.State);
        Progress = content.Kind == MiniStripKind.Job ? content.Percent : 0;
        HasProgress = content.Kind == MiniStripKind.Job;
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
