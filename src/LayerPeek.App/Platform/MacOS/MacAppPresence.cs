using System.Runtime.Versioning;

namespace LayerPeek.App.Platform.MacOS;

/// <summary>
/// The Dock icon and ⌘Tab presence of the menu bar app on macOS (T-102, user decision 2026-10-29): while a window that is
/// not always on top is open the app is a regular app (<c>NSApplicationActivationPolicyRegular</c>), otherwise an
/// accessory app with the menu bar extra only (<c>NSApplicationActivationPolicyAccessory</c>, as at the start:
/// <c>LSUIElement</c> and <c>MacOSPlatformOptions.ShowInDock = false</c>). As a regular app AppKit lists its titled windows
/// in the Dock menu and brings them to the front when the Dock icon is clicked (Avalonia reports the reopen, T-043).
/// </summary>
/// <remarks>
/// Avalonia 12.1.4 sets the policy only once, at its start (<c>MacOptions::SetShowInDock</c>, the only caller of
/// <c>setActivationPolicy:</c> in <c>libAvaloniaNative</c>), and never again, so it does not undo this. Changing the policy
/// neither activates the app nor reorders its windows; the caller activates the app for a window it brings to the front
/// (<see cref="MacAppActivation"/>). Only an existing <c>NSApp</c> is changed: without AppKit (the headless UI tests, the
/// wait for a display of T-099) nothing happens. UI (main) thread only. Never throws.
/// </remarks>
[SupportedOSPlatform("macos")]
internal sealed class MacAppPresence : IAppPresence
{
    private const nint Regular = 0;
    private const nint Accessory = 1;

    public void SetVisible(bool visible, bool keepActive)
    {
        try
        {
            nint application = MacAppActivation.RunningApplication();
            if (application == 0)
            {
                return;
            }

            nint policy = visible ? Regular : Accessory;
            if (ObjCRuntime.Send(application, ObjCRuntime.Selector("activationPolicy")) == policy)
            {
                return;
            }

            bool wasActive = ObjCRuntime.SendBool(application, ObjCRuntime.Selector("isActive"));
            _ = ObjCRuntime.SendBool(application, ObjCRuntime.Selector("setActivationPolicy:"), policy);
            if (!visible && keepActive && wasActive && !ObjCRuntime.SendBool(application, ObjCRuntime.Selector("isActive")))
            {
                // An accessory app may lose the activation with its Dock icon; the window still open (the mini preview the
                // user just switched to) keeps the focus.
                MacAppActivation.Activate();
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Not an AppKit process: there is no Dock icon to change.
        }
    }
}
