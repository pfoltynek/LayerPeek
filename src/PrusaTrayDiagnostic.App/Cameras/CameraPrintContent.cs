using System.Globalization;

using PrusaTrayDiagnostic.App.Resources;
using PrusaTrayDiagnostic.App.Tray;
using PrusaTrayDiagnostic.Core.Cameras;
using PrusaTrayDiagnostic.Core.Printers;

namespace PrusaTrayDiagnostic.App.Cameras;

/// <summary>One line of the print overlay of the camera window (T-036).</summary>
/// <param name="Item">The item the line shows; the state line also carries the progress when both are selected.</param>
/// <param name="IsEmphasized">Drawn semi-bold: the printer name and the state line.</param>
public sealed record CameraOverlayLine(CameraOverlayItems Item, string Text, bool IsEmphasized);

/// <summary>A temperature as the overlay shows it: whole degrees; <see cref="Target"/> only while the heater is on.</summary>
internal readonly record struct OverlayTemperature(long Actual, long? Target);

/// <summary>
/// What the print overlay of the camera window shows (T-036): only the selected items, already rounded like the display,
/// so that polls that change nothing visible are dropped before any text is formatted (cheap to compare).
/// </summary>
/// <remarks>
/// Visibility rules (T-036 decision, same as the T-014 overlay and the tray tooltip for the job data):
/// <list type="bullet">
/// <item>Printer name: always (it comes from the settings).</item>
/// <item>State: once the printer was polled.</item>
/// <item>Temperatures: whenever the snapshot has them (finite values; none while the printer is offline).</item>
/// <item>Job items (progress, remaining time, finish time, print time, file name): only while the printer is reachable
/// and has a running, paused or waiting job (<see cref="PrinterStateExtensions.HasActiveJob"/> or
/// <see cref="PrinterState.Attention"/>), each when its value is known.</item>
/// </list>
/// </remarks>
internal readonly record struct CameraPrintContent(
    string? Name,
    PrinterState? State,
    int? ProgressPercent,
    long? RemainingMinutes,
    DateTime? FinishTime,
    bool FinishesOnAnotherDay,
    long? PrintTimeMinutes,
    string? FileName,
    OverlayTemperature? Nozzle,
    OverlayTemperature? Bed)
{
    /// <summary>Longest printer name shown; longer ones end with "…".</summary>
    public const int MaxNameLength = 40;

    /// <summary>Longest file name shown; longer ones keep their start and end (extension) around "…".</summary>
    public const int MaxFileNameLength = 40;

    /// <summary>Characters kept at the end of a shortened file name (e.g. "_PLA_2h10m.bgcode").</summary>
    public const int FileNameTailLength = 16;

    private const string Ellipsis = "…";

    /// <summary>Nothing is shown.</summary>
    public bool IsEmpty => this == default;

    /// <summary>The T-014 overlay: state, progress and remaining time.</summary>
    public static CameraPrintContent From(PrinterSnapshot? snapshot) =>
        From(snapshot, CameraOverlayOptions.DefaultItems, printerName: null, TimeZoneInfo.Local);

    /// <param name="snapshot">The latest snapshot of the printer; <see langword="null"/> before the first poll.</param>
    /// <param name="items">The selected items of the current window mode.</param>
    /// <param name="printerName">The name from the settings (shown when <see cref="CameraOverlayItems.PrinterName"/> is selected).</param>
    /// <param name="zone">Time zone of the finish time (the local one).</param>
    public static CameraPrintContent From(PrinterSnapshot? snapshot, CameraOverlayItems items, string? printerName, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        string? name = Has(items, CameraOverlayItems.PrinterName) && !string.IsNullOrWhiteSpace(printerName)
            ? TrayText.Ellipsize(printerName, MaxNameLength)
            : null;
        if (snapshot is null)
        {
            return new CameraPrintContent { Name = name };
        }

        CameraPrintContent content = new()
        {
            Name = name,
            State = Has(items, CameraOverlayItems.State) ? snapshot.State : null,
            Nozzle = Has(items, CameraOverlayItems.NozzleTemperature) ? Round(snapshot.Telemetry?.Nozzle) : null,
            Bed = Has(items, CameraOverlayItems.BedTemperature) ? Round(snapshot.Telemetry?.Bed) : null,
        };

        if (snapshot.Job is not { } job
            || !snapshot.State.IsReachable()
            || !(snapshot.State.HasActiveJob() || snapshot.State == PrinterState.Attention))
        {
            return content;
        }

        DateTime? finish = null;
        bool otherDay = false;
        if (Has(items, CameraOverlayItems.FinishTime) && snapshot.EstimatedCompletion is { } completion)
        {
            // Rounded up to the minute like the remaining time, so that both lines agree ("5 min" → "14:35").
            DateTimeOffset local = TimeZoneInfo.ConvertTime(CeilingToMinute(completion), zone);
            finish = local.DateTime;
            otherDay = local.Date != TimeZoneInfo.ConvertTime(snapshot.Timestamp, zone).Date;
        }

        return content with
        {
            ProgressPercent = Has(items, CameraOverlayItems.Progress) ? ToolTipContent.WholePercent(job.ProgressPercent) : null,
            RemainingMinutes = Has(items, CameraOverlayItems.RemainingTime) && job.EstimatedRemaining is { } remaining
                ? ToolTipContent.CeilingMinutes(remaining)
                : null,
            FinishTime = finish,
            FinishesOnAnotherDay = otherDay,
            PrintTimeMinutes = Has(items, CameraOverlayItems.PrintTime) && job.Elapsed is { } elapsed ? ToolTipContent.CeilingMinutes(elapsed) : null,
            FileName = Has(items, CameraOverlayItems.FileName) && job.FileName is { } fileName ? ShortenFileName(fileName) : null,
        };
    }

    /// <summary>The lines in display order (printer data, then job data), in the current language and culture.</summary>
    public IReadOnlyList<CameraOverlayLine> Lines()
    {
        if (IsEmpty)
        {
            return [];
        }

        List<CameraOverlayLine> lines = new(9);
        if (Name is { } name)
        {
            lines.Add(new(CameraOverlayItems.PrinterName, name, IsEmphasized: true));
        }

        if (StateText() is { } state)
        {
            lines.Add(new(CameraOverlayItems.State, state, IsEmphasized: true));
        }

        if (Nozzle is { } nozzle)
        {
            lines.Add(new(CameraOverlayItems.NozzleTemperature, Labeled(Strings.DiagnosticsNozzle, TemperatureText(nozzle)), IsEmphasized: false));
        }

        if (Bed is { } bed)
        {
            lines.Add(new(CameraOverlayItems.BedTemperature, Labeled(Strings.DiagnosticsBed, TemperatureText(bed)), IsEmphasized: false));
        }

        if (State is null && ProgressPercent is { } progress)
        {
            // Without the state line the progress gets a line of its own, emphasized like the state line it replaces.
            lines.Add(new(CameraOverlayItems.Progress, Format(Strings.ToolTipProgress, progress), IsEmphasized: true));
        }

        if (RemainingText() is { } remaining)
        {
            lines.Add(new(CameraOverlayItems.RemainingTime, remaining, IsEmphasized: false));
        }

        if (FinishTime is { } finish)
        {
            string time = finish.ToString("t", CultureInfo.CurrentCulture);
            string text = FinishesOnAnotherDay
                ? Format(Strings.CameraOverlayFinishDateTime, finish.ToString(CultureInfo.CurrentCulture.DateTimeFormat.MonthDayPattern, CultureInfo.CurrentCulture), time)
                : Format(Strings.CameraOverlayFinishTime, time);
            lines.Add(new(CameraOverlayItems.FinishTime, text, IsEmphasized: false));
        }

        if (PrintTimeMinutes is { } printTime)
        {
            lines.Add(new(CameraOverlayItems.PrintTime, Format(Strings.CameraOverlayPrintTime, Duration(printTime)), IsEmphasized: false));
        }

        if (FileName is { } fileName)
        {
            lines.Add(new(CameraOverlayItems.FileName, fileName, IsEmphasized: false));
        }

        return lines;
    }

    /// <summary>"Printing – 42%" (state and progress), "Printing" or <see langword="null"/> when the state is not shown.</summary>
    public string? StateText()
    {
        if (State is not { } state)
        {
            return null;
        }

        string text = TrayText.StateName(state);
        return ProgressPercent is { } progress
            ? Format(Strings.ToolTipStateWithProgress, text, Format(Strings.ToolTipProgress, progress))
            : text;
    }

    /// <summary>"1 h 5 min remaining"; <see langword="null"/> when not shown.</summary>
    public string? RemainingText() =>
        RemainingMinutes is { } minutes
            ? Format(Strings.ToolTipRemaining, Duration(minutes))
            : null;

    /// <summary>
    /// A file name of at most <see cref="MaxFileNameLength"/> characters: longer ones keep the start and the last
    /// <see cref="FileNameTailLength"/> characters (slicers put material, time and the extension there) around "…".
    /// </summary>
    internal static string ShortenFileName(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        if (fileName.Length <= MaxFileNameLength)
        {
            return fileName;
        }

        int head = MaxFileNameLength - FileNameTailLength - Ellipsis.Length;
        int tailStart = fileName.Length - FileNameTailLength;
        // Never split a surrogate pair.
        if (char.IsHighSurrogate(fileName[head - 1]))
        {
            head--;
        }

        if (char.IsLowSurrogate(fileName[tailStart]))
        {
            tailStart++;
        }

        return string.Concat(fileName.AsSpan(0, head), Ellipsis, fileName.AsSpan(tailStart));
    }

    private static bool Has(CameraOverlayItems items, CameraOverlayItems item) => (items & item) != 0;

    /// <summary>"1 h 5 min"; clamped so that an absurd estimate cannot overflow <see cref="TimeSpan"/>.</summary>
    private static string Duration(long minutes) =>
        TrayToolTipFormatter.FormatDuration(TimeSpan.FromTicks(minutes < TimeSpan.MaxValue.Ticks / TimeSpan.TicksPerMinute ? minutes * TimeSpan.TicksPerMinute : TimeSpan.MaxValue.Ticks));

    private static OverlayTemperature? Round(Temperature? temperature)
    {
        if (temperature is not { } value || !double.IsFinite(value.Actual))
        {
            return null;
        }

        long? target = value.Target is { } t && double.IsFinite(t) ? WholeDegrees(t) : null;
        // A target of 0 means the heater is off: only the measured temperature is shown.
        return new OverlayTemperature(WholeDegrees(value.Actual), target is > 0 ? target : null);
    }

    private static long WholeDegrees(double celsius) => (long)Math.Round(Math.Clamp(celsius, -1e6, 1e6), MidpointRounding.AwayFromZero);

    private static DateTimeOffset CeilingToMinute(DateTimeOffset value)
    {
        long rest = value.Ticks % TimeSpan.TicksPerMinute;
        long missing = rest == 0 ? 0 : TimeSpan.TicksPerMinute - rest;
        return missing <= DateTime.MaxValue.Ticks - Math.Max(value.Ticks, value.UtcTicks) ? value.AddTicks(missing) : value;
    }

    /// <summary>"215 / 215 °C" while heating, "25 °C" with the heater off.</summary>
    private static string TemperatureText(OverlayTemperature temperature)
    {
        string actual = temperature.Actual.ToString("N0", CultureInfo.CurrentCulture);
        return temperature.Target is { } target
            ? Format(Strings.CameraOverlayTemperatureWithTarget, actual, target.ToString("N0", CultureInfo.CurrentCulture))
            : Format(Strings.DiagnosticsTemperatureValue, actual);
    }

    private static string Labeled(string label, string value) => Format(Strings.CameraOverlayLabeledValue, label, value);

    private static string Format(string format, object? arg0) => string.Format(CultureInfo.CurrentCulture, format, arg0);

    private static string Format(string format, object? arg0, object? arg1) => string.Format(CultureInfo.CurrentCulture, format, arg0, arg1);
}
