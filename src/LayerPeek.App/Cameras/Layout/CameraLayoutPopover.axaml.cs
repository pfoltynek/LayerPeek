using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace LayerPeek.App.Cameras.Layout;

/// <summary>
/// The popover of a layout card (T-091, T-076 5.4); its data context is the <see cref="CameraLayoutModeViewModel"/>.
/// Keyboard: Tab between "Highlight" and the sizes, Space toggles, the arrow keys choose among S / M / L (skipping a size the
/// widget does not have); Esc is handled by the flyout (it closes and the focus returns to the card).
/// </summary>
public sealed partial class CameraLayoutPopover : UserControl
{
    public CameraLayoutPopover()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnSizeKeyDown, RoutingStrategies.Bubble);
        foreach (RadioButton size in Sizes)
        {
            size.IsCheckedChanged += (_, _) => UpdateSizeTabStop();
        }

        UpdateSizeTabStop();
    }

    /// <summary>Tab into the sizes lands on the chosen one.</summary>
    private void UpdateSizeTabStop()
    {
        if (Array.Find(Sizes, static size => size.IsChecked == true) is { } chosen)
        {
            KeyboardNavigation.SetTabOnceActiveElement(SizeGroup, chosen);
        }
    }

    /// <summary>The "Highlight" switch: gets the focus when the popover opens from the keyboard (T-076 5.5).</summary>
    internal ToggleSwitch EmphasizeSwitch => Emphasize;

    private RadioButton[] Sizes => [SizeSmall, SizeMedium, SizeLarge];

    private void OnSizeKeyDown(object? sender, KeyEventArgs e)
    {
        int index = e.Source is RadioButton current ? Array.IndexOf(Sizes, current) : -1;
        if (index < 0)
        {
            return;
        }

        int step = e.Key switch
        {
            Key.Left or Key.Up => -1,
            Key.Right or Key.Down => 1,
            _ => 0,
        };

        if (step == 0)
        {
            return;
        }

        e.Handled = true;
        RadioButton[] sizes = Sizes;
        for (int next = index + step; next >= 0 && next < sizes.Length; next += step)
        {
            if (sizes[next].IsEffectivelyEnabled)
            {
                sizes[next].IsChecked = true;
                sizes[next].Focus(NavigationMethod.Directional);
                return;
            }
        }
    }
}
