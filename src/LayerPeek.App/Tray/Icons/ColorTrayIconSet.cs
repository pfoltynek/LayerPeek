using Avalonia.Controls;

namespace LayerPeek.App.Tray.Icons;

/// <summary>Coloured icons rendered at runtime (<see cref="ColorTrayIconPalette"/>) and cached per key.</summary>
/// <param name="multiSize">
/// One image per notification-area size (<see cref="TrayIconLayout.Sizes"/>); used on Windows, where the closest one is
/// picked for the scaling of the taskbar. Otherwise one 32 px bitmap (an OS other than Windows and macOS; macOS uses
/// <see cref="TemplateTrayIconSet"/>).
/// </param>
internal sealed class ColorTrayIconSet : ITrayIconSet
{
    private readonly TrayIconCache<WindowIcon> _cache;

    public ColorTrayIconSet(bool multiSize)
    {
        MultiSize = multiSize;
        _cache = new(key => TrayIconRenderer.RenderIcon(ColorTrayIconPalette.GetAppearance(key), multiSize));
    }

    /// <summary>One image per notification-area size (Windows) instead of one 32 px bitmap.</summary>
    public bool MultiSize { get; }

    public bool IsTemplate => false;

    public WindowIcon GetIcon(TrayIconKey key) => _cache.Get(key);

    public void Dispose() => _cache.Dispose();
}
