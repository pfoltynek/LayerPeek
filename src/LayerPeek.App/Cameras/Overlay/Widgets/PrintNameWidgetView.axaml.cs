using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace LayerPeek.App.Cameras.Overlay.Widgets;

/// <summary>
/// Content of the Print name card (T-085); the card draws it with <see cref="CardTemplate"/> for a
/// <see cref="PrintNameWidgetViewModel"/>. Tells the view model the render scaling of its window, so that the G-code
/// thumbnail (T-087) is decoded for the monitor the window is on.
/// </summary>
public sealed partial class PrintNameWidgetView : UserControl
{
    private TopLevel? _topLevel;

    public PrintNameWidgetView()
    {
        InitializeComponent();
    }

    /// <summary>The card content template of the Print name widget (<see cref="IOverlayWidgetDefinition.CardTemplate"/>).</summary>
    public static IDataTemplate CardTemplate { get; } = new FuncDataTemplate<PrintNameWidgetViewModel>(static (widget, _) => widget is null ? null : new PrintNameWidgetView());

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel is not null)
        {
            _topLevel.ScalingChanged += OnScalingChanged;
        }

        ApplyScaling();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_topLevel is not null)
        {
            _topLevel.ScalingChanged -= OnScalingChanged;
            _topLevel = null;
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        ApplyScaling();
    }

    private void OnScalingChanged(object? sender, EventArgs e) => ApplyScaling();

    private void ApplyScaling()
    {
        if (_topLevel is not null && DataContext is PrintNameWidgetViewModel widget)
        {
            widget.RenderScaling = _topLevel.RenderScaling;
        }
    }
}
