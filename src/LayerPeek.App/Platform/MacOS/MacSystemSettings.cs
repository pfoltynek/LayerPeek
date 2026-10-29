using System.Runtime.Versioning;

namespace LayerPeek.App.Platform.MacOS;

/// <summary>Opens a pane of System Settings on macOS (T-020): <c>[[NSWorkspace sharedWorkspace] openURL:]</c>.</summary>
[SupportedOSPlatform("macos")]
internal static class MacSystemSettings
{
    /// <summary>Opens <paramref name="address"/>, which must be an <c>x-apple.systempreferences:</c> address. UI thread.</summary>
    /// <exception cref="ArgumentException">Another kind of address (nothing else is ever opened here).</exception>
    /// <exception cref="InvalidOperationException">AppKit is not loaded or macOS refused.</exception>
    public static void Open(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (!address.IsAbsoluteUri || !string.Equals(address.Scheme, "x-apple.systempreferences", StringComparison.Ordinal))
        {
            throw new ArgumentException("Only addresses of System Settings are opened.", nameof(address));
        }

        bool opened = ObjCRuntime.InAutoreleasePool(() =>
        {
            nint workspaceType = ObjCRuntime.Class("NSWorkspace");
            nint urlType = ObjCRuntime.Class("NSURL");
            if (workspaceType == 0 || urlType == 0)
            {
                throw new InvalidOperationException("AppKit is not loaded.");
            }

            nint url = ObjCRuntime.Send(urlType, ObjCRuntime.Selector("URLWithString:"), ObjCRuntime.NSString(address.OriginalString));
            nint workspace = ObjCRuntime.Send(workspaceType, ObjCRuntime.Selector("sharedWorkspace"));
            return url != 0 && workspace != 0 && ObjCRuntime.SendBool(workspace, ObjCRuntime.Selector("openURL:"), url);
        });
        if (!opened)
        {
            throw new InvalidOperationException("macOS did not open System Settings.");
        }
    }
}
