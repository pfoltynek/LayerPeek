using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace LayerPeek.App.Cameras.Overlay.Widgets;

/// <summary>Content of the Times card (T-083); the card draws it with <see cref="CardTemplate"/> for a <see cref="TimesWidgetViewModel"/>.</summary>
public sealed partial class TimesWidgetView : UserControl
{
    public TimesWidgetView()
    {
        InitializeComponent();
    }

    /// <summary>The card content template of the Times widget (<see cref="IOverlayWidgetDefinition.CardTemplate"/>).</summary>
    public static IDataTemplate CardTemplate { get; } = new FuncDataTemplate<TimesWidgetViewModel>(static (widget, _) => widget is null ? null : new TimesWidgetView());
}
