using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LayerPeek.App.Platform.MacOS;

/// <summary>
/// The active displays of macOS (T-096) through CoreGraphics <c>CGGetActiveDisplayList</c>: the displays that are
/// "drawable", i.e. connected and awake; a sleeping display is online but not active. It is the same set that
/// <c>CVDisplayLinkCreateWithActiveCGDisplays</c> of Avalonia.Native needs at least one of (otherwise
/// <c>kCVReturnInvalidDisplay</c>, -6661). Loaded by absolute path; blittable signature (<c>[LibraryImport]</c>, Native AOT safe).
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed unsafe partial class MacActiveDisplays : IActiveDisplays
{
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    public bool TryCount(out uint count, out int error)
    {
        uint found = 0;
        // No list, only the count (documented: activeDisplays NULL and maxDisplays 0).
        error = CGGetActiveDisplayList(0, null, &found);
        count = error == 0 ? found : 0;
        return error == 0;
    }

    // CGError CGGetActiveDisplayList(uint32_t maxDisplays, CGDirectDisplayID *activeDisplays, uint32_t *displayCount);
    [LibraryImport(CoreGraphics, EntryPoint = "CGGetActiveDisplayList")]
    private static partial int CGGetActiveDisplayList(uint maxDisplays, uint* activeDisplays, uint* displayCount);
}
