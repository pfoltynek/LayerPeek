using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Avalonia.VisualTree;

using LayerPeek.Core.History;

namespace LayerPeek.App.Diagnostics.Chart;

/// <summary>
/// Lightweight temperature chart over <see cref="TelemetryHistory"/>: nozzle and bed actual temperatures as solid lines,
/// targets as thin dashed lines (only while heating), a value axis in °C and a local-time axis. Lines break at missing
/// values and history segments (e.g. offline, computer asleep). One control for every place that draws temperatures:
/// the diagnostics window (axes, grid, selectable range) and, in the compact mode (<see cref="IsCompact"/>, T-084), the
/// sparkline of the camera overlay's Temperatures widget.
/// </summary>
/// <remarks>
/// <para>
/// Work happens only while the control is shown: it subscribes to <see cref="TelemetryHistory.Changed"/> while it is
/// attached to the visual tree <b>and</b> visible with all its ancestors, and unsubscribes when detached (window closed)
/// or hidden (a hidden card or window, a hidden parent; the ancestors' IsVisible is watched while attached); when it is
/// shown again it redraws with the latest history. A minimized window still counts as shown. Changes of other
/// printers are ignored on the calling thread; a burst of changes posts one redraw request to the UI thread.
/// </para>
/// <para>
/// Rendering: <see cref="TemperatureChartData"/> copies and decimates the samples (one column per pixel) only when the
/// history version, the printer, the range or the width changed; geometries, pens and labels are rebuilt only then or
/// when a brush (theme) changed. Otherwise <see cref="Render"/> just replays the cached objects.
/// </para>
/// <para>
/// Compact mode: no axes, labels or grid (the plot is the whole control), a fixed range of
/// <see cref="CompactTimeWindow"/> ending now, and a sample buffer sized to that range instead of the whole history.
/// The look of the lines comes from <see cref="LineThickness"/> and <see cref="TargetOpacity"/>; "no data" is drawn in
/// <see cref="AxisBrush"/>. The control draws no background. Instead of the value axis from 0 °C, every series with
/// values in the hour gets its own band (nozzle on top, bed below), scaled to its own minimum and maximum with at least
/// <see cref="CompactBands.MinimumSpan"/> (T-106), so that a fluctuation of 1–2 °C is visible. A target is drawn in the
/// band of its series, clipped to it: the band is scaled to the actual values only, so a target far away from them
/// (e.g. at the start of heating) is not drawn instead of squeezing the actual line flat. Which bands exist depends on
/// which series have values in the hour, so the layout changes only when a series enters or leaves that range.
/// </para>
/// </remarks>
public sealed class TemperatureChart : Control
{
    /// <summary>The fixed range of the compact chart (user decision 2026-10-24, T-084 Q1).</summary>
    public static readonly TimeSpan CompactTimeWindow = TimeSpan.FromMinutes(60);

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

    public static readonly StyledProperty<bool> IsCompactProperty =
        AvaloniaProperty.Register<TemperatureChart, bool>(nameof(IsCompact));

    public static readonly StyledProperty<double> LineThicknessProperty =
        AvaloniaProperty.Register<TemperatureChart, double>(nameof(LineThickness), 1.75, coerce: CoerceLineThickness);

    public static readonly StyledProperty<double> TargetOpacityProperty =
        AvaloniaProperty.Register<TemperatureChart, double>(nameof(TargetOpacity), 1, coerce: CoerceOpacity);

    public static readonly StyledProperty<double> BandRangeWidthProperty =
        AvaloniaProperty.Register<TemperatureChart, double>(nameof(BandRangeWidth), 0, coerce: CoerceBandRangeWidth);

    public static readonly StyledProperty<string?> BandRangeFormatProperty =
        AvaloniaProperty.Register<TemperatureChart, string?>(nameof(BandRangeFormat));

    public static readonly StyledProperty<IBrush?> BandRangeBrushProperty =
        AvaloniaProperty.Register<TemperatureChart, IBrush?>(nameof(BandRangeBrush), Brushes.Gray);

    public static readonly StyledProperty<double> BandRangeFontSizeProperty =
        AvaloniaProperty.Register<TemperatureChart, double>(nameof(BandRangeFontSize), 12, coerce: CoerceFontSize);

    private const double LeftMargin = 56;
    private const double RightMargin = 12;
    private const double TopMargin = 8;
    private const double BottomMargin = 22;
    private const double LabelFontSize = 11;
    private const double CompactNoDataFontSize = 12;
    private const double LabelGap = 6;
    private const double TargetThickness = 1;

    private static readonly DashStyle TargetDash = new([4, 3], 0);

    private readonly TemperatureChartData _data = new();
    private readonly Action _refreshOnUiThread;
    private readonly EventHandler<AvaloniaPropertyChangedEventArgs> _ancestorChanged;

    // UI thread: the visual ancestors while attached, watched for IsVisible changes.
    private readonly List<Visual> _ancestors = [];
    private readonly StreamGeometry?[] _series = new StreamGeometry?[TemperatureChartData.SeriesCount];
    private readonly IPen?[] _seriesPens = new IPen?[TemperatureChartData.SeriesCount];

    // The value-to-pixel mapping of each series and the rectangle it is clipped to (the plot, or its band when compact).
    private readonly ValueBand?[] _bands = new ValueBand?[TemperatureChartData.SeriesCount];
    private readonly Rect[] _bandClips = new Rect[TemperatureChartData.SeriesCount];

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

    // Range labels of the compact bands (T-106, L widget only): text, whole degrees it shows, and where it is drawn.
    private readonly FormattedText?[] _rangeLabels = new FormattedText?[TemperatureChartData.SeriesCount];
    private readonly string?[] _rangeTexts = new string?[TemperatureChartData.SeriesCount];
    private readonly (long Low, long High)[] _rangeLabelValues = new (long, long)[TemperatureChartData.SeriesCount];
    private readonly Point[] _rangeLabelOrigins = new Point[TemperatureChartData.SeriesCount];
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
            NoDataTextProperty,
            IsCompactProperty,
            LineThicknessProperty,
            TargetOpacityProperty,
            BandRangeWidthProperty,
            BandRangeFormatProperty,
            BandRangeBrushProperty,
            BandRangeFontSizeProperty);
    }

    public TemperatureChart()
    {
        _refreshOnUiThread = OnRefreshPosted;
        _valueLabels = new ChartLabelCache<double, FormattedText>(
            value => Label(DiagnosticsFormatter.AxisTemperature(value), AxisBrush, LabelFontSize),
            capacity: 32);
        _timeLabels = new ChartLabelCache<long, FormattedText>(
            utcTicks => Label(TimeZoneInfo.ConvertTime(new DateTimeOffset(utcTicks, TimeSpan.Zero), _labelZone).ToString("t", CultureInfo.CurrentCulture), AxisBrush, LabelFontSize),
            capacity: 64);
        ClipToBounds = true;
        _ancestorChanged = OnAncestorPropertyChanged;
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

    /// <summary>Shown range ending now (at least one minute); the compact chart always shows <see cref="CompactTimeWindow"/>.</summary>
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

    /// <summary>
    /// The sparkline of the overlay (T-084): no axes, labels or grid, the plot fills the control, the range is
    /// <see cref="CompactTimeWindow"/> and "no data" is 12 instead of 11.
    /// </summary>
    public bool IsCompact
    {
        get => GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    /// <summary>Thickness of the actual temperature lines (the dashed targets are always 1).</summary>
    public double LineThickness
    {
        get => GetValue(LineThicknessProperty);
        set => SetValue(LineThicknessProperty, value);
    }

    /// <summary>Opacity of the dashed target lines on top of their brush's own (0–1; the overlay draws them at 70 %).</summary>
    public double TargetOpacity
    {
        get => GetValue(TargetOpacityProperty);
        set => SetValue(TargetOpacityProperty, value);
    }

    /// <summary>
    /// Compact mode only: width of a column at the right edge, next to the plot, that shows the value range of every band
    /// (<see cref="BandRangeFormat"/>, T-106); 0 = no column and no range labels (default). The large Temperatures widget
    /// uses it; the plot is narrower by this width, so a label never covers a line.
    /// </summary>
    public double BandRangeWidth
    {
        get => GetValue(BandRangeWidthProperty);
        set => SetValue(BandRangeWidthProperty, value);
    }

    /// <summary>Composite format of a band's range label: {0} = lowest, {1} = highest whole degree of the band.</summary>
    public string? BandRangeFormat
    {
        get => GetValue(BandRangeFormatProperty);
        set => SetValue(BandRangeFormatProperty, value);
    }

    public IBrush? BandRangeBrush
    {
        get => GetValue(BandRangeBrushProperty);
        set => SetValue(BandRangeBrushProperty, value);
    }

    public double BandRangeFontSize
    {
        get => GetValue(BandRangeFontSizeProperty);
        set => SetValue(BandRangeFontSizeProperty, value);
    }

    /// <summary>The text of a series' range label of the last render (tests); <see langword="null"/> when none is drawn.</summary>
    internal string? BandRangeTextOf(TemperatureSeries series) => _rangeLabels[(int)series] is null ? null : _rangeTexts[(int)series];

    /// <summary>Where the range label of a series is drawn (tests): its bounds in the control.</summary>
    internal Rect BandRangeBoundsOf(TemperatureSeries series) =>
        _rangeLabels[(int)series] is { } label ? new Rect(_rangeLabelOrigins[(int)series], new Size(label.WidthIncludingTrailingWhitespace, label.Height)) : default;

    /// <summary>Range labels are drawn: compact, a column for them and a format.</summary>
    private bool ShowsBandRanges => IsCompact && BandRangeWidth > 0 && !string.IsNullOrEmpty(BandRangeFormat);

    /// <summary>The prepared data of the last render (tests).</summary>
    internal TemperatureChartData Data => _data;

    /// <summary>The chart listens to <see cref="TelemetryHistory.Changed"/> (tests: nothing while closed or hidden).</summary>
    internal bool IsSubscribedToHistory => _subscribedHistory is not null;

    /// <summary>The last render drew "no data" instead of lines.</summary>
    internal bool ShowsNoData => _showNoData;

    /// <summary>The last render drew axis labels or a grid.</summary>
    internal bool HasAxes => _valueGrid is not null || _valueLabelPlacements.Count > 0 || _timeLabelPlacements.Count > 0;

    /// <summary>How often the line geometries were rebuilt (tests: only for new data, a new size or new brushes).</summary>
    internal int SeriesRebuilds { get; private set; }

    /// <summary>The pen a series is drawn with; <see langword="null"/> before the first render or without a brush.</summary>
    internal IPen? SeriesPenOf(TemperatureSeries series) => _seriesPens[(int)series];

    /// <summary>
    /// The value-to-pixel mapping a series was last drawn with (tests): the axis from 0 °C over the plot, or the series'
    /// band when compact; <see langword="null"/> when the series has no band (no data).
    /// </summary>
    internal ValueBand? BandOf(TemperatureSeries series) => _bands[(int)series];

    /// <summary>The line geometry of a series of the last render (tests); <see langword="null"/> when not drawn.</summary>
    internal Geometry? GeometryOf(TemperatureSeries series) => _series[(int)series];

    /// <summary>The rectangle a series was last clipped to (tests): the plot, or the series' band when compact.</summary>
    internal Rect ClipOf(TemperatureSeries series) => _bandClips[(int)series];

    /// <summary>The shown range: <see cref="CompactTimeWindow"/> in the compact mode, otherwise <see cref="TimeWindow"/>.</summary>
    private TimeSpan EffectiveTimeWindow => IsCompact ? CompactTimeWindow : TimeWindow;

    public override void Render(DrawingContext context)
    {
        bool compact = IsCompact;
        Rect plot = compact
            ? new Rect(0, 0, ShowsBandRanges ? Bounds.Width - BandRangeWidth : Bounds.Width, Bounds.Height)
            : new(LeftMargin, TopMargin, Bounds.Width - LeftMargin - RightMargin, Bounds.Height - TopMargin - BottomMargin);
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

        bool dataChanged = history is not null && _data.Update(history, printerId, _to - EffectiveTimeWindow, _to, (int)plot.Width);
        bool hasData = history is not null && float.IsFinite(_data.MaxValue);
        EnsurePens();
        EnsureLabelCaches(clock);

        // Rebuilt by need: the value axis only when its range or the size changes; the time ticks (positions and
        // possibly one new label) and the series geometries with every data change; nothing when only redrawn.
        (double top, double step) = ChartScale.ValueAxis(_data.MaxValue, Math.Max(2, (int)(plot.Height / 36)));
        bool layoutChanged = _visualsDirty || plot != _plot || top != _top || step != _step;
        if (layoutChanged)
        {
            RebuildValueAxis(plot, top, step, compact);
        }

        if (layoutChanged || dataChanged)
        {
            RebuildTimeAxis(plot, compact);
            RebuildSeries(plot, top, hasData, compact);
            RebuildBandRanges();
        }

        _plot = plot;
        _top = top;
        _step = step;
        _visualsDirty = false;

        if (_gridPen is not null && !compact)
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
            DrawSeries(context, TemperatureSeries.BedTarget, compact);
            DrawSeries(context, TemperatureSeries.NozzleTarget, compact);
            DrawSeries(context, TemperatureSeries.BedActual, compact);
            DrawSeries(context, TemperatureSeries.NozzleActual, compact);
        }

        for (int s = 0; s < TemperatureChartData.SeriesCount; s++)
        {
            if (_rangeLabels[s] is { } rangeLabel)
            {
                context.DrawText(rangeLabel, _rangeLabelOrigins[s]);
            }
        }

        if (_showNoData && _noDataLabel is not null)
        {
            // Compact: in the middle of the whole control, also when a column for range labels narrows the plot.
            Point centre = compact ? new Rect(Bounds.Size).Center : _plot.Center;
            context.DrawText(_noDataLabel, new Point(centre.X - (_noDataLabel.Width / 2), centre.Y - (_noDataLabel.Height / 2)));
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        AvaloniaProperty property = change.Property;
        if (property == IsVisibleProperty)
        {
            OnVisibilityChanged();
        }
        else if (property == HistoryProperty)
        {
            UpdateSubscription();
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
        else if (property == IsCompactProperty)
        {
            // The compact chart keeps only its hour of samples, not a buffer of the whole history (up to ~4 MB).
            _data.SizeBufferToRange = IsCompact;
            _noDataLabel = null;
            _refreshRequested = true;
            _visualsDirty = true;
        }
        else if (property == NozzleBrushProperty || property == BedBrushProperty || property == GridBrushProperty
            || property == LineThicknessProperty || property == TargetOpacityProperty)
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
        else if (property == BandRangeFormatProperty || property == BandRangeBrushProperty || property == BandRangeFontSizeProperty)
        {
            ClearRangeLabels();
            _visualsDirty = true;
        }
        else if (property == BandRangeWidthProperty)
        {
            _visualsDirty = true;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        // Avalonia raises no public event when an ancestor hides this control: watch the ancestors' IsVisible.
        for (Visual? ancestor = this.GetVisualParent(); ancestor is not null; ancestor = ancestor.GetVisualParent())
        {
            ancestor.PropertyChanged += _ancestorChanged;
            _ancestors.Add(ancestor);
        }

        UpdateSubscription();
        _refreshRequested = true;
        InvalidateVisual();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        // The window closed: no more work for history changes.
        _isAttached = false;
        foreach (Visual ancestor in _ancestors)
        {
            ancestor.PropertyChanged -= _ancestorChanged;
        }

        _ancestors.Clear();
        UpdateSubscription();
        base.OnDetachedFromVisualTree(e);
    }

    private static TimeSpan CoerceTimeWindow(AvaloniaObject sender, TimeSpan value) =>
        value < TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : value;

    private static double CoerceLineThickness(AvaloniaObject sender, double value) =>
        double.IsFinite(value) && value > 0 ? value : 1;

    private static double CoerceBandRangeWidth(AvaloniaObject sender, double value) =>
        double.IsFinite(value) && value > 0 ? value : 0;

    private static double CoerceFontSize(AvaloniaObject sender, double value) =>
        double.IsFinite(value) && value > 0 ? value : 12;

    private static double CoerceOpacity(AvaloniaObject sender, double value) =>
        double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 1;

    private static StreamGeometry BuildSeries(ReadOnlySpan<ChartColumn> columns, double left, in ValueBand band)
    {
        StreamGeometry geometry = new();
        using StreamGeometryContext line = geometry.Open();
        bool open = false;
        for (int i = 0; i < columns.Length; i++)
        {
            ChartColumn column = columns[i];
            double x = left + column.Column + 0.5;
            Point first = new(x, band.ToY(column.First));
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
                line.LineTo(new Point(x, band.ToY(column.Max)));
                line.LineTo(new Point(x, band.ToY(column.Min)));
                line.LineTo(new Point(x, band.ToY(column.Last)));
            }
        }

        if (open)
        {
            line.EndFigure(false);
        }

        return geometry;
    }

    /// <summary>The value axis of the full chart: 0 °C at the bottom of the plot, <paramref name="top"/> at its top.</summary>
    private static ValueBand FullAxis(Rect plot, double top) => new(plot.Bottom, plot.Height, 0, top);

    /// <summary>The actual series whose band a target series shares (a series is its own).</summary>
    private static TemperatureSeries BandSeriesOf(TemperatureSeries series) => series switch
    {
        TemperatureSeries.NozzleTarget => TemperatureSeries.NozzleActual,
        TemperatureSeries.BedTarget => TemperatureSeries.BedActual,
        _ => series,
    };

    private static FormattedText Label(string text, IBrush? brush, double fontSize) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, fontSize, brush ?? Brushes.Gray);

    private static Pen? SeriesPen(IBrush? brush, double thickness, IDashStyle? dash) =>
        brush is null ? null : new Pen(brush, thickness, dash, PenLineCap.Round, PenLineJoin.Round);

    /// <summary><paramref name="brush"/> with its opacity multiplied by <paramref name="opacity"/> (solid colours only).</summary>
    private static IBrush? Faded(IBrush? brush, double opacity) =>
        opacity < 1 && brush is ISolidColorBrush solid ? new ImmutableSolidColorBrush(solid.Color, solid.Opacity * opacity) : brush;

    /// <summary>Listens to the history only while attached and shown (this control and all its ancestors visible).</summary>
    private void UpdateSubscription() => Subscribe(IsShownInTree() ? History : null);

    /// <summary>
    /// Like <see cref="Visual.IsEffectivelyVisible"/>, but computed from the watched ancestors, so that it is right
    /// while an ancestor's IsVisible change is still being raised.
    /// </summary>
    private bool IsShownInTree()
    {
        if (!_isAttached || !IsVisible)
        {
            return false;
        }

        foreach (Visual ancestor in _ancestors)
        {
            if (!ancestor.IsVisible)
            {
                return false;
            }
        }

        return true;
    }

    private void OnAncestorPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty)
        {
            OnVisibilityChanged();
        }
    }

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

    /// <summary>Shown again: listen and redraw with what was recorded meanwhile; hidden: stop listening.</summary>
    private void OnVisibilityChanged()
    {
        bool wasSubscribed = _subscribedHistory is not null;
        UpdateSubscription();
        if (!wasSubscribed && _subscribedHistory is not null)
        {
            _refreshRequested = true;
            InvalidateVisual();
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

    private void DrawSeries(DrawingContext context, TemperatureSeries series, bool compact)
    {
        int index = (int)series;
        if (_series[index] is { } geometry && _seriesPens[index] is { } pen)
        {
            if (compact)
            {
                // A line (a target in particular) never strays into the neighbouring band.
                using (context.PushClip(_bandClips[index]))
                {
                    context.DrawGeometry(null, pen, geometry);
                }
            }
            else
            {
                context.DrawGeometry(null, pen, geometry);
            }
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
        double thickness = LineThickness;
        double targetOpacity = TargetOpacity;
        _seriesPens[(int)TemperatureSeries.NozzleActual] = SeriesPen(NozzleBrush, thickness, null);
        _seriesPens[(int)TemperatureSeries.NozzleTarget] = SeriesPen(Faded(NozzleBrush, targetOpacity), TargetThickness, TargetDash);
        _seriesPens[(int)TemperatureSeries.BedActual] = SeriesPen(BedBrush, thickness, null);
        _seriesPens[(int)TemperatureSeries.BedTarget] = SeriesPen(Faded(BedBrush, targetOpacity), TargetThickness, TargetDash);
    }

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
        ClearRangeLabels();
    }

    private void ClearRangeLabels() => Array.Clear(_rangeLabels);

    /// <summary>
    /// Range labels of the compact bands (T-106): "212–218 °C" in whole degrees, right-aligned in the
    /// <see cref="BandRangeWidth"/> column and vertically centred on the band. A label is created only when the degrees
    /// it shows (or the format, brush, culture) change; otherwise it is only moved.
    /// </summary>
    private void RebuildBandRanges()
    {
        bool show = ShowsBandRanges && !_showNoData;
        for (int s = 0; s < TemperatureChartData.SeriesCount; s++)
        {
            TemperatureSeries series = (TemperatureSeries)s;
            if (!show || BandSeriesOf(series) != series || _bands[s] is not { } band)
            {
                _rangeLabels[s] = null;
                continue;
            }

            (long Low, long High) degrees = ((long)Math.Round(band.Low, MidpointRounding.AwayFromZero), (long)Math.Round(band.High, MidpointRounding.AwayFromZero));
            if (_rangeLabels[s] is null || _rangeLabelValues[s] != degrees)
            {
                string text = string.Format(CultureInfo.CurrentCulture, BandRangeFormat!, degrees.Low, degrees.High);
                _rangeLabels[s] = Label(text, BandRangeBrush, BandRangeFontSize);
                _rangeTexts[s] = text;
                _rangeLabelValues[s] = degrees;
            }

            FormattedText label = _rangeLabels[s]!;
            Rect slot = _bandClips[s];
            double y = Math.Clamp(slot.Center.Y - (label.Height / 2), 0, Math.Max(0, Bounds.Height - label.Height));
            _rangeLabelOrigins[s] = new Point(Math.Floor(Bounds.Width - label.WidthIncludingTrailingWhitespace), Math.Round(y));
        }
    }

    /// <summary>
    /// Bands of the compact chart (T-106): one per actual series with values in the range, nozzle on top, each scaled to
    /// its own values (<see cref="CompactBands"/>) and inset by half the line thickness; a target shares the band of its
    /// series. Returns <see langword="false"/> when no series has a value (nothing to draw). Allocation-free.
    /// </summary>
    private bool LayOutCompactBands(Rect plot)
    {
        Array.Clear(_bands);
        (float nozzleMin, float nozzleMax) = _data.GetRange(TemperatureSeries.NozzleActual);
        (float bedMin, float bedMax) = _data.GetRange(TemperatureSeries.BedActual);
        bool hasNozzle = CompactBands.TryGetRange(nozzleMin, nozzleMax, out _, out _);
        bool hasBed = CompactBands.TryGetRange(bedMin, bedMax, out _, out _);
        int count = (hasNozzle ? 1 : 0) + (hasBed ? 1 : 0);
        if (count == 0)
        {
            return false;
        }

        int slot = 0;
        double inset = LineThickness / 2;
        for (int s = 0; s < TemperatureChartData.SeriesCount; s++)
        {
            TemperatureSeries series = (TemperatureSeries)s;
            if (BandSeriesOf(series) != series)
            {
                continue;
            }

            (float min, float max) = _data.GetRange(series);
            (double slotTop, double slotHeight) = CompactBands.Slot(Math.Min(slot, count - 1), count, plot.Top, plot.Height);
            if (!CompactBands.TryCreate(min, max, slotTop, slotHeight, inset, out ValueBand band))
            {
                continue;
            }

            slot++;
            Rect clip = new(plot.Left, slotTop, plot.Width, slotHeight);
            for (int t = 0; t < TemperatureChartData.SeriesCount; t++)
            {
                if (BandSeriesOf((TemperatureSeries)t) == series)
                {
                    _bands[t] = band;
                    _bandClips[t] = clip;
                }
            }
        }

        return true;
    }

    /// <summary>Horizontal grid lines and °C labels; only when the value range or the size changed. None when compact.</summary>
    private void RebuildValueAxis(Rect plot, double top, double step, bool compact)
    {
        _valueLabelPlacements.Clear();
        if (compact)
        {
            _valueGrid = null;
            return;
        }

        ValueBand axis = FullAxis(plot, top);
        StreamGeometry grid = new();
        using (StreamGeometryContext lines = grid.Open())
        {
            for (int i = 0; i * step <= top + (step / 2); i++)
            {
                double value = i * step;
                double y = Math.Round(axis.ToY(value)) + 0.5;
                lines.BeginFigure(new Point(plot.Left, y), isFilled: false);
                lines.LineTo(new Point(plot.Right, y));
                lines.EndFigure(false);
                FormattedText label = _valueLabels.Get(value);
                _valueLabelPlacements.Add((label, new Point(plot.Left - LabelGap - label.Width, y - (label.Height / 2))));
            }
        }

        _valueGrid = grid;
    }

    /// <summary>
    /// Tick positions and labels at round local times (DST-aware); a label is created only for a new tick. None when
    /// compact.
    /// </summary>
    private void RebuildTimeAxis(Rect plot, bool compact)
    {
        _tickXs.Clear();
        _timeLabelPlacements.Clear();
        if (compact)
        {
            return;
        }

        TimeSpan window = TimeWindow;
        DateTimeOffset from = _to - window;
        TimeSpan step = ChartScale.TimeStep(window, Math.Max(2, (int)(plot.Width / 90)));
        ChartScale.TimeTicks(from, _to, step, _labelZone, _ticks);
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
    private void RebuildSeries(Rect plot, double top, bool hasData, bool compact)
    {
        SeriesRebuilds++;
        if (compact)
        {
            hasData = hasData && LayOutCompactBands(plot);
        }
        else
        {
            ValueBand axis = FullAxis(plot, top);
            Array.Fill(_bands, axis);
            Array.Fill(_bandClips, plot);
        }

        for (int s = 0; s < TemperatureChartData.SeriesCount; s++)
        {
            ReadOnlySpan<ChartColumn> columns = _data.GetColumns((TemperatureSeries)s);
            _series[s] = hasData && !columns.IsEmpty && _bands[s] is { } band ? BuildSeries(columns, plot.Left, band) : null;
        }

        _showNoData = !hasData;
        if (_showNoData && _noDataLabel is null && !string.IsNullOrEmpty(NoDataText))
        {
            _noDataLabel = Label(NoDataText, AxisBrush, IsCompact ? CompactNoDataFontSize : LabelFontSize);
        }
    }
}
