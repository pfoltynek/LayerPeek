using System.Runtime.Versioning;

using Avalonia.Threading;

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
/// neither activates the app nor reorders its windows (but AppKit deactivates an app that becomes an accessory app, which is
/// undone for the windows that stay open, T-137); the caller activates the app for a window it brings to the front
/// (<see cref="MacAppActivation"/>). Only an existing <c>NSApp</c> is changed: without AppKit (the headless UI tests, the
/// wait for a display of T-099) nothing happens. UI (main) thread only. Never throws.
/// </remarks>
[SupportedOSPlatform("macos")]
internal sealed class MacAppPresence : IAppPresence
{
    private const nint Regular = 0;
    private const nint Accessory = 1;

    // AppKit deactivates an app that became an accessory app a moment later, not within setActivationPolicy: (T-137,
    // macOS 27): the app is watched this long after the change and activated again when it lost the activation.
    private static readonly TimeSpan WatchInterval = TimeSpan.FromMilliseconds(50);
    private const int WatchTicks = 20;

    private DispatcherTimer? _watch;

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

            bool wasActive = IsActive(application);
            StopWatch();
            _ = ObjCRuntime.SendBool(application, ObjCRuntime.Selector("setActivationPolicy:"), policy);
            if (!visible && keepActive && wasActive)
            {
                // An accessory app loses the activation with its Dock icon; the window still open (the mini preview the
                // user just switched to, a window just switched to always on top) keeps the focus.
                if (!IsActive(application))
                {
                    MacAppActivation.Activate();
                }

                Watch(application);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Not an AppKit process: there is no Dock icon to change.
        }
    }

    private static bool IsActive(nint application) => ObjCRuntime.SendBool(application, ObjCRuntime.Selector("isActive"));

    private void Watch(nint application)
    {
        int ticks = 0;
        _watch = new DispatcherTimer(WatchInterval, DispatcherPriority.Normal, (_, _) =>
        {
            if (!IsActive(application))
            {
                // Activated once: when another app takes the front again, that was the user.
                StopWatch();
                MacAppActivation.Activate();
            }
            else if (++ticks >= WatchTicks)
            {
                StopWatch();
            }
        });
        _watch.Start();
    }

    private void StopWatch()
    {
        _watch?.Stop();
        _watch = null;
    }
}
