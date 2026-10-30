using System.ComponentModel;
using System.Globalization;

using LayerPeek.App.Resources;
using LayerPeek.App.Tray;
using LayerPeek.Core.Cooldown;
using LayerPeek.Core.Printers;

namespace LayerPeek.App.Cameras.Overlay.Widgets;

/// <summary>
/// What the Cool-down widget shows (T-088), already rounded like the display: two polls that look the same are equal, so
/// nothing is posted for them. <c>default</c> (<see cref="CooldownPhase.None"/>) = nothing to show.
/// </summary>
/// <param name="Phase">Cooling or cooled down.</param>
/// <param name="NozzleCelsius">Whole degrees of the nozzle.</param>
/// <param name="BedCelsius">Whole degrees of the bed; <see langword="null"/> when the printer reports none.</param>
/// <param name="NozzleThresholdCelsius">The printer's nozzle threshold (T-079, T-080, T-109).</param>
/// <param name="BedThresholdCelsius">The printer's bed threshold (T-109).</param>
/// <param name="LimitingCelsius">
/// Whole degrees of the temperature that holds the cool-down up (<see cref="CooldownState.Limiting"/>, T-109): the key value
/// of the small card.
/// </param>
/// <param name="Limiting">
/// Whose temperature <paramref name="LimitingCelsius"/> is: the bed only when it holds the cool-down up and the printer
/// reports it, otherwise the nozzle (the sensor icon in front of the key value of the small card, ui-designer review
/// 2026-10-30).
/// </param>
/// <param name="ProgressPercent">0–100 towards the threshold, rounded down (100 once cooled down).</param>
/// <param name="EstimateMinutes">Minutes until the threshold, rounded up; <see langword="null"/> without a fresh estimate.</param>
/// <param name="Outcome">Whether the print finished or was stopped (cooled down only).</param>
/// <param name="PrintEndedLocal">When the print ended, local time to the minute (cooled down only); <see langword="null"/> when not seen.</param>
/// <param name="CooledInMinutes">How long the cool-down took, rounded up (cooled down only); <see langword="null"/> when unknown or nothing cooled down.</param>
public readonly record struct CooldownContent(
    CooldownPhase Phase,
    long NozzleCelsius,
    long? BedCelsius,
    int NozzleThresholdCelsius,
    int BedThresholdCelsius,
    long LimitingCelsius,
    CooldownSensor Limiting,
    int ProgressPercent,
    long? EstimateMinutes,
    CooldownPrintOutcome Outcome,
    DateTime? PrintEndedLocal,
    long? CooledInMinutes)
{
    public bool IsCooling => Phase == CooldownPhase.Cooling;

    public bool IsCooled => Phase == CooldownPhase.CooledDown;
}

/// <summary>
/// The Cool-down widget of the camera overlay (T-088, design T-076 2.5): after a finished or stopped print (not after an
/// error) "Cooling down…" with the nozzle and bed temperatures, a blue bar towards the printer's thresholds and the
/// estimate "safe to turn off in about 6 min (nozzle below 40 °C, bed below 40 °C)"; then "Cooled down – safe to turn off"
/// with a green outline, the temperatures and "print finished at 7:52 PM · cooled down in 14 min". The nozzle and the bed
/// decide together; the bar and the estimate follow the slower of the two (T-109).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>The state comes from <see cref="CooldownMonitor"/> (T-079), which also ends it at the next print, after a minute
/// offline and when the nozzle heats again; the card hides at once for a snapshot that is unreachable or has an active
/// job, so that it never stands next to "Offline" or a new print. A printer that showed an ended print already at the app
/// start has no cool-down (user decision 11), so no card.</item>
/// <item>The key value is the nozzle temperature while cooling and the state text once cooled down (T-076 Q8).</item>
/// <item>S: one row "❄ 87 °C · about 6 min" with a bar below, or "⏻ Cooled down · 48 °C", with the limiting temperature
/// (T-109: the bed's when it holds the cool-down up, like the mini preview strip); while cooling the icon of its sensor
/// (nozzle cone / bed line) stands in front of it. M / L: title, nozzle and bed, bar
/// (cooling), detail with both thresholds (at most two lines).</item>
/// <item>The estimate is shown only when the monitor has one, it is still running (counted down by the monitor from its
/// reading, never "0 min") and the state is not older than <see cref="StaleAfter"/> (monitoring stopped): otherwise the
/// detail names only the threshold. While an estimate is shown a clock ticks every <see cref="TickInterval"/>, so that it
/// counts down and goes away even without new snapshots.</item>
/// <item>Without an observed end of the print (user decision 10, <see cref="CooldownState.PrintEndedAt"/> unknown) the
/// cooled card has no times row; a print that ended below the threshold shows only when it ended.</item>
/// </list>
/// It listens to the monitor's changes of its printer only while the window has it (from its creation to its disposal).
/// </remarks>
public sealed class CooldownWidgetViewModel : OverlayWidgetViewModel<CooldownContent>
{
    /// <summary>Narrowest card per size (T-076 2.5: M min 240, L min 280; S has no minimum).</summary>
    public static readonly OverlayWidgetMinWidths MinWidths = new(0, 240, 280);

    /// <summary>The age of a state beyond which its estimate is not shown (shared with the mini preview strip: <see cref="CooldownEstimate.StaleAfter"/>).</summary>
    public static readonly TimeSpan StaleAfter = CooldownEstimate.StaleAfter;

    /// <summary>How often the shown estimate is computed again without snapshots (<see cref="CooldownEstimate.TickInterval"/>).</summary>
    public static readonly TimeSpan TickInterval = CooldownEstimate.TickInterval;

    private static readonly PropertyChangedEventArgs[] PartsChanged =
    [
        new(nameof(HeaderText)), new(nameof(ShowsHeader)), new(nameof(EmphasizesHeader)), new(nameof(NozzleText)), new(nameof(ShowsNozzle)),
        new(nameof(EmphasizesNozzle)), new(nameof(ShowsNozzleIcon)), new(nameof(ShowsSmallBedIcon)), new(nameof(ShowsBed)),
        new(nameof(DetailText)), new(nameof(ShowsDetail)),
    ];

    private readonly CooldownMonitor? _cooldown;
    private readonly CooldownCountdown? _countdown;
    private readonly EventHandler<string>? _onChanged;

    // UI thread: the texts of the shown content.
    private string _title = string.Empty;
    private string _smallCooled = string.Empty;
    private string _nozzleText = string.Empty;
    private string _limitingText = string.Empty;
    private string _bedText = string.Empty;
    private string _detail = string.Empty;
    private string _smallDetail = string.Empty;
    private double _progress;

    /// <param name="context">The window's services; without its <see cref="OverlayWidgetContext.Cooldown"/> the card never shows.</param>
    public CooldownWidgetViewModel(OverlayWidgetContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        PrinterId = context.PrinterId;
        _cooldown = context.Cooldown;
        if (_cooldown is not null)
        {
            _countdown = new CooldownCountdown(context.Time, RequestUpdate);
            _onChanged = OnCooldownChanged;
            _cooldown.Changed += _onChanged;
            IsListening = true;
        }
    }

    /// <summary>The printer of the window.</summary>
    public string PrinterId { get; }

    public bool IsCooling => ShownContent.IsCooling;

    public bool IsCooled => ShownContent.IsCooled;

    /// <summary>
    /// The first row: "Cooling down…" / "Cooled down – safe to turn off"; in S once cooled "Cooled down · 48 °C" (the only
    /// row; while cooling S starts with the nozzle).
    /// </summary>
    public string HeaderText => IsSmall && IsCooled ? _smallCooled : _title;

    public bool ShowsHeader => !(IsSmall && IsCooling);

    /// <summary>The state text is the key value once cooled down (T-076 2: accent colour when emphasized).</summary>
    public bool EmphasizesHeader => IsEmphasized && IsCooled;

    /// <summary>
    /// "87 °C": the nozzle temperature; in S the limiting temperature (T-109), which is the bed's while the bed holds the
    /// cool-down up.
    /// </summary>
    public string NozzleText => IsSmall ? _limitingText : _nozzleText;

    /// <summary>The nozzle row (in S cooled down the temperature is part of the header).</summary>
    public bool ShowsNozzle => !(IsSmall && IsCooled);

    /// <summary>The nozzle temperature is the key value while cooling.</summary>
    public bool EmphasizesNozzle => IsEmphasized && IsCooling;

    /// <summary>
    /// The nozzle cone in front of the temperature: always in M / L; in S only while the nozzle is the limiting temperature
    /// (T-109 Q3 B, ui-designer review 2026-10-30: the accent colour of the key value is close to the nozzle colour, so the
    /// icon, not the colour, says whose temperature it is).
    /// </summary>
    public bool ShowsNozzleIcon => !IsSmall || ShownContent.Limiting != CooldownSensor.Bed;

    /// <summary>S: the bed line in front of the limiting temperature while the bed holds the cool-down up.</summary>
    public bool ShowsSmallBedIcon => IsSmall && ShownContent.Limiting == CooldownSensor.Bed;

    /// <summary>"45 °C": the bed temperature (it decides together with the nozzle, T-109).</summary>
    public string BedText
    {
        get => _bedText;
        private set => SetProperty(ref _bedText, value);
    }

    /// <summary>M / L with a known bed temperature.</summary>
    public bool ShowsBed => !IsSmall && ShownContent.BedCelsius is not null;

    /// <summary>0–100 towards the threshold (the bar is drawn while cooling).</summary>
    public double Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

    /// <summary>The bar towards the threshold, only while cooling.</summary>
    public bool ShowsProgress => IsCooling;

    /// <summary>
    /// M / L: the estimate or the threshold (cooling), the times row (cooled down); S: " · about 6 min" after the nozzle
    /// while cooling with an estimate.
    /// </summary>
    public string DetailText => IsSmall ? _smallDetail : _detail;

    public bool ShowsDetail => DetailText.Length > 0;

    /// <summary>Listens to the monitor's changes (from the creation until the window closes or the widget leaves the layout).</summary>
    internal bool IsListening { get; private set; }

    /// <summary>The clock that counts the shown estimate down runs.</summary>
    internal bool IsTicking => _countdown?.IsTicking ?? false;

    /// <summary>How many times the content was computed (tests: per snapshot, per change of the monitor and per tick).</summary>
    internal int ComputeCount { get; private set; }

    /// <summary>How many times texts were formatted (tests: only for visible changes and language switches).</summary>
    internal int FormatCount { get; private set; }

    internal long? ShownEstimateMinutes => ShownContent.EstimateMinutes;

    internal long ShownNozzleCelsius => ShownContent.NozzleCelsius;

    internal long ShownLimitingCelsius => ShownContent.LimitingCelsius;

    /// <summary>
    /// The content for <paramref name="input"/> and the printer's cool-down <paramref name="state"/>: nothing without a
    /// cool-down or a nozzle temperature, for an unreachable snapshot (or before the first) and during a job (the next
    /// print ends the cool-down). Allocates nothing.
    /// </summary>
    public static CooldownContent ContentOf(in OverlayWidgetInput input, in CooldownState state)
    {
        if (state.Phase == CooldownPhase.None
            || input.Snapshot is not { } snapshot
            || !snapshot.State.IsReachable()
            || OverlayValues.ActiveJob(snapshot) is not null
            || OverlayValues.Temperature(new Temperature(state.NozzleCelsius ?? double.NaN, null)) is not { } nozzle)
        {
            return default;
        }

        long? bed = OverlayValues.Temperature(new Temperature(state.BedCelsius ?? double.NaN, null))?.Actual;
        CooldownSensor sensor = state.Limiting == CooldownSensor.Bed && bed is not null ? CooldownSensor.Bed : CooldownSensor.Nozzle;
        long limiting = sensor == CooldownSensor.Bed && bed is { } bedActual ? bedActual : nozzle.Actual;
        if (state.Phase == CooldownPhase.Cooling)
        {
            double progress = double.IsFinite(state.Progress) ? Math.Clamp(state.Progress, 0, 1) : 0;
            return new CooldownContent(
                CooldownPhase.Cooling,
                nozzle.Actual,
                bed,
                state.NozzleThresholdCelsius,
                state.BedThresholdCelsius,
                limiting,
                sensor,
                (int)Math.Floor(progress * 100),
                CooldownEstimate.Minutes(state, input.Now),
                default,
                null,
                null);
        }

        DateTime? ended = null;
        long? cooledIn = null;
        if (state.PrintEndedAt is { } endedAt)
        {
            DateTime local = TimeZoneInfo.ConvertTime(endedAt, input.TimeZone).DateTime;
            ended = new DateTime(local.Ticks - (local.Ticks % TimeSpan.TicksPerMinute), DateTimeKind.Unspecified);
            if (!state.EndedBelowThreshold && state.CooledDownAt is { } cooledAt)
            {
                cooledIn = ToolTipContent.CeilingMinutes(cooledAt - endedAt);
            }
        }

        return new CooldownContent(
            CooldownPhase.CooledDown,
            nozzle.Actual,
            bed,
            state.NozzleThresholdCelsius,
            state.BedThresholdCelsius,
            limiting,
            sensor,
            100,
            null,
            state.Outcome == CooldownPrintOutcome.Stopped ? CooldownPrintOutcome.Stopped : CooldownPrintOutcome.Finished,
            ended,
            cooledIn);
    }

    protected override CooldownContent ComputeContent(in OverlayWidgetInput input)
    {
        ComputeCount++;
        CooldownState state = _cooldown?.GetState(PrinterId) ?? CooldownState.None;
        CooldownContent content = ContentOf(input, state);
        _countdown?.Set(content.EstimateMinutes is not null);
        return content;
    }

    protected override bool HasData(in CooldownContent content) => content.Phase != CooldownPhase.None;

    protected override void Format(in CooldownContent content)
    {
        FormatCount++;
        _title = CooldownFormat.Title(content);
        _smallCooled = content.IsCooled ? CooldownFormat.SmallCooled(content) : string.Empty;
        _detail = CooldownFormat.Detail(content);
        _smallDetail = CooldownFormat.SmallDetail(content);
        _nozzleText = CooldownFormat.Temperature(content.NozzleCelsius);
        _limitingText = CooldownFormat.Temperature(content.LimitingCelsius);
        BedText = content.BedCelsius is { } bed ? CooldownFormat.Temperature(bed) : string.Empty;
        Progress = content.ProgressPercent;
        AccessibleName = CooldownFormat.AccessibleText(content);
        OnPropertyChanged(nameof(IsCooling));
        OnPropertyChanged(nameof(IsCooled));
        OnPropertyChanged(nameof(ShowsProgress));
        RaisePartsChanged();
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        switch (e.PropertyName)
        {
            case nameof(RenderedSize):
            case nameof(IsEmphasized):
                RaisePartsChanged();
                break;
            case nameof(HasContent):
            case nameof(IsCooled):
                // Only a shown "cooled down" card has the outline (also the placeholder of the layout mode has none).
                IsOutlined = HasContent && IsCooled;
                break;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_cooldown is not null && _onChanged is not null)
            {
                _cooldown.Changed -= _onChanged;
            }

            IsListening = false;
            _countdown?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void RaisePartsChanged()
    {
        foreach (PropertyChangedEventArgs args in PartsChanged)
        {
            OnPropertyChanged(args);
        }
    }

    // The monitor's thread (or whoever configures it): the state of a printer changed.
    private void OnCooldownChanged(object? sender, string printerId)
    {
        if (string.Equals(printerId, PrinterId, StringComparison.OrdinalIgnoreCase))
        {
            RequestUpdate();
        }
    }
}

/// <summary>
/// Texts of the Cool-down widget (T-088, design T-076 2.5 and 10): numbers and times in the region
/// (<c>CurrentCulture</c>), words in the app language. UI thread, only when the shown content changed or the language
/// switched.
/// </summary>
public static class CooldownFormat
{
    private const string Separator = " · ";

    /// <summary>"87 °C" (non-breaking space before "°C").</summary>
    public static string Temperature(long celsius) => OverlayFormat.Temperature(new OverlayTemperature(celsius, null));

    /// <summary>"Cooling down…" / "Cooled down – safe to turn off".</summary>
    public static string Title(in CooldownContent content) => content.IsCooled ? Strings.CameraOverlayCooled : Strings.CameraOverlayCooling;

    /// <summary>The S card once cooled down: "Cooled down · 48 °C" with the limiting temperature (T-109).</summary>
    public static string SmallCooled(in CooldownContent content) =>
        Format(Strings.CameraMiniCooled, content.LimitingCelsius.ToString("N0", CultureInfo.CurrentCulture));

    /// <summary>
    /// M / L: "safe to turn off in about 6 min (nozzle below 40 °C, bed below 40 °C)" or without an estimate "safe to turn
    /// off once the nozzle is below 40 °C and the bed below 40 °C" (cooling; both thresholds, T-109, the nozzle's alone for a
    /// printer that reports no bed); the times row (cooled down; empty without an observed end of the print).
    /// </summary>
    public static string Detail(in CooldownContent content)
    {
        if (content.IsCooled)
        {
            return Times(content) ?? string.Empty;
        }

        string nozzle = content.NozzleThresholdCelsius.ToString("N0", CultureInfo.CurrentCulture);
        if (content.BedCelsius is null)
        {
            return content.EstimateMinutes is { } nozzleMinutes
                ? Format(Strings.CameraOverlayCooldownEstimate, OverlayFormat.Duration(nozzleMinutes), nozzle)
                : Format(Strings.CameraOverlayCooldownNoEstimate, nozzle);
        }

        string bed = content.BedThresholdCelsius.ToString("N0", CultureInfo.CurrentCulture);
        return content.EstimateMinutes is { } minutes
            ? Format(Strings.CameraOverlayCooldownEstimateBed, OverlayFormat.Duration(minutes), nozzle, bed)
            : Format(Strings.CameraOverlayCooldownNoEstimateBed, nozzle, bed);
    }

    /// <summary>S while cooling with an estimate: " · about 6 min" after the nozzle; empty otherwise.</summary>
    public static string SmallDetail(in CooldownContent content) =>
        content.IsCooling && content.EstimateMinutes is { } minutes
            ? Separator + Format(Strings.CameraOverlayCooldownAbout, OverlayFormat.Duration(minutes))
            : string.Empty;

    /// <summary>
    /// "print finished at 7:52 PM · cooled down in 14 min" (or "print stopped at…"); only the end when nothing cooled down;
    /// <see langword="null"/> without an observed end of the print (user decision 10) or while cooling.
    /// </summary>
    public static string? Times(in CooldownContent content)
    {
        string? ended = Ended(content);
        if (ended is null)
        {
            return null;
        }

        return content.CooledInMinutes is { } minutes ? ended + Separator + CooledIn(minutes) : ended;
    }

    /// <summary>
    /// "Cooling down…, Nozzle 87 °C, Bed 45 °C, safe to turn off in about 6 min (nozzle below 40 °C, bed below 40 °C)" / "Cooled down – safe
    /// to turn off, Nozzle 48 °C, Bed 39 °C, print finished at 7:52 PM, cooled down in 14 min": the words the icons stand for.
    /// </summary>
    public static string AccessibleText(in CooldownContent content)
    {
        List<string> parts = [Title(content), Strings.DiagnosticsNozzle + " " + Temperature(content.NozzleCelsius)];
        if (content.BedCelsius is { } bed)
        {
            parts.Add(Strings.DiagnosticsBed + " " + Temperature(bed));
        }

        if (content.IsCooling)
        {
            parts.Add(Detail(content));
        }
        else if (Ended(content) is { } ended)
        {
            parts.Add(ended);
            if (content.CooledInMinutes is { } minutes)
            {
                parts.Add(CooledIn(minutes));
            }
        }

        return string.Join(", ", parts);
    }

    private static string? Ended(in CooldownContent content)
    {
        if (!content.IsCooled || content.PrintEndedLocal is not { } local)
        {
            return null;
        }

        string time = local.ToString("t", CultureInfo.CurrentCulture);
        return Format(content.Outcome == CooldownPrintOutcome.Stopped ? Strings.CameraOverlayCooldownStoppedAt : Strings.CameraOverlayCooldownFinishedAt, time);
    }

    private static string CooledIn(long minutes) => Format(Strings.CameraOverlayCooldownCooledIn, OverlayFormat.Duration(minutes));

    private static string Format(string format, object? arg0) => string.Format(CultureInfo.CurrentCulture, format, arg0);

    private static string Format(string format, object? arg0, object? arg1) => string.Format(CultureInfo.CurrentCulture, format, arg0, arg1);

    private static string Format(string format, object? arg0, object? arg1, object? arg2) =>
        string.Format(CultureInfo.CurrentCulture, format, arg0, arg1, arg2);
}
