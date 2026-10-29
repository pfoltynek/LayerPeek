using Avalonia.Controls;

namespace LayerPeek.App.Tray.Icons;

/// <summary>
/// macOS menu bar template icons (T-018): black and alpha only, rendered at runtime (<see cref="MenuBarIconRenderer"/>)
/// and cached per key. The tray icon must be marked as a template (<see cref="TrayIconSets.Prepare"/>), otherwise macOS
/// shows them black even on a dark menu bar.
/// </summary>
internal sealed class TemplateTrayIconSet : ITrayIconSet
{
    private readonly TrayIconCache<WindowIcon> _cache =
        new(static key => MenuBarIconRenderer.RenderIcon(MenuBarIconAppearance.For(key)));

    public bool IsTemplate => true;

    public WindowIcon GetIcon(TrayIconKey key) => _cache.Get(key);

    public void Dispose() => _cache.Dispose();
}
