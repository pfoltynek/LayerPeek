using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace LayerPeek.App.Windows;

/// <summary>
/// The keyboard shortcuts of the auxiliary windows that follow Apple's conventions on macOS (T-100, user decision
/// 2026-10-29): ⌘W closes the window, ⌘. is Cancel (like Esc), ⌃⌘F toggles full screen, ⌘⇧] / ⌘⇧[ switch the main tabs
/// and ⌘⌥→ / ⌘⌥← the sub-tabs. Windows keeps its own shortcuts (F11, Ctrl+Tab, Ctrl+PageDown…); every window decides by
/// a flag of its own (<see cref="IsPlatformDefault"/> unless a test sets it) whether these apply.
/// </summary>
/// <remarks>
/// Avalonia reports ⌘ as <see cref="KeyModifiers.Meta"/> and ⌃ as <see cref="KeyModifiers.Control"/>. Its logical
/// <see cref="Key"/> comes from the character of the current keyboard layout (without ⌘ and ⌃, with ⇧ and ⌥), falling back
/// to the US position; letters, the period and the arrows are where Apple's shortcuts expect them on the Latin layouts.
/// The brackets are not: on a Czech layout the keys right of P type ú and ) (] needs ⌥), so the tab shortcuts also match
/// the physical key at the US position of ] and [ (how a US keyboard types them), not only the logical bracket key.
/// </remarks>
internal static class MacShortcuts
{
    private const KeyModifiers Command = KeyModifiers.Meta;

    /// <summary>The shortcuts apply on macOS only.</summary>
    internal static bool IsPlatformDefault => OperatingSystem.IsMacOS();

    /// <summary>⌘W: close the window.</summary>
    internal static bool IsCloseWindow(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return e.KeyModifiers == Command && e.Key == Key.W;
    }

    /// <summary>⌘.: Cancel, the same as Esc.</summary>
    internal static bool IsCancel(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return e.KeyModifiers == Command && e.Key == Key.OemPeriod;
    }

    /// <summary>⌃⌘F: enter or leave full screen.</summary>
    internal static bool IsToggleFullScreen(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return e.KeyModifiers == (KeyModifiers.Control | Command) && e.Key == Key.F;
    }

    /// <summary>
    /// ⌘⇧] (next, +1) or ⌘⇧[ (previous, −1) to switch the main tabs; 0 for any other key. The bracket is matched by the
    /// logical key or by the physical key at its US position (see the remarks of the class).
    /// </summary>
    internal static int MainTabStep(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.KeyModifiers != (Command | KeyModifiers.Shift))
        {
            return 0;
        }

        if (e.Key == Key.OemCloseBrackets || e.PhysicalKey == PhysicalKey.BracketRight)
        {
            return 1;
        }

        return e.Key == Key.OemOpenBrackets || e.PhysicalKey == PhysicalKey.BracketLeft ? -1 : 0;
    }

    /// <summary>⌘⌥→ (next, +1) or ⌘⌥← (previous, −1) to switch the sub-tabs; 0 for any other key.</summary>
    internal static int SubTabStep(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.KeyModifiers != (Command | KeyModifiers.Alt))
        {
            return 0;
        }

        return e.Key switch
        {
            Key.Right => 1,
            Key.Left => -1,
            _ => 0,
        };
    }

    /// <summary>
    /// Handles ⌘W and ⌘. in <paramref name="window"/> (tunnelling, before a text box sees them) while
    /// <paramref name="isEnabled"/> says so: ⌘W runs <paramref name="closeWindow"/>, ⌘. runs <paramref name="cancel"/>
    /// (what the window's Cancel button or Esc does).
    /// </summary>
    internal static void HandleCloseKeys(Window window, Func<bool> isEnabled, Action closeWindow, Action cancel)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(isEnabled);
        ArgumentNullException.ThrowIfNull(closeWindow);
        ArgumentNullException.ThrowIfNull(cancel);
        window.AddHandler(
            InputElement.KeyDownEvent,
            (_, e) =>
            {
                if (e.Handled || !isEnabled())
                {
                    return;
                }

                if (IsCloseWindow(e))
                {
                    e.Handled = true;
                    closeWindow();
                }
                else if (IsCancel(e))
                {
                    e.Handled = true;
                    cancel();
                }
            },
            RoutingStrategies.Tunnel);
    }
}
