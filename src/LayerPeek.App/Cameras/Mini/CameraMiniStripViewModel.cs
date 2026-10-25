using System.Globalization;

using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;

using LayerPeek.App.Cameras.Overlay;
using LayerPeek.App.Resources;
using LayerPeek.App.Tray;
using LayerPeek.Core.Cooldown;
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
/// The info strip below the image of the mini preview (T-089, design T-076 4): a 3 DIP progress bar on its top edge and the
/// summary row "● 19 % · 2 h 35 min" with room for the two toggle buttons of T-090 on the right. The row shows the number
/// first; without a job the state, while cooling down the nozzle temperature (T-079). The full text is the row's tooltip
/// and accessible name.
/// </summary>
/// <remarks>
/// <para>
/// Snapshots and cool-down changes arrive on the monitor thread: the content is computed there under a lock with the shared
/// rules of the Status and Times widgets (<see cref="OverlayValues"/>, <see cref="OverlayFormat"/>), and one update is
/// posted to the UI thread only when it changed (a poll that changes nothing visible allocates nothing). Texts are
/// formatted on the UI thread, and again after a language switch.
/// </para>
/// <para>
/// T-090 adds the collapsible name and chart rows below the summary row: they add to <see cref="StripHeight"/>, which the
/// window follows (the image keeps the aspect ratio of the stream).
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

    private const string Separator = " · ";

    private readonly string _printerId;
    private readonly IUiDispatcher _dispatcher;
    private readonly CooldownMonitor? _cooldown;
    private readonly Action _apply;
    private readonly Lock _gate = new();

    // Guarded by _gate.
    private PrinterSnapshot? _snapshot;
    private MiniStripContent _computed;
    private bool _hasComputed;
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
    private readonly double _stripHeight = CollapsedHeight;

    /// <param name="printerId">The printer of the window.</param>
    /// <param name="cooldown">The cool-down states (T-079); <see langword="null"/>: the strip never shows a cool-down.</param>
    /// <remarks>Shows "Connecting…" until <see cref="ShowCurrent"/> or the first <see cref="OnSnapshot"/>.</remarks>
    internal CameraMiniStripViewModel(string printerId, IUiDispatcher dispatcher, CooldownMonitor? cooldown)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerId);
        ArgumentNullException.ThrowIfNull(dispatcher);
        _printerId = printerId;
        _dispatcher = dispatcher;
        _cooldown = cooldown;
        _apply = ApplyPosted;
        if (_cooldown is not null)
        {
            _cooldown.Changed += OnCooldownChanged;
        }

        ShowCurrent(null);
    }

    /// <summary>
    /// Height of the whole strip (DIP): <see cref="CollapsedHeight"/>; T-090 adds the name and chart rows here and raises
    /// the change, which the window follows with its height.
    /// </summary>
    public double StripHeight => _stripHeight;

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

    /// <summary>The language changed: the shown content is formatted again.</summary>
    public void RefreshTexts()
    {
        if (_hasShown && !_disposed)
        {
            Format(_shown);
        }
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

    /// <summary>Under the lock: the content now; whether it differs from the last computed one.</summary>
    private bool ComputeLocked()
    {
        CooldownState cooldown = _cooldown?.GetState(_printerId) ?? CooldownState.None;
        MiniStripContent content = MiniStripContent.Of(_snapshot, cooldown);
        if (_hasComputed && content == _computed)
        {
            return false;
        }

        _computed = content;
        _hasComputed = true;
        return true;
    }

    // UI thread.
    private void ApplyPosted()
    {
        MiniStripContent content;
        lock (_gate)
        {
            _applyPending = false;
            if (_disposed || !_hasComputed)
            {
                return;
            }

            content = _computed;
        }

        if (_hasShown && content == _shown)
        {
            return;
        }

        _shown = content;
        _hasShown = true;
        Format(content);
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

    private static string Degrees(long celsius) => celsius.ToString("N0", CultureInfo.CurrentCulture);

    private static string Format(string format, object? arg0) => string.Format(CultureInfo.CurrentCulture, format, arg0);

    private static string Format(string format, object? arg0, object? arg1) => string.Format(CultureInfo.CurrentCulture, format, arg0, arg1);
}
