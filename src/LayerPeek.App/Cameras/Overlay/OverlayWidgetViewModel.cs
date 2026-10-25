using Avalonia.Controls.Templates;
using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;

using LayerPeek.Core.Cameras.Overlay;

namespace LayerPeek.App.Cameras.Overlay;

/// <summary>The overlay that owns a widget: recomputes it on request (a source other than the snapshots changed).</summary>
internal interface IOverlayWidgetHost
{
    /// <summary>Any thread: <paramref name="widget"/> computes its content again with the latest input.</summary>
    void Invalidate(OverlayWidgetViewModel widget);
}

/// <summary>
/// One card of the camera window overlay (T-081): its place (anchor, size, emphasis from the layout), whether it has
/// something to show and its texts. Derive from <see cref="OverlayWidgetViewModel{TContent}"/>; the overlay
/// (<see cref="CameraOverlayViewModel"/>) drives it. Public members are for the UI thread unless stated otherwise.
/// </summary>
public abstract class OverlayWidgetViewModel : ObservableObject, IDisposable
{
    /// <summary>Prefix of the <c>AutomationId</c> of the cards (T-076 5.7): <c>CameraOverlayWidget-{id}</c>.</summary>
    public const string AutomationIdPrefix = "CameraOverlayWidget-";

    private IOverlayWidgetDefinition? _definition;
    private IOverlayWidgetHost? _host;
    private OverlayAnchor _anchor;
    private OverlayWidgetSize _size = OverlayWidgetSize.Medium;
    private OverlayWidgetSize _renderedSize = OverlayWidgetSize.Medium;
    private bool _isEmphasized;
    private bool _hasContent;
    private bool _isFaulted;
    private IBrush? _cardBackground;
    private string _accessibleName = string.Empty;
    private string _automationId = AutomationIdPrefix;

    // Only the classes of this assembly derive from it directly; widgets derive from OverlayWidgetViewModel<TContent>.
    private protected OverlayWidgetViewModel()
    {
    }

    /// <summary>The widget's id in the layout.</summary>
    public string Id => Definition.Id;

    /// <summary><c>CameraOverlayWidget-{id}</c>.</summary>
    public string AutomationId => _automationId;

    /// <summary>Where the card is drawn (from the layout).</summary>
    public OverlayAnchor Anchor
    {
        get => _anchor;
        internal set => SetProperty(ref _anchor, value);
    }

    /// <summary>The size chosen in the layout (one the widget supports); see <see cref="RenderedSize"/>.</summary>
    public OverlayWidgetSize Size
    {
        get => _size;
        internal set => SetProperty(ref _size, value);
    }

    /// <summary>
    /// The size the card is drawn in: <see cref="Size"/>, or smaller in a small window (T-076 1.4). Set by the overlay
    /// panel while it measures the cards.
    /// </summary>
    public OverlayWidgetSize RenderedSize
    {
        get => _renderedSize;
        internal set
        {
            if (SetProperty(ref _renderedSize, value))
            {
                OnPropertyChanged(nameof(IsSmall));
                OnPropertyChanged(nameof(IsMedium));
                OnPropertyChanged(nameof(IsLarge));
            }
        }
    }

    public bool IsSmall => _renderedSize == OverlayWidgetSize.Small;

    public bool IsMedium => _renderedSize == OverlayWidgetSize.Medium;

    public bool IsLarge => _renderedSize == OverlayWidgetSize.Large;

    /// <summary>The key value of the widget is drawn in the accent colour (class <c>emphasized</c>, T-076 Q8).</summary>
    public bool IsEmphasized
    {
        get => _isEmphasized;
        internal set => SetProperty(ref _isEmphasized, value);
    }

    /// <summary>The widget has something to show; a widget without data hides its card.</summary>
    public bool HasContent
    {
        get => _hasContent;
        private protected set
        {
            if (SetProperty(ref _hasContent, value))
            {
                OnPropertyChanged(nameof(IsShown));
            }
        }
    }

    /// <summary>
    /// The widget failed (an exception while computing or formatting): its card stays hidden for the rest of the window's
    /// life, the other widgets go on (the failure is logged once).
    /// </summary>
    public bool IsFaulted
    {
        get => _isFaulted;
        internal set
        {
            if (SetProperty(ref _isFaulted, value))
            {
                OnPropertyChanged(nameof(IsShown));
            }
        }
    }

    /// <summary>The card is drawn (before the small-window rules, which may still leave it out).</summary>
    public bool IsShown => _hasContent && !_isFaulted;

    /// <summary>Background of the card: <c>#0E0E10</c> with the global opacity (T-077); the same for every card of a window.</summary>
    public IBrush? CardBackground
    {
        get => _cardBackground;
        internal set => SetProperty(ref _cardBackground, value);
    }

    /// <summary>The whole content in words for screen readers (icons replace words on screen).</summary>
    public string AccessibleName
    {
        get => _accessibleName;
        protected set => SetProperty(ref _accessibleName, value ?? string.Empty);
    }

    /// <summary>The content of the card (<see cref="IOverlayWidgetDefinition.CardTemplate"/>).</summary>
    public IDataTemplate CardTemplate => Definition.CardTemplate;

    internal IOverlayWidgetDefinition Definition =>
        _definition ?? throw new InvalidOperationException("The widget is not attached to an overlay.");

    /// <summary>Collision priority (<see cref="IOverlayWidgetDefinition.Priority"/>).</summary>
    internal int Priority => Definition.Priority;

    /// <summary>
    /// Left out by the small-window rules (T-076 1.4) although it has content: measured but not drawn. Set by the overlay
    /// panel while it measures, read when it arranges; UI thread.
    /// </summary>
    internal bool IsCollapsed { get; set; }

    /// <summary>Guarded by the overlay's lock: computing failed or must not run any more.</summary>
    internal bool StopsComputing { get; set; }

    /// <summary>UI thread: the overlay saw <see cref="StopsComputing"/> while taking the computed content.</summary>
    internal bool FailureTaken { get; set; }

    public void Dispose()
    {
        _host = null;
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Called once by the overlay that created the widget, before its first content.</summary>
    internal void Attach(IOverlayWidgetDefinition definition, IOverlayWidgetHost host)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(host);
        if (_definition is not null)
        {
            throw new InvalidOperationException("The widget already belongs to an overlay.");
        }

        _definition = definition;
        _host = host;
        _automationId = AutomationIdPrefix + definition.Id;
    }

    /// <summary>
    /// Under the overlay's lock, any thread: computes the content from <paramref name="input"/>; <see langword="true"/>
    /// when it differs from the last computed one (then the overlay posts one update to the UI thread).
    /// </summary>
    internal abstract bool Compute(in OverlayWidgetInput input);

    /// <summary>Under the overlay's lock on the UI thread: takes the last computed content for <see cref="ApplyTaken"/>.</summary>
    internal abstract void TakeComputed();

    /// <summary>UI thread: shows the taken content; formats texts only when it differs from the shown one.</summary>
    internal abstract void ApplyTaken();

    /// <summary>UI thread: the language changed; the shown content is formatted again.</summary>
    internal abstract void RefreshTexts();

    /// <summary>
    /// Any thread: something the widget shows besides the snapshot changed (e.g. a timer or another service); the overlay
    /// computes its content again with the latest input.
    /// </summary>
    protected void RequestUpdate() => _host?.Invalidate(this);

    /// <summary>Releases what the widget subscribed to; called when the window closes or the widget leaves the layout.</summary>
    protected virtual void Dispose(bool disposing)
    {
    }
}

/// <summary>
/// Base of every overlay widget (T-081): the content is a cheaply comparable value (as in the T-036 overlay) computed for
/// every snapshot on the monitor thread; texts are formatted on the UI thread only when the shown content
/// changed, and again after a language switch.
/// </summary>
/// <typeparam name="TContent">
/// What the card shows, already rounded like the display (a <c>readonly record struct</c>): two polls that look the same
/// must give equal contents, so that nothing is posted to the UI thread for them.
/// </typeparam>
public abstract class OverlayWidgetViewModel<TContent> : OverlayWidgetViewModel
    where TContent : struct, IEquatable<TContent>
{
    // Under the overlay's lock.
    private TContent _computed;
    private bool _hasComputed;

    // UI thread.
    private TContent _taken;
    private bool _hasTaken;
    private TContent _shown;
    private bool _hasShown;

    /// <summary>The content of the last <see cref="Format"/> (for tests and derived widgets).</summary>
    protected TContent ShownContent => _shown;

    /// <summary>
    /// Any thread, under the overlay's lock (never concurrently): the content for <paramref name="input"/>. Must be cheap,
    /// format no text and allocate nothing per poll (reuse shortened strings, e.g. with <see cref="OverlayTextCache"/>).
    /// </summary>
    protected abstract TContent ComputeContent(in OverlayWidgetInput input);

    /// <summary>Whether <paramref name="content"/> has anything to show; <see langword="false"/> hides the card. Default: not <c>default</c>.</summary>
    protected virtual bool HasData(in TContent content) => !content.Equals(default);

    /// <summary>
    /// UI thread: sets the texts (and <see cref="OverlayWidgetViewModel.AccessibleName"/>) of <paramref name="content"/>
    /// in the current language. Called only for content with data, when it changed or the language switched.
    /// </summary>
    protected abstract void Format(in TContent content);

    internal sealed override bool Compute(in OverlayWidgetInput input)
    {
        TContent content = ComputeContent(input);
        if (_hasComputed && content.Equals(_computed))
        {
            return false;
        }

        _computed = content;
        _hasComputed = true;
        return true;
    }

    internal sealed override void TakeComputed()
    {
        if (_hasComputed)
        {
            _taken = _computed;
            _hasTaken = true;
        }
    }

    internal sealed override void ApplyTaken()
    {
        if (!_hasTaken)
        {
            return;
        }

        _hasTaken = false;
        if (_hasShown && _taken.Equals(_shown))
        {
            return;
        }

        _shown = _taken;
        _hasShown = true;
        bool hasData = HasData(_shown);
        if (hasData)
        {
            Format(_shown);
        }

        HasContent = hasData;
    }

    internal sealed override void RefreshTexts()
    {
        if (_hasShown && HasData(_shown))
        {
            Format(_shown);
        }
    }
}
