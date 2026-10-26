using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace LayerPeek.App.Cameras.Overlay.Widgets;

/// <summary>Content of the Cool-down card (T-088); the card draws it with <see cref="CardTemplate"/> for a <see cref="CooldownWidgetViewModel"/>.</summary>
public sealed partial class CooldownWidgetView : UserControl
{
    public CooldownWidgetView()
    {
        InitializeComponent();
    }

    /// <summary>The card content template of the Cool-down widget (<see cref="IOverlayWidgetDefinition.CardTemplate"/>).</summary>
    public static IDataTemplate CardTemplate { get; } = new FuncDataTemplate<CooldownWidgetViewModel>(static (widget, _) => widget is null ? null : new CooldownWidgetView());
}
