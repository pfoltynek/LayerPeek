using System.ComponentModel;

using LayerPeek.App.Diagnostics.Chart;
using LayerPeek.App.Resources;
using LayerPeek.Core.History;
using LayerPeek.Core.Printers;

namespace LayerPeek.App.Cameras.Overlay.Widgets;

/// <summary>
/// What the Temperatures widget shows (T-084): nozzle and bed in whole degrees with the target only while heating (rules
/// of T-036, <see cref="OverlayValues.Temperature"/>); each <see langword="null"/> when unknown. Both <see langword="null"/>
/// (<c>default</c>) = nothing to show.
/// </summary>
public readonly record struct TemperaturesContent(OverlayTemperature? Nozzle, OverlayTemperature? Bed)
{
    public bool HasAny => Nozzle is not null || Bed is not null;
}

/// <summary>
/// The Temperatures widget of the camera overlay (T-084, design T-076 2.3): nozzle and bed "215 / 215 °C" (actual /
/// target while heating, otherwise "28 °C") with icons in the colours of the chart lines, and below them the compact
/// <see cref="TemperatureChart"/> of the last <see cref="TemperatureChart.CompactTimeWindow"/>. The key value is the
/// nozzle's actual temperature (only that number is emphasized).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>S: one row with both values, no chart.</item>
/// <item>M: the row, the chart 256 × 64 and the axis labels "−60 min" / "now".</item>
/// <item>L: one row per heater with its word ("Nozzle", "Bed"), the chart 360 × 110 and the axis labels.</item>
/// <item>A temperature that is not known is left out (its pair is not drawn); without any the card hides (offline,
/// before the first poll). Without history the chart says "No data".</item>
/// </list>
/// The values change only with a whole degree, so most polls post nothing. The chart is not driven by the snapshots: it
/// reads <see cref="History"/> itself, listens to it only while it is shown and redraws only when the printer's history
/// changed.
/// </remarks>
public sealed class TemperaturesWidgetViewModel : OverlayWidgetViewModel<TemperaturesContent>
{
    /// <summary>The card widths (T-076 2.3): S by content, M 284 (chart 256 + 2 × 14), L 392 (chart 360 + 2 × 16).</summary>
    public static readonly OverlayWidgetMinWidths MinWidths = new(0, 284, 392);

    private static readonly PropertyChangedEventArgs ShowsChartChanged = new(nameof(ShowsChart));

    private string _nozzleActual = string.Empty;
    private string _nozzleRest = string.Empty;
    private string _bed = string.Empty;
    private string _nozzleLabel = string.Empty;
    private string _bedLabel = string.Empty;
    private string _chartStart = string.Empty;
    private string _chartNow = string.Empty;
    private string _chartNoData = string.Empty;
    private string _chartRange = string.Empty;
    private bool _hasNozzle;
    private bool _hasBed;

    public TemperaturesWidgetViewModel(OverlayWidgetContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        PrinterId = context.PrinterId;
        Clock = context.Time;
        History = context.History;
    }

    /// <summary>The history the chart draws; <see langword="null"/> = none (the chart says "No data").</summary>
    public TelemetryHistory? History { get; }

    /// <summary>The printer of the window (the chart's series).</summary>
    public string PrinterId { get; }

    /// <summary>The clock of the window: "now" of the chart.</summary>
    public TimeProvider Clock { get; }

    /// <summary>The nozzle's actual temperature, "215" (the key value).</summary>
    public string NozzleActual
    {
        get => _nozzleActual;
        private set => SetProperty(ref _nozzleActual, value);
    }

    /// <summary>The rest of the nozzle text after the actual value: " / 215 °C" while heating, otherwise " °C".</summary>
    public string NozzleRest
    {
        get => _nozzleRest;
        private set => SetProperty(ref _nozzleRest, value);
    }

    /// <summary>"60 / 60 °C" while heating, otherwise "24 °C".</summary>
    public string Bed
    {
        get => _bed;
        private set => SetProperty(ref _bed, value);
    }

    /// <summary>"Nozzle" (L only).</summary>
    public string NozzleLabel
    {
        get => _nozzleLabel;
        private set => SetProperty(ref _nozzleLabel, value);
    }

    /// <summary>"Bed" (L only).</summary>
    public string BedLabel
    {
        get => _bedLabel;
        private set => SetProperty(ref _bedLabel, value);
    }

    /// <summary>"−60 min" below the left edge of the chart.</summary>
    public string ChartStart
    {
        get => _chartStart;
        private set => SetProperty(ref _chartStart, value);
    }

    /// <summary>"now" below the right edge of the chart.</summary>
    public string ChartNow
    {
        get => _chartNow;
        private set => SetProperty(ref _chartNow, value);
    }

    /// <summary>"No data" in the chart when the hour has no temperature.</summary>
    public string ChartNoData
    {
        get => _chartNoData;
        private set => SetProperty(ref _chartNoData, value);
    }

    /// <summary>Format of the value range of a chart band, "{0}–{1} °C" (drawn only in L, T-106).</summary>
    public string ChartRange
    {
        get => _chartRange;
        private set => SetProperty(ref _chartRange, value);
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

    /// <summary>The chart and its axis labels are drawn (M / L).</summary>
    public bool ShowsChart => !IsSmall;

    /// <summary>The content of a snapshot: its temperatures, whenever it reports them (none while offline).</summary>
    public static TemperaturesContent ContentOf(in OverlayWidgetInput input)
    {
        Telemetry? telemetry = input.Snapshot?.Telemetry;
        return telemetry is null
            ? default
            : new TemperaturesContent(OverlayValues.Temperature(telemetry.Nozzle), OverlayValues.Temperature(telemetry.Bed));
    }

    protected override TemperaturesContent ComputeContent(in OverlayWidgetInput input) => ContentOf(input);

    protected override bool HasData(in TemperaturesContent content) => content.HasAny;

    protected override void Format(in TemperaturesContent content)
    {
        if (content.Nozzle is { } nozzle)
        {
            (string actual, string rest) = OverlayFormat.TemperatureParts(nozzle);
            NozzleActual = actual;
            NozzleRest = rest;
        }

        Bed = content.Bed is { } bed ? OverlayFormat.Temperature(bed) : string.Empty;
        HasNozzle = content.Nozzle is not null;
        HasBed = content.Bed is not null;
        NozzleLabel = Strings.DiagnosticsNozzle;
        BedLabel = Strings.DiagnosticsBed;
        ChartStart = Strings.CameraOverlayChartStart;
        ChartNow = Strings.CameraOverlayChartNow;
        ChartNoData = Strings.CameraOverlayChartNoData;
        ChartRange = Strings.CameraOverlayChartRange;
        AccessibleName = AccessibleText(content, ShowsChart);
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName != nameof(RenderedSize))
        {
            return;
        }

        OnPropertyChanged(ShowsChartChanged);
        // The screen reader hears about the chart only when it is drawn; the values are not formatted again.
        if (HasContent)
        {
            AccessibleName = AccessibleText(ShownContent, ShowsChart);
        }
    }

    /// <summary>"Nozzle 215 / 215 °C, Bed 60 / 60 °C, chart of the last 60 minutes": the words the icons stand for.</summary>
    private static string AccessibleText(in TemperaturesContent content, bool withChart)
    {
        string? nozzle = content.Nozzle is { } n ? Strings.DiagnosticsNozzle + " " + OverlayFormat.Temperature(n) : null;
        string? bed = content.Bed is { } b ? Strings.DiagnosticsBed + " " + OverlayFormat.Temperature(b) : null;
        string? chart = withChart ? Strings.CameraOverlayChartName : null;
        return string.Join(", ", new[] { nozzle, bed, chart }.Where(static part => part is not null));
    }
}
