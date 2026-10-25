using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace LayerPeek.App.Cameras.Overlay.Widgets;

/// <summary>Content of the Temperatures card (T-084); the card draws it with <see cref="CardTemplate"/> for a <see cref="TemperaturesWidgetViewModel"/>.</summary>
public sealed partial class TemperaturesWidgetView : UserControl
{
    public TemperaturesWidgetView()
    {
        InitializeComponent();
    }

    /// <summary>The card content template of the Temperatures widget (<see cref="IOverlayWidgetDefinition.CardTemplate"/>).</summary>
    public static IDataTemplate CardTemplate { get; } = new FuncDataTemplate<TemperaturesWidgetViewModel>(static (widget, _) => widget is null ? null : new TemperaturesWidgetView());
}
