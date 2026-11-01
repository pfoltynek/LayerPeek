using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace LayerPeek.App.Windows;

/// <summary>
/// Makes the letter alone run an item of a menu that Avalonia draws itself, like classic Windows menus and like Avalonia's
/// own submenus do: the tray menu on Windows (T-042 point 2, T-069 Q2), and the context menus and menu flyouts of the app's
/// windows (T-121: the camera window's context menu, the printer image menu of the settings).
/// <para>
/// Root cause (Avalonia 12.1): such a menu is the whole content of a top level of its own, whose access key handler is the
/// default one of every top level; it handles a letter only with Alt held down (or while a main menu bar is open, which
/// these have none of). Avalonia.Win32 shows the main tray menu in an internal borderless <see cref="Window"/>
/// (<c>TrayIconImpl.TrayPopupRoot</c>) whose content is a <see cref="MenuFlyoutPresenter"/>; a <see cref="ContextMenu"/>
/// or a <see cref="MenuFlyout"/> opens in a <see cref="PopupRoot"/> whose content is the menu (its presenter). Submenus open
/// in popups whose access key handler (<c>MenuItemAccessKeyHandler</c>, set through <c>MenuItem</c>'s popup) handles the
/// letter alone. So in those menus only Alt+letter worked, in their submenus the letter alone too.
/// </para>
/// <para>
/// Fix: in such a top level, a letter or digit typed without Alt, Ctrl or the Windows/Command key (Shift does not matter,
/// as in Windows menus) that nothing in the menu handled is raised again on the top level as Alt+that key. Avalonia's own
/// access key handler then does everything else exactly as for Alt+letter: only enabled, visible items of this menu, the
/// item runs and closes the menu or opens its submenu, several items with the same letter take turns in focus without
/// running (as in Windows menus), a letter of no item does nothing. Keys typed in a submenu (its own popup, whose keys
/// bubble on to the menu) are left to the submenu. Other top levels keep the usual rule (a letter in a text box or a
/// flyout that is no menu stays a letter).
/// </para>
/// <para>
/// macOS: the tray menu is a native NSMenu there, so no such window exists; menus of macOS have no access keys (typing
/// selects, Return runs), so the context menus and menu flyouts keep Avalonia's own behaviour there (<c>popupMenus</c> off,
/// the letter alone does nothing).
/// </para>
/// </summary>
internal static class MenuAccessKeys
{
    private const KeyModifiers NotAllowed = KeyModifiers.Alt | KeyModifiers.Control | KeyModifiers.Meta;

    private static int s_registered;

    private static bool s_popupMenus;

    /// <summary>
    /// Registers the class handler once per process (the handler is static; later calls do nothing). UI thread.
    /// <paramref name="popupMenus"/>: whether context menus and menu flyouts get the letter alone too (not on macOS).
    /// </summary>
    public static void Register(bool popupMenus)
    {
        if (Interlocked.Exchange(ref s_registered, 1) == 0)
        {
            s_popupMenus = popupMenus;
            // Bubble: the menu (arrows, Enter, Escape) and the focused item handle the key first.
            InputElement.KeyDownEvent.AddClassHandler<TopLevel>(OnKeyDown, RoutingStrategies.Bubble);
        }
    }

    /// <summary>
    /// Whether <paramref name="root"/> shows a menu as its whole content: the tray menu window of Avalonia.Win32 and, with
    /// <paramref name="popupMenus"/>, the popup of a context menu or a menu flyout.
    /// </summary>
    internal static bool IsMenuRoot(TopLevel root, bool popupMenus) => root switch
    {
        PopupRoot popup => popupMenus && popup.Content is ContextMenu or MenuFlyoutPresenter,
        Window window => window.Content is MenuFlyoutPresenter,
        _ => false,
    };

    /// <summary>Whether the key press is a plain letter or digit that an Avalonia top level would treat as an access key only with Alt.</summary>
    internal static bool IsPlainAccessKey(KeyEventArgs e) =>
        (e.KeyModifiers & NotAllowed) == 0
        && e.KeySymbol is { Length: 1 } symbol
        && char.IsLetterOrDigit(symbol[0]);

    private static void OnKeyDown(TopLevel root, KeyEventArgs e)
    {
        if (e.Handled || !IsPlainAccessKey(e) || !IsMenuRoot(root, s_popupMenus) || !IsWithin(root, e.Source))
        {
            return;
        }

        // Raised on the top level itself, so no control of the menu sees a phantom Alt+letter; Avalonia's access key
        // handler (an instance handler of the top level, which runs after this class handler) processes it like a real
        // one. It has Alt, so it does not come back here.
        KeyEventArgs withAlt = new()
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = e.Key,
            KeyModifiers = e.KeyModifiers | KeyModifiers.Alt,
            PhysicalKey = e.PhysicalKey,
            KeySymbol = e.KeySymbol,
            KeyDeviceType = e.KeyDeviceType,
        };
        root.RaiseEvent(withAlt);
        e.Handled = withAlt.Handled;
    }

    /// <summary>Whether the key comes from this top level, not from a submenu (a popup of its own) that it bubbled up from.</summary>
    private static bool IsWithin(TopLevel root, object? source) =>
        source is Visual visual && (ReferenceEquals(visual, root) || root.IsVisualAncestorOf(visual));
}
