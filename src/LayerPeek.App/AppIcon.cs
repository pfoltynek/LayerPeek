using Avalonia.Controls;
using Avalonia.Platform;

namespace LayerPeek.App;

/// <summary>
/// The application icon (T-025): one multi-size <c>.ico</c> (16–256 px) used by every window (style in App.axaml) and
/// as the static tray icon until the first state icon is drawn. The same file is the exe icon (<c>ApplicationIcon</c>).
/// On Windows Avalonia picks the closest entry per use: 16 px × scaling for the title bar and the notification area,
/// 24 px × scaling for the taskbar.
/// </summary>
public static class AppIcon
{
    public static readonly Uri AssetUri = new("avares://LayerPeek/Assets/AppIcon/app-icon.ico");

    /// <summary>
    /// The 1024 px master of the macOS icon (Apple's grid, T-098; the image of <c>LayerPeek.icns</c>): the Dock icon of a
    /// run whose bundle has no icon (T-139, <c>MacAppIcon</c>).
    /// </summary>
    public static readonly Uri MacMasterUri = new("avares://LayerPeek/Assets/AppIcon/app-icon-macos-1024.png");

    /// <summary>
    /// Loaded on first use (the Avalonia platform must be initialized) and shared by all windows. Published atomically
    /// (T-131), so concurrent first uses all get the same instance; a failed load is not cached and is retried.
    /// </summary>
    public static WindowIcon Window => LazyInitializer.EnsureInitialized(ref field, Load);

    private static WindowIcon Load()
    {
        using Stream stream = AssetLoader.Open(AssetUri);
        return new WindowIcon(stream);
    }
}
