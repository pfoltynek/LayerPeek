using Avalonia.Controls;

namespace LayerPeek.App.Cameras.Mini;

/// <summary>The info strip below the image of the mini preview (T-089); its data context is a <see cref="CameraMiniStripViewModel"/>.</summary>
public sealed partial class CameraMiniStrip : UserControl
{
    public CameraMiniStrip()
    {
        InitializeComponent();
    }
}
