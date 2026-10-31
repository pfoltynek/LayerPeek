using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

using LayerPeek.App.Platform.Windows;

namespace LayerPeek.App.Windows;

/// <summary>
/// The keyboard focus across a change of the taskbar button (T-122). On Windows Avalonia hides the taskbar button by giving
/// the window a hidden owner; Windows may then take the keyboard focus away (WM_KILLFOCUS) while the window stays active, so
/// no activation gives it back and the next Space / Enter goes nowhere. Showing the button again hides and shows the window
/// with its activation, which gives the focus back to the element the window remembers (that is why a window button clicked
/// with the mouse leaves the focus on the image, <see cref="OnWindowButtonClick"/>). Other OSes keep the focus anyway (macOS
/// counts the Dock icon in <see cref="AppPresence"/>), so nothing happens there.
/// </summary>
public sealed partial class CameraWindow
{
    /// <summary>
    /// Changes <see cref="Window.ShowInTaskbar"/>, keeping the keyboard focus: if the window was active with the focus on one
    /// of its elements and is still active but without a focus after the change (at once or once the window manager's
    /// messages were handled), that element gets it back, the same way as before (from the keyboard with its focus ring).
    /// </summary>
    private void ShowInTaskbarKeepingFocus(bool showInTaskbar)
    {
        if (ShowInTaskbar == showInTaskbar)
        {
            return;
        }

        KeptFocus? kept = FocusToKeep();
        Trace($"ShowInTaskbar -> {showInTaskbar} kept={kept} {FocusText()}");
        ShowInTaskbar = showInTaskbar;
        Trace($"ShowInTaskbar done {FocusText()}");
        if (kept is { } focus)
        {
            GiveFocusBack(focus);
            Dispatcher.UIThread.Post(() => GiveFocusBack(focus), DispatcherPriority.Background);
        }
    }

    private KeptFocus? FocusToKeep()
    {
        if (!IsActive || FocusManager?.GetFocusedElement() is not InputElement element || TopLevel.GetTopLevel(element) != this)
        {
            return null;
        }

        // Pointer: no focus ring (a click moved the focus there); Tab: the focus ring of the keyboard.
        NavigationMethod method = element.Classes.Contains(":focus-visible") ? NavigationMethod.Tab : NavigationMethod.Pointer;
        return new KeptFocus(element, method);
    }

    private void GiveFocusBack(KeptFocus focus)
    {
        Trace($"GiveFocusBack closed={_closed} {FocusText()} top={TopLevel.GetTopLevel(focus.Element) == this}");
        if (_closed || !IsActive || FocusManager?.GetFocusedElement() is not null || TopLevel.GetTopLevel(focus.Element) != this)
        {
            return;
        }

        if (OperatingSystem.IsWindows() && TryGetPlatformHandle() is { HandleDescriptor: "HWND", Handle: var hwnd })
        {
            // The window manager's focus first, so that the keys come to the window as before.
            WindowFocusNative.FocusIfActive(hwnd);
        }

        _ = focus.Element.Focus(focus.Method);
    }

    /// <summary>The element that had the keyboard focus before the taskbar button changed, and how it got it.</summary>
    private readonly record struct KeptFocus(InputElement Element, NavigationMethod Method);
}
