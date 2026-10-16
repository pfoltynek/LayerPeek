using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

using PrusaTrayDiagnostic.Core.History;

namespace PrusaTrayDiagnostic.App.Diagnostics.Chart;

/// <summary>
/// Lightweight temperature chart over <see cref="TelemetryHistory"/>: nozzle and bed actual temperatures as solid lines,
/// targets as thin dashed lines (only while heating), a value axis in °C and a local-time axis. Lines break at missing
/// values and history segments (e.g. offline, computer asleep).
/// </summary>
/// <remarks>
/// <para>
/// Work happens only while the control is in a visible window: it subscribes to <see cref="TelemetryHistory.Changed"/>
/// when attached to the visual tree and unsubscribes when detached (window closed). Changes of other printers are
/// ignored on the calling thread; a burst of changes posts one redraw request to the UI thread.
/// </para>
/// <para>
/// Rendering: <see cref="TemperatureChartData"/> copies and decimates the samples (one column per pixel) only when the
/// history version, the printer, the range or the width changed; geometries, pens and labels are rebuilt only then or
/// when a brush (theme) changed. Otherwise <see cref="Render"/> just replays the cached objects.
/// </para>
/// </remarks>
public sealed class TemperatureChart : Control
{
    public static readonly StyledProperty<TelemetryHistory?> HistoryProperty =
        AvaloniaProperty.Register<TemperatureChart, TelemetryHistory?>(nameof(History));

    public static readonly StyledProperty<string?> PrinterIdProperty =
        AvaloniaProperty.Register<TemperatureChart, string?>(nameof(PrinterId));

    public static readonly StyledProperty<TimeSpan> TimeWindowProperty =
        AvaloniaProperty.Register<TemperatureChart, TimeSpan>(nameof(TimeWindow), TimeSpan.FromHours(2), coerce: CoerceTimeWindow);

    public static readonly StyledProperty<TimeProvider?> ClockProperty =
        AvaloniaProperty.Register<TemperatureChart, TimeProvider?>(nameof(Clock));

    public static readonly StyledProperty<IBrush?> NozzleBrushProperty =
        AvaloniaProperty.Register<TemperatureChart, IBrush?>(nameof(NozzleBrush), Brushes.OrangeRed);

    public static readonly StyledProperty<IBrush?> BedBrushProperty =
        AvaloniaProperty.Register<TemperatureChart, IBrush?>(nameof(BedBrush), Brushes.RoyalBlue);

    public static readonly StyledProperty<IBrush?> AxisBrushProperty =
        AvaloniaProperty.Register<TemperatureChart, IBrush?>(nameof(AxisBrush), Brushes.Gray);

    public static readonly StyledProperty<IBrush?> GridBrushProperty =
        AvaloniaProperty.Register<TemperatureChart, IBrush?>(nameof(GridBrush), Brushes.LightGray);

    public static readonly StyledProperty<string?> NoDataTextProperty =
        AvaloniaProperty.Register<TemperatureChart, string?>(nameof(NoDataText));

    private const double LeftMargin = 56;
    private const double RightMargin = 12;
    private const double TopMargin = 8;
    private const double BottomMargin = 22;
    private const double LabelFontSize = 11;
    private const double LabelGap = 6;
    private const double ActualThickness = 1.75;
    private const double TargetThickness = 1;

    private static readonly DashStyle TargetDash = new([4, 3], 0);

    private readonly TemperatureChartData _data = new();
    private readonly Action _refreshOnUiThread;
    private readonly StreamGeometry?[] _series = new StreamGeometry?[TemperatureChartData.SeriesCount];
    private readonly IPen?[] _seriesPens = new IPen?[TemperatureChartData.SeriesCount];

    // Labels are created once per axis value / tick time and reused across redraws.
    private readonly ChartLabelCache<double, FormattedText> _valueLabels;
    private readonly ChartLabelCache<long, FormattedText> _timeLabels;
    private readonly List<(FormattedText Text, Point Origin)> _valueLabelPlacements = [];
    private readonly List<(FormattedText Text, Point Origin)> _timeLabelPlacements = [];
    private readonly List<DateTimeOffset> _ticks = [];
    private readonly List<double> _tickXs = [];

    // Read by the Changed handler on monitor threads.
    private string? _printerId;
    private int _refreshPosted;

    // UI thread only.
    private TelemetryHistory? _subscribedHistory;
    private bool _isAttached;
    private bool _refreshRequested = true;
    private bool _visualsDirty = true;
    private bool _pensDirty = true;
    private DateTimeOffset _to;
    private Rect _plot;
    private double _top;
    private double _step;
    private CultureInfo? _labelCulture;
    private TimeZoneInfo _labelZone = TimeZoneInfo.Utc;
    private StreamGeometry? _valueGrid;
    private FormattedText? _noDataLabel;
    private bool _showNoData;
    private IPen? _gridPen;

    static TemperatureChart()
    {
        AffectsRender<TemperatureChart>(
            HistoryProperty,
            PrinterIdProperty,
            TimeWindowProperty,
            ClockProperty,
            NozzleBrushProperty,
            BedBrushProperty,
            AxisBrushProperty,
            GridBrushProperty,
            NoDataTextProperty);
    }

    public TemperatureChart()
    {
        _refreshOnUiThread = OnRefreshPosted;
        _valueLabels = new ChartLabelCache<double, FormattedText>(
            value => Label(DiagnosticsFormatter.AxisTemperature(value), AxisBrush),
            capacity: 32);
        _timeLabels = new ChartLabelCache<long, FormattedText>(
            utcTicks => Label(TimeZoneInfo.ConvertTime(new DateTimeOffset(utcTicks, TimeSpan.Zero), _labelZone).ToString("t", CultureInfo.CurrentCulture), AxisBrush),
            capacity: 64);
        ClipToBounds = true;
    }

    public TelemetryHistory? History
    {
        get => GetValue(HistoryProperty);
        set => SetValue(HistoryProperty, value);
    }

    /// <summary>Printer to show; <see langword="null"/> = empty chart.</summary>
    public string? PrinterId
    {
        get => GetValue(PrinterIdProperty);
        set => SetValue(PrinterIdProperty, value);
    }

    /// <summary>Shown range ending now (at least one minute).</summary>
    public TimeSpan TimeWindow
    {
        get => GetValue(TimeWindowProperty);
        set => SetValue(TimeWindowProperty, value);
    }

    /// <summary>Source of "now" and of the local time zone; <see langword="null"/> = <see cref="TimeProvider.System"/>.</summary>
    public TimeProvider? Clock
    {
        get => GetValue(ClockProperty);
        set => SetValue(ClockProperty, value);
    }

    public IBrush? NozzleBrush
    {
        get => GetValue(NozzleBrushProperty);
        set => SetValue(NozzleBrushProperty, value);
    }

    public IBrush? BedBrush
    {
        get => GetValue(BedBrushProperty);
        set => SetValue(BedBrushProperty, value);
    }

    /// <summary>Axis labels and the "no data" text.</summary>
    public IBrush? AxisBrush
    {
        get => GetValue(AxisBrushProperty);
        set => SetValue(AxisBrushProperty, value);
    }

    public IBrush? GridBrush
    {
        get => GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    /// <summary>Shown in the middle when the range has no temperature.</summary>
    public string? NoDataText
    {
        get => GetValue(NoDataTextProperty);
        set => SetValue(NoDataTextProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        Rect plot = new(LeftMargin, TopMargin, Bounds.Width - LeftMargin - RightMargin, Bounds.Height - TopMargin - BottomMargin);
        if (plot.Width < 1 || plot.Height < 1)
        {
            return;
        }

        TimeProvider clock = Clock ?? TimeProvider.System;
        TelemetryHistory? history = History;
        string? printerId = PrinterId;
        // "Now" moves only when new data arrived or something was requested, so a resize does not shift the time axis.
        if (_refreshRequested || (history is not null && printerId is not null && history.GetVersion(printerId) != _data.Version))
        {
            _to = clock.GetUtcNow();
            _refreshRequested = false;
        }

        bool dataChanged = history is not null && _data.Update(history, printerId, _to - TimeWindow, _to, (int)plot.Width);
        bool hasData = history is not null && float.IsFinite(_data.MaxValue);
        EnsurePens();
        EnsureLabelCaches(clock);

        // Rebuilt by need: the value axis only when its range or the size changes; the time ticks (positions and
        // possibly one new label) and the series geometries with every data change; nothing when only redrawn.
        (double top, double step) = ChartScale.ValueAxis(_data.MaxValue, Math.Max(2, (int)(plot.Height / 36)));
        bool layoutChanged = _visualsDirty || plot != _plot || top != _top || step != _step;
        if (layoutChanged)
        {
            RebuildValueAxis(plot, top, step);
        }

        if (layoutChanged || dataChanged)
        {
            RebuildTimeAxis(plot);
            RebuildSeries(plot, top, hasData);
        }

        _plot = plot;
        _top = top;
        _step = step;
        _visualsDirty = false;

        if (_gridPen is not null)
        {
            if (_valueGrid is not null)
            {
                context.DrawGeometry(null, _gridPen, _valueGrid);
            }

            foreach (double x in _tickXs)
            {
                context.DrawLine(_gridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
            }
        }

        foreach ((FormattedText text, Point origin) in _valueLabelPlacements)
        {
            context.DrawText(text, origin);
        }

        foreach ((FormattedText text, Point origin) in _timeLabelPlacements)
        {
            context.DrawText(text, origin);
        }

        using (context.PushClip(_plot))
        {
            // Targets below actual values, bed below nozzle.
            DrawSeries(context, TemperatureSeries.BedTarget);
            DrawSeries(context, TemperatureSeries.NozzleTarget);
            DrawSeries(context, TemperatureSeries.BedActual);
            DrawSeries(context, TemperatureSeries.NozzleActual);
        }

        if (_showNoData && _noDataLabel is not null)
        {
            context.DrawText(_noDataLabel, new Point(_plot.Center.X - (_noDataLabel.Width / 2), _plot.Center.Y - (_noDataLabel.Height / 2)));
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        AvaloniaProperty property = change.Property;
        if (property == HistoryProperty)
        {
            if (_isAttached)
            {
                Subscribe(History);
            }

            _refreshRequested = true;
        }
        else if (property == PrinterIdProperty)
        {
            Volatile.Write(ref _printerId, PrinterId);
            _refreshRequested = true;
        }
        else if (property == TimeWindowProperty || property == ClockProperty)
        {
            _refreshRequested = true;
        }
        else if (property == NozzleBrushProperty || property == BedBrushProperty || property == GridBrushProperty)
        {
            _pensDirty = true;
            _visualsDirty = true;
        }
        else if (property == AxisBrushProperty)
        {
            // Labels carry their brush: a theme change recreates them.
            ClearLabels();
            _visualsDirty = true;
        }
        else if (property == NoDataTextProperty)
        {
            _noDataLabel = null;
            _visualsDirty = true;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        Subscribe(History);
        _refreshRequested = true;
        InvalidateVisual();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        // The window closed: no more work for history changes.
        _isAttached = false;
        Subscribe(null);
        base.OnDetachedFromVisualTree(e);
    }

    private static TimeSpan CoerceTimeWindow(AvaloniaObject sender, TimeSpan value) =>
        value < TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : value;

    private static StreamGeometry BuildSeries(ReadOnlySpan<ChartColumn> columns, Rect plot, double top)
    {
        StreamGeometry geometry = new();
        using StreamGeometryContext line = geometry.Open();
        bool open = false;
        for (int i = 0; i < columns.Length; i++)
        {
            ChartColumn column = columns[i];
            double x = plot.Left + column.Column + 0.5;
            Point first = new(x, ValueToY(column.First, plot, top));
            if (!open || column.StartsLine)
            {
                if (open)
                {
                    line.EndFigure(false);
                }

                line.BeginFigure(first, isFilled: false);
                open = true;
                bool continues = i + 1 < columns.Length && !columns[i + 1].StartsLine;
                if (!continues && column.Min == column.Max)
                {
                    // A lone sample would be a zero-length line, which is not drawn: show a short dash instead.
                    line.LineTo(new Point(x + 2, first.Y));
                    continue;
                }
            }
            else
            {
                line.LineTo(first);
            }

            if (column.Max > column.Min)
            {
                // The column's whole range, so that no peak disappears in decimation.
                line.LineTo(new Point(x, ValueToY(column.Max, plot, top)));
                line.LineTo(new Point(x, ValueToY(column.Min, plot, top)));
                line.LineTo(new Point(x, ValueToY(column.Last, plot, top)));
            }
        }

        if (open)
        {
            line.EndFigure(false);
        }

        return geometry;
    }

    private static double ValueToY(double value, Rect plot, double top) => plot.Bottom - (value / top * plot.Height);

    private static FormattedText Label(string text, IBrush? brush) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, LabelFontSize, brush ?? Brushes.Gray);

    private void Subscribe(TelemetryHistory? history)
    {
        if (ReferenceEquals(history, _subscribedHistory))
        {
            return;
        }

        if (_subscribedHistory is not null)
        {
            _subscribedHistory.Changed -= OnHistoryChanged;
        }

        _subscribedHistory = history;
        if (history is not null)
        {
            history.Changed += OnHistoryChanged;
        }
    }

    /// <summary>Monitor thread: only the shown printer counts, and one redraw request is pending at most.</summary>
    private void OnHistoryChanged(object? sender, string printerId)
    {
        if (string.Equals(printerId, Volatile.Read(ref _printerId), StringComparison.OrdinalIgnoreCase)
            && Interlocked.Exchange(ref _refreshPosted, 1) == 0)
        {
            Dispatcher.UIThread.Post(_refreshOnUiThread, DispatcherPriority.Background);
        }
    }

    private void OnRefreshPosted()
    {
        Volatile.Write(ref _refreshPosted, 0);
        _refreshRequested = true;
        InvalidateVisual();
    }

    private void DrawSeries(DrawingContext context, TemperatureSeries series)
    {
        int index = (int)series;
        if (_series[index] is { } geometry && _seriesPens[index] is { } pen)
        {
            context.DrawGeometry(null, pen, geometry);
        }
    }

    private void EnsurePens()
    {
        if (!_pensDirty)
        {
            return;
        }

        _pensDirty = false;
        _gridPen = GridBrush is { } grid ? new Pen(grid, 1) : null;
        _seriesPens[(int)TemperatureSeries.NozzleActual] = SeriesPen(NozzleBrush, ActualThickness, null);
        _seriesPens[(int)TemperatureSeries.NozzleTarget] = SeriesPen(NozzleBrush, TargetThickness, TargetDash);
        _seriesPens[(int)TemperatureSeries.BedActual] = SeriesPen(BedBrush, ActualThickness, null);
        _seriesPens[(int)TemperatureSeries.BedTarget] = SeriesPen(BedBrush, TargetThickness, TargetDash);
    }

    private static Pen? SeriesPen(IBrush? brush, double thickness, IDashStyle? dash) =>
        brush is null ? null : new Pen(brush, thickness, dash, PenLineCap.Round, PenLineJoin.Round);

    /// <summary>Drops cached labels when the culture or the time zone changed (the brush is handled in <see cref="OnPropertyChanged"/>).</summary>
    private void EnsureLabelCaches(TimeProvider clock)
    {
        CultureInfo culture = CultureInfo.CurrentCulture;
        if (!ReferenceEquals(culture, _labelCulture))
        {
            _labelCulture = culture;
            ClearLabels();
            _visualsDirty = true;
        }

        TimeZoneInfo zone = clock.LocalTimeZone;
        if (!ReferenceEquals(zone, _labelZone))
        {
            _labelZone = zone;
            _timeLabels.Clear();
            _visualsDirty = true;
        }
    }

    private void ClearLabels()
    {
        _valueLabels.Clear();
        _timeLabels.Clear();
        _noDataLabel = null;
    }

    /// <summary>Horizontal grid lines and °C labels; only when the value range or the size changed.</summary>
    private void RebuildValueAxis(Rect plot, double top, double step)
    {
        _valueLabelPlacements.Clear();
        StreamGeometry grid = new();
        using (StreamGeometryContext lines = grid.Open())
        {
            for (int i = 0; i * step <= top + (step / 2); i++)
            {
                double value = i * step;
                double y = Math.Round(ValueToY(value, plot, top)) + 0.5;
                lines.BeginFigure(new Point(plot.Left, y), isFilled: false);
                lines.LineTo(new Point(plot.Right, y));
                lines.EndFigure(false);
                FormattedText label = _valueLabels.Get(value);
                _valueLabelPlacements.Add((label, new Point(plot.Left - LabelGap - label.Width, y - (label.Height / 2))));
            }
        }

        _valueGrid = grid;
    }

    /// <summary>Tick positions and labels at round local times (DST-aware); a label is created only for a new tick.</summary>
    private void RebuildTimeAxis(Rect plot)
    {
        TimeSpan window = TimeWindow;
        DateTimeOffset from = _to - window;
        TimeSpan step = ChartScale.TimeStep(window, Math.Max(2, (int)(plot.Width / 90)));
        ChartScale.TimeTicks(from, _to, step, _labelZone, _ticks);
        _tickXs.Clear();
        _timeLabelPlacements.Clear();
        foreach (DateTimeOffset tick in _ticks)
        {
            double x = Math.Round(plot.Left + ((tick - from).Ticks / (double)window.Ticks * plot.Width)) + 0.5;
            _tickXs.Add(x);
            FormattedText label = _timeLabels.Get(tick.UtcTicks);
            double labelX = Math.Clamp(x - (label.Width / 2), 0, Math.Max(0, Bounds.Width - label.Width));
            _timeLabelPlacements.Add((label, new Point(labelX, plot.Bottom + 3)));
        }
    }

    /// <summary>Line geometries of the series; with every data change (the only per-poll allocation besides a new tick label).</summary>
    private void RebuildSeries(Rect plot, double top, bool hasData)
    {
        for (int s = 0; s < TemperatureChartData.SeriesCount; s++)
        {
            ReadOnlySpan<ChartColumn> columns = _data.GetColumns((TemperatureSeries)s);
            _series[s] = hasData && !columns.IsEmpty ? BuildSeries(columns, plot, top) : null;
        }

        _showNoData = !hasData;
        if (_showNoData && _noDataLabel is null && !string.IsNullOrEmpty(NoDataText))
        {
            _noDataLabel = Label(NoDataText, AxisBrush);
        }
    }
}
