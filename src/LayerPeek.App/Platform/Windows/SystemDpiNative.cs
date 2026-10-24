using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LayerPeek.App.Platform.Windows;

/// <summary>
/// The system DPI (the scaling of the primary monitor when the user signed in), for bitmaps whose target window does not
/// exist yet: the tray menu (T-039) opens as a new popup where the mouse is, so its monitor is unknown in advance.
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class SystemDpiNative
{
    private const double DefaultDpi = 96;

    /// <summary>The system scaling (1.0 = 100 %); 1.0 when Windows cannot tell.</summary>
    public static double GetScaling()
    {
        try
        {
            uint dpi = GetDpiForSystem();
            return dpi == 0 ? 1.0 : dpi / DefaultDpi;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Before Windows 10 1607.
            return 1.0;
        }
    }

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForSystem();
}
