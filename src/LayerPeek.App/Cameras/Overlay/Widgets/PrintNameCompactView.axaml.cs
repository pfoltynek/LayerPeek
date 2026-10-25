using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace LayerPeek.App.Cameras.Overlay.Widgets;

/// <summary>
/// The compact Print name row of the mini preview strip (prepared in T-085 for T-090): the same
/// <see cref="PrintNameWidgetViewModel"/> as the card, drawn with <see cref="RowTemplate"/>.
/// </summary>
public sealed partial class PrintNameCompactView : UserControl
{
    public PrintNameCompactView()
    {
        InitializeComponent();
    }

    /// <summary>The template of the name row for a <see cref="PrintNameWidgetViewModel"/>.</summary>
    public static IDataTemplate RowTemplate { get; } = new FuncDataTemplate<PrintNameWidgetViewModel>(static (widget, _) => widget is null ? null : new PrintNameCompactView());
}
