using System.ComponentModel;

using LayerPeek.App.Resources;
using LayerPeek.Core.Printers;

namespace LayerPeek.App.Cameras.Overlay.Widgets;

/// <summary>
/// What the Times widget shows (T-083), already rounded like the display: remaining minutes (rounded up), the local finish
/// time (rounded up to the minute, with "another day") and the print time so far; each <see langword="null"/> when unknown.
/// All <see langword="null"/> (<c>default</c>) = no job, nothing to show.
/// </summary>
public readonly record struct TimesContent(long? RemainingMinutes, OverlayFinishTime? Finish, long? PrintTimeMinutes)
{
    public bool HasAny => RemainingMinutes is not null || Finish is not null || PrintTimeMinutes is not null;
}

/// <summary>
/// The Times widget of the camera overlay (T-083, design T-076 2.2): the remaining time as the large number (its key value,
/// emphasized in the accent colour), the finish time with a flag and the print time with a clock (icons instead of labels;
/// screen readers get the words). Only during a job (rules of T-036, <see cref="OverlayValues"/>).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>S: hourglass + remaining time only.</item>
/// <item>M / L: label "Remaining" + the remaining time, then the finish row and the print time row.</item>
/// <item>A value that is not known is left out (its row is not drawn); a card left without a row hides (T-076 2): in M / L
/// without all three values, in S without the remaining time. Without a remaining time M / L start with the next row.</item>
/// </list>
/// The content changes about once a minute (whole minutes); polls in between post nothing. The remaining time does not tick
/// down between polls: every poll brings a new estimate, which is what is shown.
/// </remarks>
public sealed class TimesWidgetViewModel : OverlayWidgetViewModel<TimesContent>
{
    /// <summary>Narrowest card per size (T-076 2.2: M min 180, L min 220; S has no minimum).</summary>
    public static readonly OverlayWidgetMinWidths MinWidths = new(0, 180, 220);

    private static readonly PropertyChangedEventArgs ShowsRowsChanged = new(nameof(ShowsRows));
    private static readonly PropertyChangedEventArgs ShowsFinishRowChanged = new(nameof(ShowsFinishRow));
    private static readonly PropertyChangedEventArgs ShowsPrintTimeRowChanged = new(nameof(ShowsPrintTimeRow));

    private string _remainingLabel = string.Empty;
    private string _remaining = string.Empty;
    private string _finish = string.Empty;
    private string _printTime = string.Empty;
    private bool _hasRemaining;
    private bool _hasFinish;
    private bool _hasPrintTime;

    /// <summary>"Remaining" above the large number (M / L).</summary>
    public string RemainingLabel
    {
        get => _remainingLabel;
        private set => SetProperty(ref _remainingLabel, value);
    }

    /// <summary>The remaining time, "2 h 35 min" (the key value).</summary>
    public string Remaining
    {
        get => _remaining;
        private set => SetProperty(ref _remaining, value);
    }

    /// <summary>"Done at 7:50 PM", or on another day "Done October 17 at 8:00 AM".</summary>
    public string Finish
    {
        get => _finish;
        private set => SetProperty(ref _finish, value);
    }

    /// <summary>The print time so far, "47 min" (the clock icon stands for the label).</summary>
    public string PrintTime
    {
        get => _printTime;
        private set => SetProperty(ref _printTime, value);
    }

    public bool HasRemaining
    {
        get => _hasRemaining;
        private set => SetProperty(ref _hasRemaining, value);
    }

    public bool HasFinish
    {
        get => _hasFinish;
        private set
        {
            if (SetProperty(ref _hasFinish, value))
            {
                OnRowsChanged();
            }
        }
    }

    public bool HasPrintTime
    {
        get => _hasPrintTime;
        private set
        {
            if (SetProperty(ref _hasPrintTime, value))
            {
                OnRowsChanged();
            }
        }
    }

    /// <summary>The rows below the large number are drawn (M / L, when the finish or the print time is known).</summary>
    public bool ShowsRows => !IsSmall && (_hasFinish || _hasPrintTime);

    /// <summary>The finish row (M / L only).</summary>
    public bool ShowsFinishRow => !IsSmall && _hasFinish;

    /// <summary>The print time row (M / L only).</summary>
    public bool ShowsPrintTimeRow => !IsSmall && _hasPrintTime;

    /// <summary>The content of a snapshot: the times of the active job (T-036 rules), <c>default</c> without one.</summary>
    public static TimesContent ContentOf(in OverlayWidgetInput input)
    {
        PrinterSnapshot? snapshot = input.Snapshot;
        if (OverlayValues.ActiveJob(snapshot) is not { } job)
        {
            return default;
        }

        return new TimesContent(
            OverlayValues.RemainingMinutes(job),
            OverlayValues.Finish(snapshot!, input.TimeZone),
            OverlayValues.PrintTimeMinutes(job));
    }

    protected override TimesContent ComputeContent(in OverlayWidgetInput input) => ContentOf(input);

    /// <summary>A row is left in the drawn size: S has only the remaining time, M / L any of the three.</summary>
    protected override bool HasData(in TimesContent content) =>
        IsSmall ? content.RemainingMinutes is not null : content.HasAny;

    protected override void Format(in TimesContent content)
    {
        RemainingLabel = Strings.CameraOverlayRemainingLabel;
        Remaining = content.RemainingMinutes is { } remaining ? OverlayFormat.Duration(remaining) : string.Empty;
        Finish = content.Finish is { } finish ? OverlayFormat.Finish(finish) : string.Empty;
        PrintTime = content.PrintTimeMinutes is { } printTime ? OverlayFormat.Duration(printTime) : string.Empty;
        HasRemaining = content.RemainingMinutes is not null;
        HasFinish = content.Finish is not null;
        HasPrintTime = content.PrintTimeMinutes is not null;
        AccessibleName = AccessibleText(content);
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName != nameof(RenderedSize))
        {
            return;
        }

        OnRowsChanged();
        // Whether a row is left depends on the size (S shows only the remaining time). Content that had no row in the
        // previous size was not formatted then: it is formatted now, in the current language.
        bool hasData = HasData(ShownContent);
        if (hasData && !HasContent)
        {
            Format(ShownContent);
        }

        HasContent = hasData;
    }

    /// <summary>"2 h 35 min remaining, Done at 7:50 PM, Print time: 47 min": the words the icons stand for (T-076 2.2).</summary>
    private static string AccessibleText(in TimesContent content)
    {
        string? remaining = content.RemainingMinutes is { } minutes ? OverlayFormat.Remaining(minutes) : null;
        string? finish = content.Finish is { } at ? OverlayFormat.Finish(at) : null;
        string? printTime = content.PrintTimeMinutes is { } elapsed ? OverlayFormat.PrintTime(elapsed) : null;
        return string.Join(", ", new[] { remaining, finish, printTime }.Where(static part => part is not null));
    }

    private void OnRowsChanged()
    {
        OnPropertyChanged(ShowsRowsChanged);
        OnPropertyChanged(ShowsFinishRowChanged);
        OnPropertyChanged(ShowsPrintTimeRowChanged);
    }
}
