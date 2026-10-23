using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace PrusaTrayDiagnostic.App.Tray.Menu;

/// <summary>
/// Makes the letter alone run an item of the tray menu that Avalonia draws itself (Windows), like classic Windows menus
/// and like Avalonia's own submenus do (T-042 point 2, T-069 Q2).
/// <para>
/// Root cause (Avalonia 12.1): Avalonia.Win32 shows the main tray menu in an internal borderless <see cref="Window"/>
/// (<c>TrayIconImpl.TrayPopupRoot</c>) whose content is a <see cref="MenuFlyoutPresenter"/>. That window gets the
/// default access key handler of every window, which handles a letter only with Alt held down (or while a main menu bar
/// is open, which this window has none of). Submenus open in popups whose access key handler (<c>MenuItemAccessKeyHandler</c>,
/// set through <c>MenuItem</c>'s popup) handles the letter alone. So in the main menu only Alt+letter worked.
/// </para>
/// <para>
/// Fix: in such a window, a letter or digit typed without Alt, Ctrl or the Windows/Command key (Shift does not matter,
/// as in Windows menus) that nothing in the menu handled is raised again on the window as Alt+that key. Avalonia's own
/// access key handler then does everything else exactly as for Alt+letter: only enabled, visible items of this menu, the
/// item runs and closes the menu or opens its submenu, several items with the same letter take turns in focus. Other
/// windows keep the usual rule (a letter in a text box stays a letter). Platform-neutral: macOS shows a native NSMenu, so
/// no such window exists there.
/// </para>
/// </summary>
internal static class TrayMenuAccessKeys
{
    private const KeyModifiers NotAllowed = KeyModifiers.Alt | KeyModifiers.Control | KeyModifiers.Meta;

    private static int s_registered;

    /// <summary>Registers the class handler once per process (the handler is static; later calls do nothing). UI thread.</summary>
    public static void Register()
    {
        if (Interlocked.Exchange(ref s_registered, 1) == 0)
        {
            // Bubble: the menu (arrows, Enter, Escape) and the focused item handle the key first.
            InputElement.KeyDownEvent.AddClassHandler<Window>(OnKeyDown, RoutingStrategies.Bubble);
        }
    }

    /// <summary>Whether <paramref name="window"/> shows a menu as its whole content: the tray menu of Avalonia.Win32.</summary>
    internal static bool IsMenuWindow(Window window) => window.Content is MenuFlyoutPresenter;

    /// <summary>Whether the key press is a plain letter or digit that an Avalonia window would treat as an access key only with Alt.</summary>
    internal static bool IsPlainAccessKey(KeyEventArgs e) =>
        (e.KeyModifiers & NotAllowed) == 0
        && e.KeySymbol is { Length: 1 } symbol
        && char.IsLetterOrDigit(symbol[0]);

    private static void OnKeyDown(Window window, KeyEventArgs e)
    {
        if (e.Handled || !IsMenuWindow(window) || !IsPlainAccessKey(e))
        {
            return;
        }

        // Raised on the window itself, so no control of the menu sees a phantom Alt+letter; Avalonia's access key handler
        // (an instance handler of the window, which runs after this class handler) processes it like a real one. It has
        // Alt, so it does not come back here.
        KeyEventArgs withAlt = new()
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = e.Key,
            KeyModifiers = e.KeyModifiers | KeyModifiers.Alt,
            PhysicalKey = e.PhysicalKey,
            KeySymbol = e.KeySymbol,
            KeyDeviceType = e.KeyDeviceType,
        };
        window.RaiseEvent(withAlt);
        e.Handled = withAlt.Handled;
    }
}
