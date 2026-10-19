using Avalonia.Controls;
using Avalonia.Platform;

namespace PrusaTrayDiagnostic.App;

/// <summary>
/// The application icon (T-025): one multi-size <c>.ico</c> (16–256 px) used by every window (style in App.axaml) and
/// as the static tray icon until the first state icon is drawn. The same file is the exe icon (<c>ApplicationIcon</c>).
/// On Windows Avalonia picks the closest entry per use: 16 px × scaling for the title bar and the notification area,
/// 24 px × scaling for the taskbar.
/// </summary>
public static class AppIcon
{
    public static readonly Uri AssetUri = new("avares://PrusaTrayDiagnostic/Assets/AppIcon/app-icon.ico");

    /// <summary>Loaded on first use (the Avalonia platform must be initialized) and shared by all windows.</summary>
    public static WindowIcon Window => field ??= Load();

    private static WindowIcon Load()
    {
        using Stream stream = AssetLoader.Open(AssetUri);
        return new WindowIcon(stream);
    }
}
