using Avalonia.Controls;

namespace LayerPeek.App.Tray.Icons;

/// <summary>Chooses the icon set of the platform and prepares the tray icon for it (T-018). UI thread only.</summary>
internal static class TrayIconSets
{
    /// <summary>
    /// macOS: menu bar template icons (<see cref="TemplateTrayIconSet"/>). Windows: the coloured multi-size icons of T-007
    /// (<see cref="ColorTrayIconSet"/>). Any other OS: the coloured set as one bitmap.
    /// </summary>
    public static ITrayIconSet ForPlatform(bool windows, bool macOS) =>
        macOS ? new TemplateTrayIconSet() : new ColorTrayIconSet(multiSize: windows);

    /// <inheritdoc cref="ForPlatform(bool, bool)"/>
    public static ITrayIconSet ForCurrentPlatform() => ForPlatform(OperatingSystem.IsWindows(), OperatingSystem.IsMacOS());

    /// <summary>
    /// Marks <paramref name="trayIcon"/> as a template image when <paramref name="iconSet"/> draws template icons, so that
    /// macOS recolours it for the light, dark and highlighted menu bar. Avalonia applies the flag to the current and
    /// every later image. Nothing is set for other sets (the Windows tray icon is left exactly as before).
    /// </summary>
    public static void Prepare(TrayIcon trayIcon, ITrayIconSet iconSet)
    {
        ArgumentNullException.ThrowIfNull(trayIcon);
        ArgumentNullException.ThrowIfNull(iconSet);
        if (iconSet.IsTemplate)
        {
            MacOSProperties.SetIsTemplateIcon(trayIcon, true);
        }
    }
}
