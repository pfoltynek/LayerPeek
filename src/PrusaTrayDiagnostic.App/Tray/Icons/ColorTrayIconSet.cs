using Avalonia.Controls;

namespace PrusaTrayDiagnostic.App.Tray.Icons;

/// <summary>Coloured icons rendered at runtime (<see cref="ColorTrayIconPalette"/>) and cached per key.</summary>
internal sealed class ColorTrayIconSet : ITrayIconSet
{
    private readonly TrayIconCache<WindowIcon> _cache =
        new(static key => TrayIconRenderer.Render(ColorTrayIconPalette.GetAppearance(key)));

    public WindowIcon GetIcon(TrayIconKey key) => _cache.Get(key);

    public void Dispose() => _cache.Dispose();
}
