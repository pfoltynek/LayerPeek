using Avalonia.Controls;

namespace LayerPeek.App.Tray.Icons;

/// <summary>Coloured icons rendered at runtime (<see cref="ColorTrayIconPalette"/>) and cached per key.</summary>
/// <param name="multiSize">
/// One image per notification-area size (<see cref="TrayIconLayout.Sizes"/>); used on Windows, where the closest one is
/// picked for the scaling of the taskbar. Otherwise one 32 px bitmap (macOS menu bar icons follow in T-018).
/// </param>
internal sealed class ColorTrayIconSet(bool multiSize) : ITrayIconSet
{
    private readonly TrayIconCache<WindowIcon> _cache =
        new(key => TrayIconRenderer.RenderIcon(ColorTrayIconPalette.GetAppearance(key), multiSize));

    public WindowIcon GetIcon(TrayIconKey key) => _cache.Get(key);

    public void Dispose() => _cache.Dispose();
}
