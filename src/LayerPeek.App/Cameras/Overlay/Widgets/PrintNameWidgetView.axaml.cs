using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace LayerPeek.App.Cameras.Overlay.Widgets;

/// <summary>Content of the Print name card (T-085); the card draws it with <see cref="CardTemplate"/> for a <see cref="PrintNameWidgetViewModel"/>.</summary>
public sealed partial class PrintNameWidgetView : UserControl
{
    public PrintNameWidgetView()
    {
        InitializeComponent();
    }

    /// <summary>The card content template of the Print name widget (<see cref="IOverlayWidgetDefinition.CardTemplate"/>).</summary>
    public static IDataTemplate CardTemplate { get; } = new FuncDataTemplate<PrintNameWidgetViewModel>(static (widget, _) => widget is null ? null : new PrintNameWidgetView());
}
