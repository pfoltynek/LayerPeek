using System.Runtime.Versioning;

using Avalonia.Platform;

namespace LayerPeek.App.Platform.MacOS;

/// <summary>
/// The app icon in the Dock, ⌘Tab and on minimized windows when the running bundle has none (T-139): the development
/// run (<c>dotnet run</c>, no bundle) and the test bundle of the E2E tests and the verification script
/// (<c>LayerPeek E2E.app</c>, no <c>.icns</c>) otherwise show the generic icon of an executable while a window makes the
/// app a regular app (T-102). Avalonia never sets the application icon (no <c>setApplicationIconImage:</c> in
/// <c>libAvaloniaNative</c> 12.1.4), and <c>Window.Icon</c> (the Windows <c>.ico</c>) is not used by macOS. The released
/// <c>LayerPeek.app</c> has <c>LayerPeek.icns</c> and is left alone.
/// </summary>
/// <remarks>Only an existing <c>NSApp</c> is changed. UI (main) thread only. Never throws.</remarks>
[SupportedOSPlatform("macos")]
internal static class MacAppIcon
{
    /// <summary>The 1024 px master of the macOS icon (Apple's grid, T-098), the image of the <c>.icns</c>.</summary>
    internal static readonly Uri MasterUri = new("avares://LayerPeek/Assets/AppIcon/app-icon-macos-1024.png");

    /// <summary>Sets the application icon from <see cref="MasterUri"/> when the main bundle has no icon file.</summary>
    public static void ApplyWhenTheBundleHasNone()
    {
        try
        {
            nint application = MacAppActivation.RunningApplication();
            if (application == 0)
            {
                return;
            }

            ObjCRuntime.InAutoreleasePool(() =>
            {
                if (!HasBundleIcon())
                {
                    SetIcon(application);
                }

                return 0;
            });
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or IOException)
        {
            // Not an AppKit process, or the image is missing: the icon stays as it is.
        }
    }

    /// <summary>Whether <c>CFBundleIconFile</c> of the main bundle names an existing resource.</summary>
    private static bool HasBundleIcon()
    {
        nint bundle = ObjCRuntime.Send(ObjCRuntime.Class("NSBundle"), ObjCRuntime.Selector("mainBundle"));
        nint name = bundle == 0 ? 0 : ObjCRuntime.Send(bundle, ObjCRuntime.Selector("objectForInfoDictionaryKey:"), ObjCRuntime.NSString("CFBundleIconFile"));
        if (name == 0 || !ObjCRuntime.IsKindOf(name, "NSString"))
        {
            return false;
        }

        // "LayerPeek.icns" or "LayerPeek": the extension is optional in Info.plist.
        string file = ObjCRuntime.ToText(name) ?? string.Empty;
        string type = Path.HasExtension(file) ? string.Empty : "icns";
        nint path = ObjCRuntime.Send(bundle, ObjCRuntime.Selector("pathForResource:ofType:"), ObjCRuntime.NSString(file), ObjCRuntime.NSString(type));
        return path != 0;
    }

    private static unsafe void SetIcon(nint application)
    {
        byte[] png;
        using (Stream stream = AssetLoader.Open(MasterUri))
        using (MemoryStream copy = new())
        {
            stream.CopyTo(copy);
            png = copy.ToArray();
        }

        nint image;
        fixed (byte* bytes = png)
        {
            nint data = ObjCRuntime.Send(ObjCRuntime.Class("NSData"), ObjCRuntime.Selector("dataWithBytes:length:"), (nint)bytes, png.Length);
            image = ObjCRuntime.Send(ObjCRuntime.Send(ObjCRuntime.Class("NSImage"), ObjCRuntime.Selector("alloc")), ObjCRuntime.Selector("initWithData:"), data);
        }

        if (image == 0)
        {
            return;
        }

        // NSApp retains the image.
        _ = ObjCRuntime.Send(application, ObjCRuntime.Selector("setApplicationIconImage:"), image);
        ObjCRuntime.Release(image);
    }
}
