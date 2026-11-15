using Avalonia;
using Avalonia.Input;

namespace LayerPeek.App.Windows;

/// <summary>
/// Values for the app's own context menu of text fields (App.axaml, T-242) that Avalonia's <c>TextBox</c> does not
/// offer like it offers <c>TextBox.CutGesture</c>, <c>CopyGesture</c> and <c>PasteGesture</c>.
/// </summary>
public static class TextBoxContextMenu
{
    /// <summary>
    /// The platform's Select All gesture shown next to the item (Ctrl+A on Windows, ⌘A on macOS); the text box handles
    /// the key itself. <see langword="null"/> before the platform is set up.
    /// </summary>
    public static KeyGesture? SelectAllGesture
    {
        get
        {
            List<KeyGesture>? gestures = Application.Current?.PlatformSettings?.HotkeyConfiguration.SelectAll;
            return gestures is { Count: > 0 } ? gestures[0] : null;
        }
    }
}
