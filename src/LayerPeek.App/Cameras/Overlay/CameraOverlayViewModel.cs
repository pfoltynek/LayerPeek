using System.Globalization;

using Avalonia.Media;
using Avalonia.Media.Immutable;

using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.Extensions.Logging;

using LayerPeek.App.Tray;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Overlay;
using LayerPeek.Core.Printers;

namespace LayerPeek.App.Cameras.Overlay;

/// <summary>
/// The widget overlay of one camera window (T-081): the widgets of the printer's layout (<see cref="CameraOverlayLayout"/>)
/// that are visible and registered, split into the six anchors in stacking order, with the global card opacity.
/// </summary>
/// <remarks>
/// <para>
/// Snapshots arrive on the monitor thread (<see cref="OnSnapshot"/>): every widget computes its cheaply comparable content
/// there, under a lock; one update is posted to the UI thread only when a widget's content changed, and there only the
/// changed widgets format their texts. The per-snapshot path allocates nothing.
/// </para>
/// <para>
/// A widget that throws (while being created, computing or formatting) is logged once and hidden; the window and the other
/// widgets go on. Ids without a registered widget are skipped (newer versions, widgets not implemented yet).
/// </para>
/// <para>
/// Cards are drawn in the normal window and on full screen, which share the layout; the mini preview has no cards (user
/// decision 2026-10-24, <see cref="AreCardsVisible"/>). Saved settings apply right away; the anchor lists change only when
/// the layout changes which widgets sit where, so a new size or emphasis changes the cards in place.
/// </para>
/// <para>Public members are for the UI thread unless stated otherwise.</para>
/// </remarks>
public sealed partial class CameraOverlayViewModel : ObservableObject, IDisposable, IOverlayWidgetHost
{
    private static readonly OverlayWidgetViewModel[] NoWidgets = [];

    // The anchor list properties in OverlayAnchor order.
    private static readonly string[] AnchorPropertyNames = [nameof(TopLeft), nameof(TopCenter), nameof(TopRight), nameof(BottomLeft), nameof(BottomCenter), nameof(BottomRight)];

    private readonly OverlayWidgetRegistry _registry;
    private readonly OverlayWidgetContext _context;
    private readonly IUiDispatcher _dispatcher;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly Action _apply;
    private readonly Lock _gate = new();

    // Guarded by _gate (read by the monitor thread); written on the UI thread.
    private PrinterSnapshot? _snapshot;
    private PrinterDefinition _printer;
    private CameraWindowMode _mode;
    private OverlayWidgetViewModel[] _widgets = NoWidgets;
    private bool _applyPending;
    private bool _disposed;

    // UI thread.
    private readonly OverlayWidgetViewModel[][] _anchors = [NoWidgets, NoWidgets, NoWidgets, NoWidgets, NoWidgets, NoWidgets];
    private CameraOverlayLayout? _layout;
    private int _opacityPercent;
    private IBrush _cardBackground;

    /// <param name="snapshot">The latest snapshot of the printer; <see langword="null"/> before the first poll.</param>
    /// <param name="opacityPercent">The global card opacity (<see cref="CameraOverlayOpacity"/>); an invalid value counts as the default.</param>
    internal CameraOverlayViewModel(
        OverlayWidgetRegistry registry,
        OverlayWidgetContext context,
        IUiDispatcher dispatcher,
        ILogger logger,
        PrinterDefinition printer,
        CameraOverlayLayout layout,
        int opacityPercent,
        CameraWindowMode mode,
        PrinterSnapshot? snapshot)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(printer);
        ArgumentNullException.ThrowIfNull(layout);
        _registry = registry;
        _context = context;
        _dispatcher = dispatcher;
        _logger = logger;
        _time = context.Time;
        _apply = ApplyPosted;
        _printer = printer;
        _mode = mode;
        _snapshot = snapshot;
        _opacityPercent = Coerce(opacityPercent);
        _cardBackground = CardBrush(_opacityPercent);
        ApplySettings(printer, layout, opacityPercent);
    }

    /// <summary>Cards are drawn: in the normal window and on full screen, not in the mini preview.</summary>
    public bool AreCardsVisible => _mode != CameraWindowMode.Mini;

    /// <summary>The layout in use (follows saved settings).</summary>
    public CameraOverlayLayout Layout => _layout!;

    /// <summary>The global opacity of the card backgrounds in percent (30–95).</summary>
    public int OpacityPercent => _opacityPercent;

    /// <summary><c>#0E0E10</c> with <see cref="OpacityPercent"/> as alpha; set on every card.</summary>
    public IBrush CardBackground => _cardBackground;

    /// <summary>Every widget drawn, in layout order (grouped by anchor, each group from the window edge inward).</summary>
    public IReadOnlyList<OverlayWidgetViewModel> Widgets => _widgets;

    public IReadOnlyList<OverlayWidgetViewModel> TopLeft => _anchors[(int)OverlayAnchor.TopLeft];

    public IReadOnlyList<OverlayWidgetViewModel> TopCenter => _anchors[(int)OverlayAnchor.TopCenter];

    public IReadOnlyList<OverlayWidgetViewModel> TopRight => _anchors[(int)OverlayAnchor.TopRight];

    public IReadOnlyList<OverlayWidgetViewModel> BottomLeft => _anchors[(int)OverlayAnchor.BottomLeft];

    public IReadOnlyList<OverlayWidgetViewModel> BottomCenter => _anchors[(int)OverlayAnchor.BottomCenter];

    public IReadOnlyList<OverlayWidgetViewModel> BottomRight => _anchors[(int)OverlayAnchor.BottomRight];

    /// <summary>The widgets of <paramref name="anchor"/>, from the window edge inward.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Unknown anchor.</exception>
    public IReadOnlyList<OverlayWidgetViewModel> InAnchor(OverlayAnchor anchor) =>
        OverlayAnchors.IsDefined(anchor) ? _anchors[(int)anchor] : throw new ArgumentOutOfRangeException(nameof(anchor), anchor, "Unknown overlay anchor.");

    /// <summary>
    /// Any thread: a new snapshot of the printer. Every widget computes its content; one update is posted to the UI
    /// thread only when a content changed.
    /// </summary>
    public void OnSnapshot(PrinterSnapshot snapshot)
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
            post = ComputeLocked(_widgets) && !_applyPending;
            _applyPending |= post;
        }

        if (post)
        {
            _dispatcher.Post(_apply);
        }
    }

    /// <summary>
    /// The window opens: the latest snapshot the monitor had (read after subscribing) is shown right away, unless a newer
    /// one arrived meanwhile through <see cref="OnSnapshot"/>.
    /// </summary>
    internal void ShowCurrent(PrinterSnapshot? current)
    {
        if (_disposed)
        {
            return;
        }

        OverlayWidgetViewModel[] widgets;
        lock (_gate)
        {
            _snapshot ??= current;
            widgets = _widgets;
            ComputeLocked(widgets);
            TakeLocked(widgets);
        }

        ApplyTaken(widgets);
    }

    /// <summary>The window switched modes: the cards show or hide; widgets see the new mode right away.</summary>
    internal void SetMode(CameraWindowMode mode)
    {
        if (_disposed || _mode == mode)
        {
            return;
        }

        bool wereVisible = AreCardsVisible;
        lock (_gate)
        {
            _mode = mode;
            ComputeLocked(_widgets);
            TakeLocked(_widgets);
        }

        ApplyTaken(_widgets);
        if (wereVisible != AreCardsVisible)
        {
            OnPropertyChanged(nameof(AreCardsVisible));
        }
    }

    /// <summary>
    /// Saved settings (or the window opening): the printer, its layout and the global opacity apply right away, in place.
    /// Widgets that stay keep their instance (and card); only a different set or order in an anchor replaces its list.
    /// </summary>
    internal void ApplySettings(PrinterDefinition printer, CameraOverlayLayout layout, int opacityPercent)
    {
        ArgumentNullException.ThrowIfNull(printer);
        ArgumentNullException.ThrowIfNull(layout);
        if (_disposed)
        {
            return;
        }

        SetOpacity(opacityPercent);
        if (layout != _layout)
        {
            _layout = layout;
            Rebuild(layout);
        }

        OverlayWidgetViewModel[] widgets;
        lock (_gate)
        {
            _printer = printer;
            widgets = _widgets;
            ComputeLocked(widgets);
            TakeLocked(widgets);
        }

        ApplyTaken(widgets);
    }

    /// <summary>The language changed: the shown contents are formatted again.</summary>
    public void RefreshTexts()
    {
        if (_disposed)
        {
            return;
        }

        foreach (OverlayWidgetViewModel widget in _widgets)
        {
            if (widget.IsFaulted)
            {
                continue;
            }

            try
            {
                widget.RefreshTexts();
            }
            catch (Exception ex)
            {
                Fail(widget, ex);
            }
        }
    }

    public void Dispose()
    {
        OverlayWidgetViewModel[] widgets;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            widgets = _widgets;
        }

        foreach (OverlayWidgetViewModel widget in widgets)
        {
            DisposeWidget(widget);
        }
    }

    void IOverlayWidgetHost.Invalidate(OverlayWidgetViewModel widget)
    {
        bool post;
        lock (_gate)
        {
            if (_disposed || Array.IndexOf(_widgets, widget) < 0)
            {
                return;
            }

            OverlayWidgetInput input = InputLocked();
            post = ComputeLocked(widget, input) && !_applyPending;
            _applyPending |= post;
        }

        if (post)
        {
            _dispatcher.Post(_apply);
        }
    }

    private static int Coerce(int percent) => CameraOverlayOpacity.IsValid(percent) ? percent : CameraOverlayOpacity.DefaultPercent;

    /// <summary><c>#0E0E10</c> with <paramref name="percent"/> as alpha (T-076 1.3).</summary>
    private static ImmutableSolidColorBrush CardBrush(int percent) =>
        new(Color.FromArgb((byte)Math.Round(percent * 255 / 100.0, MidpointRounding.AwayFromZero), 0x0E, 0x0E, 0x10));

    private static OverlayWidgetSize Supported(OverlayWidgetSize size, IReadOnlyList<OverlayWidgetSize> supported)
    {
        // The largest supported size not above the chosen one, else the smallest supported.
        OverlayWidgetSize? best = null;
        foreach (OverlayWidgetSize candidate in supported)
        {
            if (candidate <= size && (best is null || candidate > best))
            {
                best = candidate;
            }
        }

        return best ?? supported[0];
    }

    private void SetOpacity(int opacityPercent)
    {
        int percent = Coerce(opacityPercent);
        if (percent == _opacityPercent && _layout is not null)
        {
            return;
        }

        bool changed = percent != _opacityPercent;
        _opacityPercent = percent;
        _cardBackground = CardBrush(percent);
        foreach (OverlayWidgetViewModel widget in _widgets)
        {
            widget.CardBackground = _cardBackground;
        }

        if (changed)
        {
            OnPropertyChanged(nameof(OpacityPercent));
            OnPropertyChanged(nameof(CardBackground));
        }
    }

    /// <summary>UI thread: the widgets of <paramref name="layout"/>; existing ones are reused, others created or disposed.</summary>
    private void Rebuild(CameraOverlayLayout layout)
    {
        OverlayWidgetViewModel[] previous = _widgets;
        List<OverlayWidgetViewModel> next = new(layout.Widgets.Count);
        foreach (OverlayWidgetPlacement placement in layout.Widgets)
        {
            if (!placement.Visible || _registry.Find(placement.WidgetId) is not { } definition)
            {
                // Hidden, or no widget registered for the id (a newer version's, or not implemented yet): not drawn.
                continue;
            }

            OverlayWidgetViewModel? widget = Array.Find(previous, existing => ReferenceEquals(existing.Definition, definition)) ?? Create(definition);
            if (widget is null)
            {
                continue;
            }

            widget.Anchor = placement.Anchor;
            widget.Size = Supported(placement.Size, definition.SupportedSizes);
            widget.IsEmphasized = placement.Emphasized;
            widget.CardBackground = _cardBackground;
            next.Add(widget);
        }

        OverlayWidgetViewModel[] widgets = [.. next];
        lock (_gate)
        {
            _widgets = widgets;
        }

        foreach (OverlayWidgetViewModel old in previous)
        {
            if (Array.IndexOf(widgets, old) < 0)
            {
                DisposeWidget(old);
            }
        }

        if (!previous.AsSpan().SequenceEqual(widgets))
        {
            OnPropertyChanged(nameof(Widgets));
        }

        foreach (OverlayAnchor anchor in OverlayAnchors.All)
        {
            OverlayWidgetViewModel[] inAnchor = Array.FindAll(widgets, widget => widget.Anchor == anchor);
            if (!_anchors[(int)anchor].AsSpan().SequenceEqual(inAnchor))
            {
                _anchors[(int)anchor] = inAnchor.Length == 0 ? NoWidgets : inAnchor;
                OnPropertyChanged(AnchorPropertyNames[(int)anchor]);
            }
        }
    }

    private OverlayWidgetViewModel? Create(IOverlayWidgetDefinition definition)
    {
        try
        {
            OverlayWidgetViewModel widget = definition.Create(_context)
                ?? throw new InvalidOperationException("The widget definition created no view model.");
            widget.Attach(definition, this);
            return widget;
        }
        catch (Exception ex)
        {
            LogWidgetFailed(definition.Id, "creating", ex);
            return null;
        }
    }

    private OverlayWidgetInput InputLocked() =>
        new(_snapshot, _printer, _mode, _time.GetUtcNow(), _time.LocalTimeZone, CultureInfo.CurrentCulture);

    /// <summary>Under the lock: every widget computes its content; whether one changed (or failed).</summary>
    private bool ComputeLocked(OverlayWidgetViewModel[] widgets)
    {
        if (widgets.Length == 0)
        {
            return false;
        }

        OverlayWidgetInput input = InputLocked();
        bool changed = false;
        foreach (OverlayWidgetViewModel widget in widgets)
        {
            changed |= ComputeLocked(widget, input);
        }

        return changed;
    }

    private bool ComputeLocked(OverlayWidgetViewModel widget, in OverlayWidgetInput input)
    {
        if (widget.StopsComputing)
        {
            return false;
        }

        try
        {
            return widget.Compute(input);
        }
        catch (Exception ex)
        {
            // Hidden on the UI thread by the update this causes; never computed again.
            widget.StopsComputing = true;
            LogWidgetFailed(widget.Id, "computing", ex);
            return true;
        }
    }

    private static void TakeLocked(OverlayWidgetViewModel[] widgets)
    {
        foreach (OverlayWidgetViewModel widget in widgets)
        {
            widget.FailureTaken = widget.StopsComputing;
            if (!widget.StopsComputing)
            {
                widget.TakeComputed();
            }
        }
    }

    // UI thread.
    private void ApplyPosted()
    {
        OverlayWidgetViewModel[] widgets;
        lock (_gate)
        {
            _applyPending = false;
            if (_disposed)
            {
                return;
            }

            widgets = _widgets;
            TakeLocked(widgets);
        }

        ApplyTaken(widgets);
    }

    private void ApplyTaken(OverlayWidgetViewModel[] widgets)
    {
        foreach (OverlayWidgetViewModel widget in widgets)
        {
            if (widget.FailureTaken)
            {
                widget.IsFaulted = true;
                continue;
            }

            if (widget.IsFaulted)
            {
                continue;
            }

            try
            {
                widget.ApplyTaken();
            }
            catch (Exception ex)
            {
                Fail(widget, ex);
            }
        }
    }

    /// <summary>UI thread: formatting failed; the widget is hidden and never computed again.</summary>
    private void Fail(OverlayWidgetViewModel widget, Exception exception)
    {
        lock (_gate)
        {
            widget.StopsComputing = true;
        }

        widget.IsFaulted = true;
        LogWidgetFailed(widget.Id, "formatting", exception);
    }

    private void DisposeWidget(OverlayWidgetViewModel widget)
    {
        try
        {
            widget.Dispose();
        }
        catch (Exception ex)
        {
            LogWidgetFailed(widget.Id, "disposing", ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The camera overlay widget {WidgetId} failed while {Phase}; it is hidden in this window.")]
    private partial void LogWidgetFailed(string widgetId, string phase, Exception exception);
}
