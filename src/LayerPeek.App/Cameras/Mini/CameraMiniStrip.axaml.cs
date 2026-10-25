using Avalonia;
using Avalonia.Controls;
using Avalonia.Rendering;

namespace LayerPeek.App.Cameras.Mini;

/// <summary>The info strip below the image of the mini preview (T-089, T-090); its data context is a <see cref="CameraMiniStripViewModel"/>.</summary>
/// <remarks>The name row shows the material chip from a window width of 320 and the layer height chip from 400 (T-076 4).</remarks>
public sealed partial class CameraMiniStrip : UserControl
{
    /// <summary>From this width the name row shows the material chip.</summary>
    public const double OneChipWidth = 320;

    /// <summary>From this width the name row shows the layer height chip too.</summary>
    public const double TwoChipsWidth = 400;

    // Layout rounding may give a 320 DIP window a strip a hair narrower.
    private const double WidthTolerance = 0.5;

    public CameraMiniStrip()
    {
        InitializeComponent();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        double width = e.NewSize.Width + WidthTolerance;
        NameRow.Classes.Set("chipsOne", width >= OneChipWidth && width < TwoChipsWidth);
        NameRow.Classes.Set("chipsTwo", width >= TwoChipsWidth);
    }
}

/// <summary>
/// The background and rows of the mini preview strip. The strip lies above the resize grips of the borderless window
/// (T-076 4: the toggle buttons are above the grips). Its own hit test leaves the outer <see cref="GripSize"/> DIP on the
/// left, right and bottom to the grips below it, so those edges still resize the window; the toggle buttons are hit tested
/// on their own and stay clickable there, so the bottom edge right below them does not resize. Everywhere else a press
/// hits the strip (dragging moves the window). Parts drawn into the edges must not take the pointer
/// (<c>IsHitTestVisible="False"</c>, e.g. the progress bar).
/// </summary>
public sealed class CameraMiniStripPanel : Grid, ICustomHitTest
{
    /// <summary>The width of the window's resize grips (DIP).</summary>
    public const double GripSize = 6;

    /// <inheritdoc/>
    public bool HitTest(Point point)
    {
        Size size = Bounds.Size;
        return point.X >= GripSize
            && point.X < size.Width - GripSize
            && point.Y >= 0
            && point.Y < size.Height - GripSize;
    }
}
