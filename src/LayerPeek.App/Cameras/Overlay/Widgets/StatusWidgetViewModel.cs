using Avalonia.Media;

using LayerPeek.Core.Printers;

namespace LayerPeek.App.Cameras.Overlay.Widgets;

/// <summary>
/// What the Status widget shows (T-082), rounded like the display: two polls that look the same are equal.
/// </summary>
/// <param name="Name">The printer name shortened to <see cref="OverlayFormat.MaxNameLength"/> characters.</param>
/// <param name="State">The printer state; <see langword="null"/> before the first poll (connecting).</param>
/// <param name="Percent">Whole percent of the active job (T-036 rules); <see langword="null"/> without one.</param>
public readonly record struct StatusWidgetContent(string Name, PrinterState? State, int? Percent);

/// <summary>
/// The Status widget (T-082, design T-076 2.1): the printer name, the state pill with a dot in the tray colour, the large
/// progress percent (the key value, accent when emphasized) and the progress bar. Without an active job only the name and
/// the pill. Never hides: offline, rejected credentials or an error are exactly what it must show.
/// </summary>
public sealed class StatusWidgetViewModel : OverlayWidgetViewModel<StatusWidgetContent>
{
    /// <summary>Card widths (T-076 2.1): S has only a maximum, M at least 180, L at least 220.</summary>
    public static readonly OverlayWidgetMinWidths MinWidths = new(0, 180, 220);

    // Under the overlay's lock: the shortened name is reused while the configured name is unchanged.
    private readonly OverlayTextCache _names = new();

    private string _printerName = string.Empty;
    private string _stateText = string.Empty;
    private IBrush _dotBrush = OverlayStateBrushes.NoData;
    private string _percentText = string.Empty;
    private double _progress;
    private bool _hasProgress;

    /// <summary>The printer name (at most 40 characters, then shortened in the middle by width); not drawn in size S.</summary>
    public string PrinterName
    {
        get => _printerName;
        private set => SetProperty(ref _printerName, value);
    }

    /// <summary>"Printing", "Connecting…" (the tray's state names).</summary>
    public string StateText
    {
        get => _stateText;
        private set => SetProperty(ref _stateText, value);
    }

    /// <summary>The state dot (<see cref="OverlayStateBrushes"/>).</summary>
    public IBrush DotBrush
    {
        get => _dotBrush;
        private set => SetProperty(ref _dotBrush, value);
    }

    /// <summary>"42%" / "42 %" (by language); empty without an active job.</summary>
    public string PercentText
    {
        get => _percentText;
        private set => SetProperty(ref _percentText, value);
    }

    /// <summary>0–100, the shown whole percent (the bar agrees with the number).</summary>
    public double Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

    /// <summary>An active job: the percent and the bar are drawn.</summary>
    public bool HasProgress
    {
        get => _hasProgress;
        private set => SetProperty(ref _hasProgress, value);
    }

    protected override StatusWidgetContent ComputeContent(in OverlayWidgetInput input) => new(
        _names.Name(input.Printer.Name),
        input.Snapshot?.State,
        OverlayValues.ActiveJob(input.Snapshot) is { } job ? OverlayValues.ProgressPercent(job) : null);

    // The name and the pill are always there (the card never hides).
    protected override bool HasData(in StatusWidgetContent content) => content.Name is not null;

    protected override void Format(in StatusWidgetContent content)
    {
        string state = OverlayFormat.StateName(content.State);
        string? percent = content.Percent is { } value ? OverlayFormat.Percent(value) : null;

        PrinterName = content.Name;
        StateText = state;
        DotBrush = OverlayStateBrushes.For(content.State);
        PercentText = percent ?? string.Empty;
        Progress = content.Percent ?? 0;
        HasProgress = percent is not null;

        // "MK4: Printing, 42%" (T-076 2.1); without a job "MK4: Ready".
        AccessibleName = percent is null
            ? string.Concat(content.Name, ": ", state)
            : string.Concat(content.Name, ": ", state, ", ", percent);
    }
}
